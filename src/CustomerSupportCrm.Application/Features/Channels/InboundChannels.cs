using System.Text.RegularExpressions;
using CustomerSupportCrm.Application.Abstractions.Channels;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Tickets;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Domain.Customers;
using CustomerSupportCrm.Domain.Shared;
using CustomerSupportCrm.Domain.Tickets;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Channels;

/// <summary>
/// Provider-specific webhook handling (signature verification and payload parsing) for one
/// inbound channel. Implementations live in Infrastructure.
/// </summary>
public interface IChannelWebhookAdapter
{
    TicketChannel Channel { get; }

    /// <summary>Subscription handshake (GET). Returns the challenge to echo, or null to reject.</summary>
    string? Handshake(HttpRequest request);

    /// <summary>Verifies the request and returns its messages, or null when verification fails.</summary>
    Task<IReadOnlyList<InboundChannelMessage>?> ParseAsync(HttpRequest request, CancellationToken cancellationToken);
}

/// <summary>Finds or creates the customer behind an address (email or E.164 phone).</summary>
public sealed class CustomerResolver(IApplicationDbContext db, ISequenceGenerator sequences)
{
    public async Task<Customer> ResolveAsync(ContactType type, string address, string? name, Guid? preferredDepartmentId, CancellationToken cancellationToken)
    {
        var value = CustomerContact.NormalizeValue(type, address);
        var matchTypes = type is ContactType.Phone or ContactType.WhatsApp ? new[] { ContactType.Phone, ContactType.WhatsApp } : [type];

        var existing = await db.Customers
            .Include(c => c.Contacts)
            .Where(c => c.Contacts.Any(x => matchTypes.Contains(x.Type) && x.Value == value))
            .OrderBy(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (branchId, departmentId) = await DefaultUnitAsync(preferredDepartmentId, cancellationToken);
        var number = Customer.FormatNumber(await sequences.NextValueAsync(Sequences.CustomerNumbers, cancellationToken));
        var customer = Customer.Create(number, CustomerType.Individual, string.IsNullOrWhiteSpace(name) ? value : name.Trim(), null, "en", ["auto-created"], branchId, departmentId);
        customer.AddContact(type, value, null, isPrimary: true);
        db.Customers.Add(customer);
        return customer;
    }

    /// <summary>The routed department (e.g. by inbound address) or the oldest active branch.</summary>
    public async Task<(Guid BranchId, Guid? DepartmentId)> DefaultUnitAsync(Guid? preferredDepartmentId, CancellationToken cancellationToken)
    {
        if (preferredDepartmentId is { } departmentId)
        {
            var department = await db.Departments.AsNoTracking().Where(d => d.Id == departmentId && d.IsActive).Select(d => new { d.Id, d.BranchId }).FirstOrDefaultAsync(cancellationToken);
            if (department is not null)
            {
                return (department.BranchId, department.Id);
            }
        }

        var branchId = await db.Branches.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.CreatedAt).Select(b => (Guid?)b.Id).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("NO_ACTIVE_BRANCH", "No active branch is configured.");
        return (branchId, null);
    }
}

/// <summary>
/// Turns a normalized inbound message into a new ticket or a reply on an existing one.
/// Email threads by the [T-000123] subject tag; WhatsApp/SMS continue the customer's latest
/// active conversation on the same channel within 7 days.
/// </summary>
public sealed record ProcessInboundMessageCommand(InboundChannelMessage Message) : IRequest<Guid?>;

