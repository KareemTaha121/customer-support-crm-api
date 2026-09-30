using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Pagination;
using CustomerSupportCrm.Application.Features.Attachments;
using CustomerSupportCrm.Application.Features.Tickets;
using CustomerSupportCrm.Application.Features.Tickets.Common;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Portal;
using CustomerSupportCrm.Domain.Attachments;
using CustomerSupportCrm.Domain.Customers;
using CustomerSupportCrm.Domain.Tickets;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.CustomerPortal;

internal static class PortalTickets
{
    /// <summary>The customer's own ticket (tracked) or 404.</summary>
    public static async Task<Ticket> LoadOwnAsync(IApplicationDbContext db, ICurrentCustomer customer, Guid ticketId, CancellationToken cancellationToken)
    {
        var customerId = customer.CustomerId;
        return await db.Tickets.SingleOrDefaultAsync(t => t.Id == ticketId && t.CustomerId == customerId, cancellationToken)
            ?? throw TicketQueries.NotFound();
    }

    public static string DownloadPath(Guid ticketId, Guid attachmentId) => $"/api/v1/portal/tickets/{ticketId}/attachments/{attachmentId}";

    public static async Task<PortalTicketResponse> ToResponseAsync(IApplicationDbContext db, Ticket t, CancellationToken cancellationToken)
    {
        var category = await db.TicketCategories.Where(c => c.Id == t.CategoryId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);
        var agent = await db.Users.Where(u => u.Id == t.AssignedAgentId).Select(u => u.DisplayName).FirstOrDefaultAsync(cancellationToken);
        return new PortalTicketResponse(
            t.Id,
            t.Number,
            t.Subject,
            t.Description,
            t.Status.ToString(),
            category,
            agent,
            t.Status != TicketStatus.Closed,
            t.Status is TicketStatus.Resolved or TicketStatus.Closed,
            t.SatisfactionRating,
            t.SatisfactionComment,
            t.CreatedAt,
            t.UpdatedAt);
    }
}

/// <param name="Status">open (default), closed or all.</param>
public sealed record PortalListTicketsQuery(int Page = 1, int PageSize = 20, string? Status = null) : IRequest<PagedResult<PortalTicketListItemResponse>>;

internal sealed class PortalListTicketsHandler(IApplicationDbContext db, ICurrentCustomer customer) : IRequestHandler<PortalListTicketsQuery, PagedResult<PortalTicketListItemResponse>>
{
    public async Task<PagedResult<PortalTicketListItemResponse>> Handle(PortalListTicketsQuery request, CancellationToken cancellationToken)
    {
        var customerId = customer.CustomerId;
        var query = db.Tickets.AsNoTracking().Where(t => t.CustomerId == customerId);
        query = request.Status switch
        {
            "closed" => query.Where(t => t.Status == TicketStatus.Resolved || t.Status == TicketStatus.Closed),
            "all" => query,
            _ => query.Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed),
        };

        var page = await query.OrderByDescending(t => t.CreatedAt)
            .Select(t => new PortalTicketListItemResponse(t.Id, t.Number, t.Subject, t.Status.ToString(), t.CreatedAt, t.UpdatedAt, t.LastAgentMessageAt))
            .ToPagedResultAsync(Math.Max(request.Page, 1), Math.Clamp(request.PageSize, 1, 50), cancellationToken);
        return page;
    }
}

public sealed record PortalCreateTicketCommand(string Subject, string Message, Guid? CategoryId) : IRequest<PortalTicketResponse>;

internal sealed class PortalCreateTicketValidator : AbstractValidator<PortalCreateTicketCommand>
{
    public PortalCreateTicketValidator()
    {
        RuleFor(c => c.Subject).NotEmpty().MaximumLength(Ticket.SubjectMaxLength);
        RuleFor(c => c.Message).NotEmpty().MaximumLength(10_000);
    }
}

