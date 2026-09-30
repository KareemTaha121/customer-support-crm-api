using System.Net;
using CustomerSupportCrm.Application.Abstractions.Channels;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Messaging;
using CustomerSupportCrm.Application.Abstractions.Notifications;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Tickets.Common;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Tickets;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CustomerSupportCrm.Application.Features.Channels;

/// <summary>Localized (en/ar) customer-facing texts with organization branding.</summary>
internal static class CustomerTemplates
{
    public sealed record Rendered(string Subject, string Text, string Html);

    public static Rendered Build(string language, string organization, string color, string subject, string heading, string body, string? linkUrl, string? linkLabel)
    {
        var rtl = language == "ar";
        var text = $"{heading}\n\n{body}" + (linkUrl is null ? string.Empty : $"\n\n{linkLabel}: {linkUrl}") + $"\n\n— {organization}";
        var html = $"""
            <!doctype html><html lang="{language}" dir="{(rtl ? "rtl" : "ltr")}"><body style="font-family:Segoe UI,Tahoma,Arial,sans-serif;background:#f5f6f8;padding:24px">
            <div style="max-width:560px;margin:auto;background:#fff;border-radius:8px;overflow:hidden;border:1px solid #e3e5e8">
            <div style="background:{color};color:#fff;padding:16px 24px;font-size:18px;font-weight:600">{WebUtility.HtmlEncode(organization)}</div>
            <div style="padding:24px;color:#1f2328;line-height:1.6">
            <h2 style="margin-top:0;font-size:18px">{WebUtility.HtmlEncode(heading)}</h2>
            <div style="white-space:pre-wrap">{WebUtility.HtmlEncode(body)}</div>
            {(linkUrl is null ? string.Empty : $"<p><a href=\"{WebUtility.HtmlEncode(linkUrl)}\" style=\"display:inline-block;background:{color};color:#fff;padding:10px 16px;border-radius:6px;text-decoration:none\">{WebUtility.HtmlEncode(linkLabel)}</a></p>")}
            </div></div></body></html>
            """;
        return new Rendered(subject, text, html);
    }

    public static (string Heading, string Link) AgentReply(string language) =>
        language == "ar" ? ("رد جديد على طلبك", "عرض الطلب") : ("New reply on your request", "View request");

    public static (string Heading, string Body, string Link) Received(string language, string number) =>
        language == "ar"
            ? ("تم استلام طلبك", $"شكراً لتواصلك معنا. رقم طلبك هو {number} وسيقوم فريقنا بالرد عليك قريباً.", "متابعة الطلب")
            : ("We received your request", $"Thank you for contacting us. Your request number is {number} and our team will get back to you soon.", "Track request");

    public static (string Heading, string Body, string Link) Resolved(string language, string number) =>
        language == "ar"
            ? ("تم حل طلبك", $"تم حل طلبك رقم {number}. نرجو تقييم تجربتك، ويمكنك الرد إذا احتجت مزيداً من المساعدة.", "قيّم الخدمة")
            : ("Your request was resolved", $"Request {number} has been resolved. Please rate your experience, or reply if you need more help.", "Rate our service");

    public static (string Subject, string Heading, string Body) Verification(string language, string code) =>
        language == "ar"
            ? ("رمز التحقق", "تأكيد البريد الإلكتروني", $"رمز التحقق الخاص بك هو: {code}\nصالح لمدة 24 ساعة.")
            : ("Your verification code", "Confirm your email", $"Your verification code is: {code}\nIt is valid for 24 hours.");
}