internal sealed partial class ProcessInboundMessageHandler(
    IApplicationDbContext db,
    CustomerResolver customers,
    TicketFactory tickets,
    TicketMessageWriter messages,
    TimeProvider time)
    : IRequestHandler<ProcessInboundMessageCommand, Guid?>
{
    private static readonly TimeSpan ConversationWindow = TimeSpan.FromDays(7);

    public async Task<Guid?> Handle(ProcessInboundMessageCommand request, CancellationToken cancellationToken)
    {
        var message = request.Message;
        if (await db.InboundReceipts.AnyAsync(r => r.Channel == message.Channel && r.ExternalId == message.ExternalId, cancellationToken))
        {
            return null;
        }

        var contactType = message.Channel switch
        {
            TicketChannel.Email => ContactType.Email,
            TicketChannel.WhatsApp => ContactType.WhatsApp,
            _ => ContactType.Phone,
        };

        Guid? routedDepartment = null;
        if (message.Channel == TicketChannel.Email && message.To is { } to)
        {
            var address = EmailAddress.Normalize(to);
            routedDepartment = await db.Departments.Where(d => d.Email == address && d.IsActive).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(cancellationToken);
        }

        var customer = await customers.ResolveAsync(contactType, message.From, message.FromName, routedDepartment, cancellationToken);
        var body = string.IsNullOrWhiteSpace(message.Body) ? "(empty message)" : message.Body.Trim();
        var ticket = await FindThreadAsync(customer.Id, message, cancellationToken);

        if (ticket is null || ticket.Status == TicketStatus.Closed)
        {
            var subject = message.Subject is { Length: > 0 } s ? s : body.Length > 80 ? body[..80] + "…" : body;
            var (branchId, departmentId) = routedDepartment is not null
                ? await customers.DefaultUnitAsync(routedDepartment, cancellationToken)
                : (customer.BranchId, customer.DepartmentId);

            ticket = await tickets.CreateAsync(
                new NewTicket(customer.Id, subject[..Math.Min(subject.Length, Ticket.SubjectMaxLength)], body, null, TicketPriority.Medium, message.Channel, branchId, departmentId, [], CustomerContact.NormalizeValue(contactType, message.From)),
                scope: null,
                cancellationToken);
        }
        else
        {
            await messages.AddAsync(ticket, MessageAuthorType.Customer, null, customer.Id, body, false, message.Channel, message.ExternalId, [], [], cancellationToken);
        }

        db.InboundReceipts.Add(InboundReceipt.Record(message.Channel, message.ExternalId, ticket.Id, time.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return ticket.Id;
    }

    private async Task<Ticket?> FindThreadAsync(Guid customerId, InboundChannelMessage message, CancellationToken cancellationToken)
    {
        if (message.Channel == TicketChannel.Email)
        {
            var match = message.Subject is null ? null : TicketTag().Match(message.Subject);
            if (match is { Success: true })
            {
                var number = match.Groups[1].Value;
                return await db.Tickets.SingleOrDefaultAsync(t => t.Number == number && t.CustomerId == customerId, cancellationToken);
            }

            return null;
        }

        var since = time.GetUtcNow() - ConversationWindow;
        return await db.Tickets
            .Where(t => t.CustomerId == customerId && t.Channel == message.Channel && t.Status != TicketStatus.Closed && (t.UpdatedAt ?? t.CreatedAt) >= since)
            .OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    [GeneratedRegex(@"\[(T-\d{6,})\]")]
    private static partial Regex TicketTag();
}

internal sealed class ChannelWebhookEndpoints : IPublicEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/channels/{channel}/webhook").WithTags("Channels");

        group.MapGet("/", (string channel, HttpRequest request, IEnumerable<IChannelWebhookAdapter> adapters) =>
            {
                var adapter = Find(adapters, channel);
                return adapter?.Handshake(request) is { } challenge ? Results.Text(challenge) : Results.Forbid();
            })
            .WithName("ChannelWebhookHandshake");

        group.MapPost("/", async (string channel, HttpRequest request, IEnumerable<IChannelWebhookAdapter> adapters, ISender sender, CancellationToken ct) =>
            {
                var adapter = Find(adapters, channel);
                if (adapter is null)
                {
                    return Results.NotFound();
                }

                var messages = await adapter.ParseAsync(request, ct);
                if (messages is null)
                {
                    return Results.Unauthorized();
                }

                foreach (var message in messages)
                {
                    await sender.Send(new ProcessInboundMessageCommand(message), ct);
                }

                // Providers expect a fast 200; the body is irrelevant to them.
                return Results.Ok();
            })
            .DisableAntiforgery()
            .WithName("ChannelWebhook");
    }

    private static IChannelWebhookAdapter? Find(IEnumerable<IChannelWebhookAdapter> adapters, string channel) =>
        adapters.FirstOrDefault(a => string.Equals(a.Channel.ToString(), channel, StringComparison.OrdinalIgnoreCase));
}

// ---------- Web forms ----------

/// <param name="Website">Honeypot: must stay empty (bots fill every field).</param>
public sealed record WebFormTicketRequest(string Name, string Email, string? Phone, string Subject, string Message, Guid? CategoryId, string? Language, string? Website);

public sealed record WebFormTicketResponse(string TicketNumber);

public sealed record SubmitWebFormCommand(WebFormTicketRequest Form) : IRequest<WebFormTicketResponse>;

internal sealed class SubmitWebFormValidator : AbstractValidator<SubmitWebFormCommand>
{
    public SubmitWebFormValidator()
    {
        RuleFor(c => c.Form.Name).NotEmpty().MaximumLength(Customer.NameMaxLength);
        RuleFor(c => c.Form.Email).NotEmpty().EmailAddress().MaximumLength(EmailAddress.MaxLength);
        RuleFor(c => c.Form.Phone).MaximumLength(32);
        RuleFor(c => c.Form.Subject).NotEmpty().MaximumLength(Ticket.SubjectMaxLength);
        RuleFor(c => c.Form.Message).NotEmpty().MaximumLength(10_000);
        RuleFor(c => c.Form.Website).Empty().WithErrorCode(ErrorCodes.Invalid);
        RuleFor(c => c.Form.Language).Must(l => l is null or "en" or "ar").WithErrorCode(ErrorCodes.InvalidValue);
    }
}

/// <summary>
/// Anonymous web-form submission. The customer is matched by email but nothing about the
/// existing record is revealed; follow-up happens by email or after portal verification.
/// </summary>
internal sealed class SubmitWebFormHandler(IApplicationDbContext db, CustomerResolver customers, TicketFactory tickets) : IRequestHandler<SubmitWebFormCommand, WebFormTicketResponse>
{
    public async Task<WebFormTicketResponse> Handle(SubmitWebFormCommand request, CancellationToken cancellationToken)
    {
        var form = request.Form;
        var customer = await customers.ResolveAsync(ContactType.Email, form.Email, form.Name, null, cancellationToken);

        if (!string.IsNullOrWhiteSpace(form.Phone) && PhoneNumber.TryCreate(form.Phone, out var phone)
            && !customer.Contacts.Any(c => c.Type == ContactType.Phone && c.Value == phone!.Value))
        {
            customer.AddContact(ContactType.Phone, phone!.Value, "web form", isPrimary: false);
        }

        var categoryId = form.CategoryId is { } id && await db.TicketCategories.AnyAsync(c => c.Id == id && c.IsActive, cancellationToken) ? id : (Guid?)null;
        var ticket = await tickets.CreateAsync(
            new NewTicket(customer.Id, form.Subject, form.Message, categoryId, TicketPriority.Medium, TicketChannel.WebForm, null, null, [], EmailAddress.Normalize(form.Email)),
            scope: null,
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return new WebFormTicketResponse(ticket.Number);
    }
}

internal sealed class WebFormEndpoints : IPublicEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/web-forms/tickets", async (WebFormTicketRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Created("/api/v1/public/web-forms/tickets", await sender.Send(new SubmitWebFormCommand(request), ct)))
            .RequireRateLimiting(RateLimitPolicies.Public)
            .WithTags("Channels")
            .WithName("SubmitWebForm")
            .Produces<ApiResponse<WebFormTicketResponse>>(StatusCodes.Status201Created);
}
