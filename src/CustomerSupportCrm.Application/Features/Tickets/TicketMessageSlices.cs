using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Authorization;
using CustomerSupportCrm.Application.Features.Attachments;
using CustomerSupportCrm.Application.Features.Tickets.Common;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Tickets;
using CustomerSupportCrm.Domain.Attachments;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Tickets;
using CustomerSupportCrm.Domain.Users;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Tickets;

/// <summary>
/// Adds a message to a ticket from any author. Delivery on the ticket's channel (email,
/// WhatsApp, SMS, chat) happens in the TicketMessageAdded handlers of the channels feature.
/// </summary>
public sealed class TicketMessageWriter(IApplicationDbContext db, TimeProvider time)
{
    public async Task<TicketMessage> AddAsync(
        Ticket ticket,
        MessageAuthorType authorType,
        UserId? authorUserId,
        Guid? authorCustomerId,
        string body,
        bool isInternal,
        TicketChannel channel,
        string? externalMessageId,
        IReadOnlyCollection<Guid> mentionedUserIds,
        IReadOnlyCollection<Guid> attachmentIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var now = time.GetUtcNow();

        var message = TicketMessage.Create(ticket.Id, authorType, authorUserId, authorCustomerId, body, isInternal, channel, externalMessageId, mentionedUserIds, now);
        db.TicketMessages.Add(message);

        if (attachmentIds.Count > 0)
        {
            var ids = attachmentIds.ToList();
            var files = await db.Attachments
                .Where(a => ids.Contains(a.Id) && a.OwnerType == AttachmentOwnerTypes.Ticket && a.OwnerId == ticket.Id && a.ParentId == null)
                .ToListAsync(cancellationToken);
            foreach (var file in files)
            {
                file.AttachTo(message.Id, isPublic: !isInternal);
            }
        }

        ticket.RecordMessage(message.Id, authorType, isInternal, now);
        return message;
    }
}

public sealed record AddTicketMessageCommand(Guid TicketId, string Body, bool IsInternal, IReadOnlyList<Guid> MentionedUserIds, IReadOnlyList<Guid> AttachmentIds)
    : IRequest<TicketMessageResponse>;

internal sealed class AddTicketMessageValidator : AbstractValidator<AddTicketMessageCommand>
{
    public AddTicketMessageValidator()
    {
        RuleFor(c => c.Body).NotEmpty().MaximumLength(TicketMessage.BodyMaxLength);
        RuleFor(c => c.MentionedUserIds).NotNull().Must(m => m.Count <= 20);
        RuleFor(c => c.AttachmentIds).NotNull().Must(a => a.Count <= 10);
    }
}