/// <summary>
/// Queues customer-facing messages in the outbox, choosing the channel from the ticket.
/// Messaging channels (email, WhatsApp, SMS) carry the reply itself; other channels get an
/// email notification when the customer has an address.
/// </summary>
public sealed class CustomerMessenger(IApplicationDbContext db, IOptions<CustomerPortalOptions> portal, TimeProvider time)
{
    public async Task QueueAgentReplyAsync(Ticket ticket, TicketMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(message);
        var context = await ContextAsync(ticket, cancellationToken);
        var now = time.GetUtcNow();

        switch (ticket.Channel)
        {
            case TicketChannel.WhatsApp or TicketChannel.Sms when ticket.ReplyAddress is { } phone:
                db.OutboundMessages.Add(OutboundMessage.Queue(ticket.Channel, phone, null, message.Body, null, ticket.Id, message.Id, now));
                break;
            case TicketChannel.Chat:
                // Delivered live over SignalR; nothing to queue.
                break;
            default:
                var email = ticket.ReplyAddress ?? context.Email;
                if (email is not null && email.Contains('@', StringComparison.Ordinal))
                {
                    var (heading, link) = CustomerTemplates.AgentReply(context.Language);
                    var rendered = CustomerTemplates.Build(context.Language, context.Organization, context.Color, EmailSubject(ticket), heading, message.Body, TicketUrl(ticket), link);
                    db.OutboundMessages.Add(OutboundMessage.Queue(TicketChannel.Email, email, rendered.Subject, rendered.Text, rendered.Html, ticket.Id, message.Id, now));
                }

                break;
        }
    }

    public async Task QueueReceivedAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var context = await ContextAsync(ticket, cancellationToken);
        var (heading, body, link) = CustomerTemplates.Received(context.Language, ticket.Number);
        QueueNotice(ticket, context, heading, body, link);
    }

    public async Task QueueResolvedAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var context = await ContextAsync(ticket, cancellationToken);
        var (heading, body, link) = CustomerTemplates.Resolved(context.Language, ticket.Number);
        QueueNotice(ticket, context, heading, body, link);
    }

    public async Task QueueVerificationCodeAsync(string email, string language, string code, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.AsNoTracking().Select(o => new { o.Name, o.PrimaryColor }).FirstOrDefaultAsync(cancellationToken);
        var (subject, heading, body) = CustomerTemplates.Verification(language, code);
        var rendered = CustomerTemplates.Build(language, organization?.Name ?? "Support", organization?.PrimaryColor ?? "#1F6FEB", subject, heading, body, null, null);
        db.OutboundMessages.Add(OutboundMessage.Queue(TicketChannel.Email, email, rendered.Subject, rendered.Text, rendered.Html, null, null, time.GetUtcNow()));
    }

    /// <summary>Email subject carrying the ticket number, used to thread customer replies.</summary>
    public static string EmailSubject(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return $"[{ticket.Number}] {ticket.Subject}";
    }

    private void QueueNotice(Ticket ticket, MessagingContext context, string heading, string body, string link)
    {
        var now = time.GetUtcNow();
        if (ticket.Channel is TicketChannel.WhatsApp or TicketChannel.Sms && ticket.ReplyAddress is { } phone)
        {
            db.OutboundMessages.Add(OutboundMessage.Queue(ticket.Channel, phone, null, $"{heading}\n{body}\n{TicketUrl(ticket)}", null, ticket.Id, null, now));
            return;
        }

        var email = ticket.ReplyAddress is { } reply && reply.Contains('@', StringComparison.Ordinal) ? reply : context.Email;
        if (email is null)
        {
            return;
        }

        var rendered = CustomerTemplates.Build(context.Language, context.Organization, context.Color, EmailSubject(ticket), heading, body, TicketUrl(ticket), link);
        db.OutboundMessages.Add(OutboundMessage.Queue(TicketChannel.Email, email, rendered.Subject, rendered.Text, rendered.Html, ticket.Id, null, now));
    }

    private string TicketUrl(Ticket ticket) => $"{portal.Value.BaseUrl.TrimEnd('/')}/tickets/{ticket.Id}";

    private async Task<MessagingContext> ContextAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Id == ticket.CustomerId)
            .Select(c => new { c.PreferredLanguage, c.PrimaryEmail })
            .FirstOrDefaultAsync(cancellationToken);
        var organization = await db.Organizations.AsNoTracking().Select(o => new { o.Name, o.PrimaryColor }).FirstOrDefaultAsync(cancellationToken);
        return new MessagingContext(customer?.PreferredLanguage ?? "en", customer?.PrimaryEmail, organization?.Name ?? "Support", organization?.PrimaryColor ?? "#1F6FEB");
    }

    private sealed record MessagingContext(string Language, string? Email, string Organization, string Color);
}