internal sealed class PortalCreateTicketHandler(IApplicationDbContext db, ICurrentCustomer customer, TicketFactory tickets) : IRequestHandler<PortalCreateTicketCommand, PortalTicketResponse>
{
    public async Task<PortalTicketResponse> Handle(PortalCreateTicketCommand request, CancellationToken cancellationToken)
    {
        var categoryId = request.CategoryId is { } id && await db.TicketCategories.AnyAsync(c => c.Id == id && c.IsActive, cancellationToken) ? id : (Guid?)null;
        var email = await db.CustomerAccounts.Where(a => a.Id == customer.AccountId).Select(a => a.Email).SingleAsync(cancellationToken);

        var ticket = await tickets.CreateAsync(
            new NewTicket(customer.CustomerId, request.Subject, request.Message, categoryId, TicketPriority.Medium, TicketChannel.Portal, null, null, [], email),
            scope: null,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await PortalTickets.ToResponseAsync(db, ticket, cancellationToken);
    }
}

public sealed record PortalGetTicketQuery(Guid TicketId) : IRequest<PortalTicketResponse>;

internal sealed class PortalGetTicketHandler(IApplicationDbContext db, ICurrentCustomer customer) : IRequestHandler<PortalGetTicketQuery, PortalTicketResponse>
{
    public async Task<PortalTicketResponse> Handle(PortalGetTicketQuery request, CancellationToken cancellationToken) =>
        await PortalTickets.ToResponseAsync(db, await PortalTickets.LoadOwnAsync(db, customer, request.TicketId, cancellationToken), cancellationToken);
}

public sealed record PortalGetMessagesQuery(Guid TicketId) : IRequest<IReadOnlyList<PortalMessageResponse>>;

/// <summary>Public messages only; internal notes and internal files never leave the staff app.</summary>
internal sealed class PortalGetMessagesHandler(IApplicationDbContext db, ICurrentCustomer customer) : IRequestHandler<PortalGetMessagesQuery, IReadOnlyList<PortalMessageResponse>>
{
    public async Task<IReadOnlyList<PortalMessageResponse>> Handle(PortalGetMessagesQuery request, CancellationToken cancellationToken)
    {
        var ticket = await PortalTickets.LoadOwnAsync(db, customer, request.TicketId, cancellationToken);
        var messages = await TicketQueries.GetMessagesAsync(db, ticket.Id, publicOnly: true, id => PortalTickets.DownloadPath(ticket.Id, id), cancellationToken);
        return [.. messages.Select(m => new PortalMessageResponse(m.Id, m.AuthorType, m.AuthorName, m.Body, m.CreatedAt, m.Attachments))];
    }
}

public sealed record PortalAddMessageCommand(Guid TicketId, string Body, IReadOnlyList<Guid> AttachmentIds) : IRequest;

internal sealed class PortalAddMessageValidator : AbstractValidator<PortalAddMessageCommand>
{
    public PortalAddMessageValidator()
    {
        RuleFor(c => c.Body).NotEmpty().MaximumLength(10_000);
        RuleFor(c => c.AttachmentIds).NotNull().Must(a => a.Count <= 10);
    }
}

internal sealed class PortalAddMessageHandler(IApplicationDbContext db, ICurrentCustomer customer, TicketMessageWriter writer) : IRequestHandler<PortalAddMessageCommand>
{
    public async Task Handle(PortalAddMessageCommand request, CancellationToken cancellationToken)
    {
        var ticket = await PortalTickets.LoadOwnAsync(db, customer, request.TicketId, cancellationToken);
        await writer.AddAsync(ticket, MessageAuthorType.Customer, null, customer.CustomerId, request.Body, false, TicketChannel.Portal, null, [], request.AttachmentIds, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record PortalUploadAttachmentCommand(Guid TicketId, string FileName, Stream Content, long Length) : IRequest<AttachmentResponse>;

internal sealed class PortalUploadAttachmentHandler(IApplicationDbContext db, ICurrentCustomer customer, AttachmentService attachments) : IRequestHandler<PortalUploadAttachmentCommand, AttachmentResponse>
{
    public async Task<AttachmentResponse> Handle(PortalUploadAttachmentCommand request, CancellationToken cancellationToken)
    {
        var ticket = await PortalTickets.LoadOwnAsync(db, customer, request.TicketId, cancellationToken);
        var attachment = await attachments.StoreAsync(AttachmentOwnerTypes.Ticket, ticket.Id, null, request.FileName, request.Content, request.Length, true, null, customer.CustomerId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return AttachmentService.ToResponse(attachment, null, PortalTickets.DownloadPath(ticket.Id, attachment.Id));
    }
}

public sealed record PortalDownloadAttachmentQuery(Guid TicketId, Guid AttachmentId) : IRequest<StoredFile>;

internal sealed class PortalDownloadAttachmentHandler(IApplicationDbContext db, ICurrentCustomer customer, AttachmentService attachments) : IRequestHandler<PortalDownloadAttachmentQuery, StoredFile>
{
    public async Task<StoredFile> Handle(PortalDownloadAttachmentQuery request, CancellationToken cancellationToken)
    {
        var ticket = await PortalTickets.LoadOwnAsync(db, customer, request.TicketId, cancellationToken);
        var attachment = await attachments.GetAsync(request.AttachmentId, AttachmentOwnerTypes.Ticket, ticket.Id, publicOnly: true, cancellationToken);
        return await attachments.OpenAsync(attachment, cancellationToken);
    }
}

public sealed record PortalFeedbackCommand(Guid TicketId, int Rating, string? Comment) : IRequest<PortalTicketResponse>;

internal sealed class PortalFeedbackValidator : AbstractValidator<PortalFeedbackCommand>
{
    public PortalFeedbackValidator()
    {
        RuleFor(c => c.Rating).InclusiveBetween(1, 5);
        RuleFor(c => c.Comment).MaximumLength(2000);
    }
}

internal sealed class PortalFeedbackHandler(IApplicationDbContext db, ICurrentCustomer customer, TimeProvider time) : IRequestHandler<PortalFeedbackCommand, PortalTicketResponse>
{
    public async Task<PortalTicketResponse> Handle(PortalFeedbackCommand request, CancellationToken cancellationToken)
    {
        var ticket = await PortalTickets.LoadOwnAsync(db, customer, request.TicketId, cancellationToken);
        ticket.SubmitFeedback(request.Rating, request.Comment, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return await PortalTickets.ToResponseAsync(db, ticket, cancellationToken);
    }
}

/// <summary>The customer confirms the request is solved: active → Resolved, Resolved → Closed.</summary>
public sealed record PortalCloseTicketCommand(Guid TicketId) : IRequest<PortalTicketResponse>;

internal sealed class PortalCloseTicketHandler(IApplicationDbContext db, ICurrentCustomer customer, TimeProvider time) : IRequestHandler<PortalCloseTicketCommand, PortalTicketResponse>
{
    public async Task<PortalTicketResponse> Handle(PortalCloseTicketCommand request, CancellationToken cancellationToken)
    {
        var ticket = await PortalTickets.LoadOwnAsync(db, customer, request.TicketId, cancellationToken);
        ticket.ChangeStatus(ticket.Status == TicketStatus.Resolved ? TicketStatus.Closed : TicketStatus.Resolved, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return await PortalTickets.ToResponseAsync(db, ticket, cancellationToken);
    }
}

internal sealed class PortalTicketEndpoints : IPortalEndpoint
{
    private static readonly string[] PortalActivityTypes =
    [
        CustomerActivityTypes.TicketCreated, CustomerActivityTypes.TicketStatusChanged, CustomerActivityTypes.TicketMessage,
        CustomerActivityTypes.TicketFeedback, CustomerActivityTypes.PortalSignIn, CustomerActivityTypes.ChatStarted,
    ];

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/categories", async (IApplicationDbContext db, CancellationToken ct) =>
                ApiResults.Ok(await db.TicketCategories.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                    .Select(c => new PortalCategoryResponse(c.Id, c.Name, c.NameAr)).ToListAsync(ct)))
            .WithName("PortalListCategories")
            .Produces<ApiResponse<List<PortalCategoryResponse>>>();

        app.MapGet("/history", async (int? page, IApplicationDbContext db, ICurrentCustomer customer, CancellationToken ct) =>
            {
                var customerId = customer.CustomerId;
                var result = await db.CustomerActivities.AsNoTracking()
                    .Where(a => a.CustomerId == customerId && PortalActivityTypes.Contains(a.Type))
                    .OrderByDescending(a => a.OccurredAt)
                    .Select(a => new PortalHistoryItemResponse(a.Id, a.Type, a.Summary, a.TicketId, a.OccurredAt))
                    .ToPagedResultAsync(Math.Max(page ?? 1, 1), 25, ct);
                return ApiResults.Paged(result.Items, result.Meta);
            })
            .WithName("PortalHistory")
            .Produces<ApiResponse<IReadOnlyList<PortalHistoryItemResponse>>>();

        var tickets = app.MapGroup("/tickets");
        tickets.MapGet("/", async ([AsParameters] PortalListTicketsQuery query, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(query, ct);
                return ApiResults.Paged(result.Items, result.Meta);
            })
            .WithName("PortalListTickets")
            .Produces<ApiResponse<IReadOnlyList<PortalTicketListItemResponse>>>();

        tickets.MapPost("/", async (PortalCreateTicketRequest request, ISender sender, CancellationToken ct) =>
            {
                var ticket = await sender.Send(new PortalCreateTicketCommand(request.Subject, request.Message, request.CategoryId), ct);
                return ApiResults.Created($"/api/v1/portal/tickets/{ticket.Id}", ticket);
            })
            .WithName("PortalCreateTicket")
            .Produces<ApiResponse<PortalTicketResponse>>(StatusCodes.Status201Created);

        tickets.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new PortalGetTicketQuery(id), ct)))
            .WithName("PortalGetTicket")
            .Produces<ApiResponse<PortalTicketResponse>>();

        tickets.MapGet("/{id:guid}/messages", async (Guid id, ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new PortalGetMessagesQuery(id), ct)))
            .WithName("PortalGetMessages")
            .Produces<ApiResponse<IReadOnlyList<PortalMessageResponse>>>();

        tickets.MapPost("/{id:guid}/messages", async (Guid id, PortalMessageRequest request, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new PortalAddMessageCommand(id, request.Body, request.AttachmentIds ?? []), ct);
                return ApiResults.Success();
            })
            .WithName("PortalAddMessage")
            .Produces<ApiResponse<object?>>();