internal sealed class AddTicketMessageHandler(
    IApplicationDbContext db,
    IAccessScopeProvider scopes,
    ICurrentUser currentUser,
    TicketMessageWriter writer)
    : IRequestHandler<AddTicketMessageCommand, TicketMessageResponse>
{
    public async Task<TicketMessageResponse> Handle(AddTicketMessageCommand request, CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(cancellationToken);
        var ticket = await TicketQueries.LoadAsync(db, scope, request.TicketId, cancellationToken);

        var mentioned = request.MentionedUserIds.Distinct().Select(id => new UserId(id)).ToList();
        var validMentions = await db.Users.Where(u => mentioned.Contains(u.Id) && u.Status == UserStatus.Active).Select(u => u.Id.Value).ToListAsync(cancellationToken);

        var message = await writer.AddAsync(
            ticket,
            MessageAuthorType.Agent,
            currentUser.UserId,
            null,
            request.Body,
            request.IsInternal,
            ticket.Channel,
            null,
            validMentions,
            request.AttachmentIds,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var messages = await TicketQueries.GetMessagesAsync(db, ticket.Id, publicOnly: false, id => TicketQueries.DownloadPath(ticket.Id, id), cancellationToken);
        return messages.Single(m => m.Id == message.Id);
    }
}

public sealed record UploadTicketAttachmentCommand(Guid TicketId, string FileName, Stream Content, long Length) : IRequest<AttachmentResponse>;

/// <summary>Uploads a file to the ticket; it becomes visible when a message references it.</summary>
internal sealed class UploadTicketAttachmentHandler(IApplicationDbContext db, IAccessScopeProvider scopes, AttachmentService attachments, ICurrentUser currentUser)
    : IRequestHandler<UploadTicketAttachmentCommand, AttachmentResponse>
{
    public async Task<AttachmentResponse> Handle(UploadTicketAttachmentCommand request, CancellationToken cancellationToken)
    {
        await TicketQueries.EnsureAccessibleAsync(db, await scopes.GetAsync(cancellationToken), request.TicketId, cancellationToken);
        var attachment = await attachments.StoreAsync(AttachmentOwnerTypes.Ticket, request.TicketId, null, request.FileName, request.Content, request.Length, false, currentUser.UserId, null, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await attachments.ToResponseAsync(attachment, TicketQueries.DownloadPath(request.TicketId, attachment.Id), cancellationToken);
    }
}

public sealed record ListTicketAttachmentsQuery(Guid TicketId) : IRequest<IReadOnlyList<AttachmentResponse>>;

internal sealed class ListTicketAttachmentsHandler(IApplicationDbContext db, IAccessScopeProvider scopes, AttachmentService attachments)
    : IRequestHandler<ListTicketAttachmentsQuery, IReadOnlyList<AttachmentResponse>>
{
    public async Task<IReadOnlyList<AttachmentResponse>> Handle(ListTicketAttachmentsQuery request, CancellationToken cancellationToken)
    {
        await TicketQueries.EnsureAccessibleAsync(db, await scopes.GetAsync(cancellationToken), request.TicketId, cancellationToken);
        return await attachments.ListAsync(AttachmentOwnerTypes.Ticket, request.TicketId, false, id => TicketQueries.DownloadPath(request.TicketId, id), cancellationToken);
    }
}

public sealed record DownloadTicketAttachmentQuery(Guid TicketId, Guid AttachmentId) : IRequest<StoredFile>;

internal sealed class DownloadTicketAttachmentHandler(IApplicationDbContext db, IAccessScopeProvider scopes, AttachmentService attachments)
    : IRequestHandler<DownloadTicketAttachmentQuery, StoredFile>
{
    public async Task<StoredFile> Handle(DownloadTicketAttachmentQuery request, CancellationToken cancellationToken)
    {
        await TicketQueries.EnsureAccessibleAsync(db, await scopes.GetAsync(cancellationToken), request.TicketId, cancellationToken);
        var attachment = await attachments.GetAsync(request.AttachmentId, AttachmentOwnerTypes.Ticket, request.TicketId, false, cancellationToken);
        return await attachments.OpenAsync(attachment, cancellationToken);
    }
}

public sealed record DeleteTicketAttachmentCommand(Guid TicketId, Guid AttachmentId) : IRequest;

internal sealed class DeleteTicketAttachmentHandler(IApplicationDbContext db, IAccessScopeProvider scopes, AttachmentService attachments)
    : IRequestHandler<DeleteTicketAttachmentCommand>
{
    public async Task Handle(DeleteTicketAttachmentCommand request, CancellationToken cancellationToken)
    {
        await TicketQueries.EnsureAccessibleAsync(db, await scopes.GetAsync(cancellationToken), request.TicketId, cancellationToken);
        var attachment = await attachments.GetAsync(request.AttachmentId, AttachmentOwnerTypes.Ticket, request.TicketId, false, cancellationToken);
        await attachments.DeleteAsync(attachment, cancellationToken);
    }
}

internal sealed class TicketMessageEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tickets/{ticketId:guid}").WithTags("Tickets");

        group.MapPost("/messages", async (Guid ticketId, AddTicketMessageRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new AddTicketMessageCommand(ticketId, request.Body, request.IsInternal, request.MentionedUserIds ?? [], request.AttachmentIds ?? []), ct)))
            .RequireAuthorization(Permissions.TicketsUpdate)
            .WithName("AddTicketMessage")
            .Produces<ApiResponse<TicketMessageResponse>>();

        group.MapGet("/attachments", async (Guid ticketId, ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new ListTicketAttachmentsQuery(ticketId), ct)))
            .RequireAuthorization(Permissions.TicketsView)
            .WithName("ListTicketAttachments")
            .Produces<ApiResponse<IReadOnlyList<AttachmentResponse>>>();

        group.MapPost("/attachments", async (Guid ticketId, IFormFile file, ISender sender, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return ApiResults.Ok(await sender.Send(new UploadTicketAttachmentCommand(ticketId, file.FileName, stream, file.Length), ct));
            })
            .RequireAuthorization(Permissions.TicketsUpdate)
            .DisableAntiforgery()
            .WithName("UploadTicketAttachment")
            .Produces<ApiResponse<AttachmentResponse>>();

        group.MapGet("/attachments/{attachmentId:guid}", async (Guid ticketId, Guid attachmentId, ISender sender, HttpContext http, CancellationToken ct) =>
                AttachmentService.ToDownload(await sender.Send(new DownloadTicketAttachmentQuery(ticketId, attachmentId), ct), http))
            .RequireAuthorization(Permissions.TicketsView)
            .WithName("DownloadTicketAttachment");

        group.MapDelete("/attachments/{attachmentId:guid}", async (Guid ticketId, Guid attachmentId, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new DeleteTicketAttachmentCommand(ticketId, attachmentId), ct);
                return ApiResults.Success();
            })
            .RequireAuthorization(Permissions.TicketsUpdate)
            .WithName("DeleteTicketAttachment")
            .Produces<ApiResponse<object?>>();
    }
}