// ---------- Event handlers ----------

internal sealed class ChannelDeliveryHandlers(IApplicationDbContext db, CustomerMessenger messenger, IRealtimeNotifier realtime)
    : INotificationHandler<DomainEventNotification<TicketMessageAddedDomainEvent>>,
      INotificationHandler<DomainEventNotification<TicketCreatedDomainEvent>>,
      INotificationHandler<DomainEventNotification<TicketStatusChangedDomainEvent>>
{
    public async Task Handle(DomainEventNotification<TicketMessageAddedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        if (e.IsInternal)
        {
            return;
        }

        var ticket = await TicketQueries.FindTrackedAsync(db, e.TicketId, cancellationToken);
        var message = db.TicketMessages.Local.FirstOrDefault(m => m.Id == e.MessageId);
        if (ticket is null || message is null)
        {
            return;
        }

        if (e.AuthorType == MessageAuthorType.Agent)
        {
            await messenger.QueueAgentReplyAsync(ticket, message, cancellationToken);
        }

        if (ticket.Channel == TicketChannel.Chat)
        {
            var conversationId = await db.ChatConversations.Where(c => c.TicketId == ticket.Id).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(cancellationToken);
            if (conversationId is { } id)
            {
                await realtime.SendToGroupAsync(
                    RealtimeGroups.ChatConversation(id),
                    RealtimeEvents.ChatMessage,
                    new { conversationId = id, messageId = message.Id, authorType = message.AuthorType.ToString(), body = message.Body, createdAt = message.CreatedAt },
                    cancellationToken);
            }
        }
    }

    public async Task Handle(DomainEventNotification<TicketCreatedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        if (e.Channel is TicketChannel.Email or TicketChannel.WebForm or TicketChannel.Portal or TicketChannel.WhatsApp or TicketChannel.Sms)
        {
            var ticket = await TicketQueries.FindTrackedAsync(db, e.TicketId, cancellationToken);
            if (ticket is not null)
            {
                await messenger.QueueReceivedAsync(ticket, cancellationToken);
            }
        }
    }

    public async Task Handle(DomainEventNotification<TicketStatusChangedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        if (e.Status == TicketStatus.Resolved)
        {
            var ticket = await TicketQueries.FindTrackedAsync(db, e.TicketId, cancellationToken);
            if (ticket is not null && ticket.Channel != TicketChannel.Chat)
            {
                await messenger.QueueResolvedAsync(ticket, cancellationToken);
            }
        }
    }
}

// ---------- Outbox dispatch ----------

/// <summary>Sends due outbox rows through the configured providers (runs every few seconds).</summary>
public sealed record DispatchOutboxCommand : IRequest;

internal sealed class DispatchOutboxHandler(IApplicationDbContext db, IEnumerable<IMessageSender> senders, TimeProvider time) : IRequestHandler<DispatchOutboxCommand>
{
    private const int BatchSize = 50;