        tickets.MapPost("/{id:guid}/attachments", async (Guid id, IFormFile file, ISender sender, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return ApiResults.Ok(await sender.Send(new PortalUploadAttachmentCommand(id, file.FileName, stream, file.Length), ct));
            })
            .DisableAntiforgery()
            .WithName("PortalUploadAttachment")
            .Produces<ApiResponse<AttachmentResponse>>();

        tickets.MapGet("/{id:guid}/attachments/{attachmentId:guid}", async (Guid id, Guid attachmentId, ISender sender, HttpContext http, CancellationToken ct) =>
                AttachmentService.ToDownload(await sender.Send(new PortalDownloadAttachmentQuery(id, attachmentId), ct), http))
            .WithName("PortalDownloadAttachment");

        tickets.MapPost("/{id:guid}/feedback", async (Guid id, PortalFeedbackRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new PortalFeedbackCommand(id, request.Rating, request.Comment), ct)))
            .WithName("PortalSubmitFeedback")
            .Produces<ApiResponse<PortalTicketResponse>>();

        tickets.MapPost("/{id:guid}/close", async (Guid id, ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new PortalCloseTicketCommand(id), ct)))
            .WithName("PortalCloseTicket")
            .Produces<ApiResponse<PortalTicketResponse>>();
    }
}