    public async Task Handle(DispatchOutboxCommand request, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var due = await db.OutboundMessages
            .Where(m => m.Status == OutboundStatus.Pending && m.NextAttemptAt <= now)
            .OrderBy(m => m.NextAttemptAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in due)
        {
            var sender = senders.FirstOrDefault(s => s.Channel == message.Channel);
            if (sender is null || !sender.IsConfigured)
            {
                message.MarkFailed($"The {message.Channel} channel is not configured.", now, permanent: true);
                continue;
            }

            SendResult result;
            try
            {
                result = await sender.SendAsync(new OutboundEnvelope(message.To, message.Subject, message.Body, message.HtmlBody), cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TimeoutException or System.Net.Mail.SmtpException or IOException)
            {
                result = SendResult.Failed(exception.Message, permanent: false);
            }

            if (result.Succeeded)
            {
                message.MarkSent(result.ProviderMessageId, time.GetUtcNow());
            }
            else
            {
                message.MarkFailed(result.Error ?? "Unknown error", time.GetUtcNow(), result.Permanent);
            }

            // Persist each outcome immediately so a crash never re-sends delivered messages.
            await db.SaveChangesAsync(cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

// ---------- Channel administration ----------

public sealed record ChannelStatusResponse(string Channel, bool Configured);

public sealed record OutboundMessageResponse(Guid Id, string Channel, string To, string? Subject, string Status, int Attempts, string? LastError, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, Guid? TicketId);

public sealed record TestChannelRequest(string Channel, string To);

public sealed record SendChannelTestCommand(string Channel, string To) : IRequest;

internal sealed class SendChannelTestValidator : AbstractValidator<SendChannelTestCommand>
{
    public SendChannelTestValidator()
    {
        RuleFor(c => c.Channel).Must(c => c is "Email" or "WhatsApp" or "Sms").WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(c => c.To).NotEmpty().MaximumLength(320);
    }
}

internal sealed class SendChannelTestHandler(IApplicationDbContext db, TimeProvider time) : IRequestHandler<SendChannelTestCommand>
{
    public async Task Handle(SendChannelTestCommand request, CancellationToken cancellationToken)
    {
        var channel = Enum.Parse<TicketChannel>(request.Channel);
        db.OutboundMessages.Add(OutboundMessage.Queue(channel, request.To, "Test message", "This is a test message from your customer support system.", null, null, null, time.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class ChannelAdministrationEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/channels").WithTags("Channels").RequireAuthorization(Permissions.ChannelsManage);

        group.MapGet("/status", (IEnumerable<IMessageSender> senders) =>
                ApiResults.Ok<IReadOnlyList<ChannelStatusResponse>>([.. senders.Select(s => new ChannelStatusResponse(s.Channel.ToString(), s.IsConfigured))]))
            .WithName("GetChannelStatus")
            .Produces<ApiResponse<IReadOnlyList<ChannelStatusResponse>>>();

        group.MapPost("/test", async (TestChannelRequest request, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new SendChannelTestCommand(request.Channel, request.To), ct);
                return ApiResults.Success();
            })
            .WithName("SendChannelTest")
            .Produces<ApiResponse<object?>>();

        group.MapGet("/outbox", async (string? status, int? page, IApplicationDbContext db, CancellationToken ct) =>
            {
                var query = db.OutboundMessages.AsNoTracking();
                if (Enum.TryParse<OutboundStatus>(status, out var parsed))
                {
                    query = query.Where(m => m.Status == parsed);
                }

                var pageNumber = Math.Max(page ?? 1, 1);
                var total = await query.LongCountAsync(ct);
                var items = await query.OrderByDescending(m => m.CreatedAt).Skip((pageNumber - 1) * 50).Take(50)
                    .Select(m => new OutboundMessageResponse(m.Id, m.Channel.ToString(), m.To, m.Subject, m.Status.ToString(), m.Attempts, m.LastError, m.CreatedAt, m.SentAt, m.TicketId))
                    .ToListAsync(ct);
                return ApiResults.Paged<OutboundMessageResponse>(items, PaginationMeta.Create(pageNumber, 50, total));
            })
            .WithName("ListOutboundMessages")
            .Produces<ApiResponse<IReadOnlyList<OutboundMessageResponse>>>();

        group.MapPost("/outbox/{id:guid}/retry", async (Guid id, IApplicationDbContext db, TimeProvider time, CancellationToken ct) =>
            {
                var message = await db.OutboundMessages.SingleOrDefaultAsync(m => m.Id == id, ct)
                    ?? throw new NotFoundException("OUTBOUND_MESSAGE_NOT_FOUND", "The message was not found.");
                message.Retry(time.GetUtcNow());
                await db.SaveChangesAsync(ct);
                return ApiResults.Success();
            })
            .WithName("RetryOutboundMessage")
            .Produces<ApiResponse<object?>>();
    }
}
