using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Authorization;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Common.Pagination;
using CustomerSupportCrm.Application.Common.Validation;
using CustomerSupportCrm.Application.Features.Attachments;
using CustomerSupportCrm.Application.Features.Customers.Common;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Customers;
using CustomerSupportCrm.Domain.Attachments;
using CustomerSupportCrm.Domain.Customers;
using CustomerSupportCrm.Domain.Roles;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Customers;

// ---------- Contacts ----------

/// <param name="ContactId">Null to add a contact.</param>
public sealed record SaveCustomerContactCommand(Guid CustomerId, Guid? ContactId, string Type, string Value, string? Label, bool IsPrimary)
    : IRequest<CustomerResponse>;

internal sealed class SaveCustomerContactValidator : AbstractValidator<SaveCustomerContactCommand>
{
    public SaveCustomerContactValidator()
    {
        RuleFor(c => c.Type).NotEmpty().IsEnumName(typeof(ContactType), caseSensitive: false);
        RuleFor(c => c.Value).NotEmpty().MaximumLength(CustomerContact.ValueMaxLength);
        RuleFor(c => c.Label).MaximumLength(CustomerContact.LabelMaxLength);
    }
}

internal sealed class SaveCustomerContactHandler(IApplicationDbContext db, IAccessScopeProvider scopes, IAuditTrail audit)
    : IRequestHandler<SaveCustomerContactCommand, CustomerResponse>
{
    public async Task<CustomerResponse> Handle(SaveCustomerContactCommand request, CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(cancellationToken);
        var customer = await CustomerQueries.LoadAsync(db, scope, request.CustomerId, cancellationToken);
        var type = Enum.Parse<ContactType>(request.Type, ignoreCase: true);

        Guid contactId;
        if (request.ContactId is { } existing)
        {
            customer.UpdateContact(existing, request.Value, request.Label);
            if (request.IsPrimary)
            {
                customer.SetPrimary(existing);
            }

            contactId = existing;
        }
        else
        {
            contactId = customer.AddContact(type, request.Value, request.Label, request.IsPrimary).Id;
        }

        var contact = customer.Contacts.Single(c => c.Id == contactId);
        var (emails, phones) = CustomerQueries.MatchableValues([(contact.Type, contact.Value)]);
        var duplicates = await CustomerQueries.FindDuplicatesAsync(db, emails, phones, customer.Id, cancellationToken);
        if (duplicates.Count > 0)
        {
            throw new ConflictException(CustomerErrors.DuplicateCustomer, $"This contact belongs to another customer ({duplicates[0].Number}).");
        }

        audit.Record("customers.contact_saved", "Customer", customer.Id.ToString(), newValues: new { contact.Id, contact.Type, contact.Value, contact.IsPrimary });
        await db.SaveChangesAsync(cancellationToken);
        return await CustomerQueries.GetResponseAsync(db, scope, customer.Id, cancellationToken);
    }
}

public sealed record RemoveCustomerContactCommand(Guid CustomerId, Guid ContactId) : IRequest<CustomerResponse>;

internal sealed class RemoveCustomerContactHandler(IApplicationDbContext db, IAccessScopeProvider scopes, IAuditTrail audit)
    : IRequestHandler<RemoveCustomerContactCommand, CustomerResponse>
{
    public async Task<CustomerResponse> Handle(RemoveCustomerContactCommand request, CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(cancellationToken);
        var customer = await CustomerQueries.LoadAsync(db, scope, request.CustomerId, cancellationToken);
        customer.RemoveContact(request.ContactId);

        audit.Record("customers.contact_removed", "Customer", customer.Id.ToString(), oldValues: new { contactId = request.ContactId });
        await db.SaveChangesAsync(cancellationToken);
        return await CustomerQueries.GetResponseAsync(db, scope, customer.Id, cancellationToken);
    }
}

public sealed record SetPrimaryContactCommand(Guid CustomerId, Guid ContactId) : IRequest<CustomerResponse>;

internal sealed class SetPrimaryContactHandler(IApplicationDbContext db, IAccessScopeProvider scopes)
    : IRequestHandler<SetPrimaryContactCommand, CustomerResponse>
{
    public async Task<CustomerResponse> Handle(SetPrimaryContactCommand request, CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(cancellationToken);
        var customer = await CustomerQueries.LoadAsync(db, scope, request.CustomerId, cancellationToken);
        customer.SetPrimary(request.ContactId);
        await db.SaveChangesAsync(cancellationToken);
        return await CustomerQueries.GetResponseAsync(db, scope, customer.Id, cancellationToken);
    }
}

// ---------- Notes (internal only) ----------

public sealed record ListCustomerNotesQuery(Guid CustomerId, int Page = 1, int PageSize = PaginationExtensions.DefaultPageSize)
    : IRequest<PagedResult<CustomerNoteResponse>>;

internal sealed class ListCustomerNotesHandler(IApplicationDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser)
    : IRequestHandler<ListCustomerNotesQuery, PagedResult<CustomerNoteResponse>>
{
    public async Task<PagedResult<CustomerNoteResponse>> Handle(ListCustomerNotesQuery request, CancellationToken cancellationToken)
    {
        await CustomerQueries.EnsureAccessibleAsync(db, await scopes.GetAsync(cancellationToken), request.CustomerId, cancellationToken);
        var me = currentUser.UserId;
        var canEditAll = currentUser.HasPermission(Permissions.DataAllBranches);

        var page = await db.CustomerNotes.AsNoTracking()
            .Where(n => n.CustomerId == request.CustomerId)
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.CreatedAt)
            .Select(n => new
            {
                n.Id,
                n.Body,
                n.IsPinned,
                n.AuthorId,
                AuthorName = db.Users.Where(u => u.Id == n.AuthorId).Select(u => u.DisplayName).FirstOrDefault(),
                n.CreatedAt,
                n.UpdatedAt,
            })
            .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);

        return page.Map(n => new CustomerNoteResponse(n.Id, n.Body, n.IsPinned, n.AuthorId.Value, n.AuthorName ?? string.Empty, n.CreatedAt, n.UpdatedAt, canEditAll || n.AuthorId == me));
    }
}

/// <param name="NoteId">Null to add a note.</param>
public sealed record SaveCustomerNoteCommand(Guid CustomerId, Guid? NoteId, string Body, bool IsPinned) : IRequest<CustomerNoteResponse>;

internal sealed class SaveCustomerNoteValidator : AbstractValidator<SaveCustomerNoteCommand>
{
    public SaveCustomerNoteValidator() => RuleFor(c => c.Body).NotEmpty().MaximumLength(CustomerNote.BodyMaxLength);
}

internal sealed class SaveCustomerNoteHandler(
    IApplicationDbContext db,
    IAccessScopeProvider scopes,
    ICurrentUser currentUser,
    CustomerTimeline timeline,
    TimeProvider time)
    : IRequestHandler<SaveCustomerNoteCommand, CustomerNoteResponse>
{
    public async Task<CustomerNoteResponse> Handle(SaveCustomerNoteCommand request, CancellationToken cancellationToken)
    {
        await CustomerQueries.EnsureAccessibleAsync(db, await scopes.GetAsync(cancellationToken), request.CustomerId, cancellationToken);
        var me = currentUser.UserId;
        var now = time.GetUtcNow();

        CustomerNote note;
        if (request.NoteId is { } noteId)
        {
            note = await db.CustomerNotes.SingleOrDefaultAsync(n => n.Id == noteId && n.CustomerId == request.CustomerId, cancellationToken)
                ?? throw new NotFoundException(CustomerErrors.NoteNotFound, "The note was not found.");
            EnsureCanEdit(note, me);
            note.Edit(request.Body, request.IsPinned, now);
        }
        else
        {
            note = CustomerNote.Create(request.CustomerId, me, request.Body, request.IsPinned, now);
            db.CustomerNotes.Add(note);
            timeline.Record(request.CustomerId, CustomerActivityTypes.NoteAdded, note.Body.Length > 120 ? note.Body[..120] + "…" : note.Body);
        }

        await db.SaveChangesAsync(cancellationToken);
        var author = await db.Users.Where(u => u.Id == note.AuthorId).Select(u => u.DisplayName).FirstOrDefaultAsync(cancellationToken);
        return new CustomerNoteResponse(note.Id, note.Body, note.IsPinned, note.AuthorId.Value, author ?? string.Empty, note.CreatedAt, note.UpdatedAt, true);
    }

    private void EnsureCanEdit(CustomerNote note, Domain.Users.UserId me)
    {
        if (note.AuthorId != me && !currentUser.HasPermission(Permissions.DataAllBranches))
        {
            throw new ForbiddenException(CustomerErrors.NoteEditForbidden, "Only the author can change this note.");
        }
    }
}

public sealed record DeleteCustomerNoteCommand(Guid CustomerId, Guid NoteId) : IRequest;

internal sealed class DeleteCustomerNoteHandler(IApplicationDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser)
    : IRequestHandler<DeleteCustomerNoteCommand>
{
    public async Task Handle(DeleteCustomerNoteCommand request, CancellationToken cancellationToken)
    {
        await CustomerQueries.EnsureAccessibleAsync(db, await scopes.GetAsync(cancellationToken), request.CustomerId, cancellationToken);
        var note = await db.CustomerNotes.SingleOrDefaultAsync(n => n.Id == request.NoteId && n.CustomerId == request.CustomerId, cancellationToken)
            ?? throw new NotFoundException(CustomerErrors.NoteNotFound, "The note was not found.");

        if (note.AuthorId != currentUser.UserId && !currentUser.HasPermission(Permissions.DataAllBranches))
        {
            throw new ForbiddenException(CustomerErrors.NoteEditForbidden, "Only the author can delete this note.");
        }

        db.CustomerNotes.Remove(note);
        await db.SaveChangesAsync(cancellationToken);
    }
}

// ---------- Attachments ----------

public sealed record ListCustomerAttachmentsQuery(Guid CustomerId) : IRequest<IReadOnlyList<AttachmentResponse>>;

internal sealed class ListCustomerAttachmentsHandler(IApplicationDbContext db, IAccessScopeProvider scopes, AttachmentService attachments)
    : IRequestHandler<ListCustomerAttachmentsQuery, IReadOnlyList<AttachmentResponse>>
{
    public async Task<IReadOnlyList<AttachmentResponse>> Handle(ListCustomerAttachmentsQuery request, CancellationToken cancellationToken)
    {
        await CustomerQueries.EnsureAccessibleAsync(db, await scopes.GetAsync(cancellationToken), request.CustomerId, cancellationToken);
        return await attachments.ListAsync(AttachmentOwnerTypes.Customer, request.CustomerId, publicOnly: false, id => CustomerAttachmentPaths.Download(request.CustomerId, id), cancellationToken);
    }
}

public sealed record UploadCustomerAttachmentCommand(Guid CustomerId, string FileName, Stream Content, long Length) : IRequest<AttachmentResponse>;

internal sealed class UploadCustomerAttachmentHandler(
    IApplicationDbContext db,
    IAccessScopeProvider scopes,
    AttachmentService attachments,
    ICurrentUser currentUser,
    CustomerTimeline timeline)
    : IRequestHandler<UploadCustomerAttachmentCommand, AttachmentResponse>
{
    public async Task<AttachmentResponse> Handle(UploadCustomerAttachmentCommand request, CancellationToken cancellationToken)
    {
        await CustomerQueries.EnsureAccessibleAsync(db, await scopes.GetAsync(cancellationToken), request.CustomerId, cancellationToken);

        var attachment = await attachments.StoreAsync(
            AttachmentOwnerTypes.Customer,
            request.CustomerId,
            null,
            request.FileName,
            request.Content,
            request.Length,
            isPublic: false,
            currentUser.UserId,
            null,
            cancellationToken);
        timeline.Record(request.CustomerId, CustomerActivityTypes.AttachmentAdded, attachment.FileName, data: new { attachment.Id, attachment.FileName });
        await db.SaveChangesAsync(cancellationToken);

        return await attachments.ToResponseAsync(attachment, CustomerAttachmentPaths.Download(request.CustomerId, attachment.Id), cancellationToken);
    }
}

public sealed record DownloadCustomerAttachmentQuery(Guid CustomerId, Guid AttachmentId) : IRequest<StoredFile>;

internal sealed class DownloadCustomerAttachmentHandler(IApplicationDbContext db, IAccessScopeProvider scopes, AttachmentService attachments)
    : IRequestHandler<DownloadCustomerAttachmentQuery, StoredFile>
{
    public async Task<StoredFile> Handle(DownloadCustomerAttachmentQuery request, CancellationToken cancellationToken)
    {
        await CustomerQueries.EnsureAccessibleAsync(db, await scopes.GetAsync(cancellationToken), request.CustomerId, cancellationToken);
        var attachment = await attachments.GetAsync(request.AttachmentId, AttachmentOwnerTypes.Customer, request.CustomerId, publicOnly: false, cancellationToken);
        return await attachments.OpenAsync(attachment, cancellationToken);
    }
}

public sealed record DeleteCustomerAttachmentCommand(Guid CustomerId, Guid AttachmentId) : IRequest;

internal sealed class DeleteCustomerAttachmentHandler(IApplicationDbContext db, IAccessScopeProvider scopes, AttachmentService attachments, IAuditTrail audit)
    : IRequestHandler<DeleteCustomerAttachmentCommand>
{
    public async Task Handle(DeleteCustomerAttachmentCommand request, CancellationToken cancellationToken)
    {
        await CustomerQueries.EnsureAccessibleAsync(db, await scopes.GetAsync(cancellationToken), request.CustomerId, cancellationToken);
        var attachment = await attachments.GetAsync(request.AttachmentId, AttachmentOwnerTypes.Customer, request.CustomerId, publicOnly: false, cancellationToken);
        audit.Record("customers.attachment_deleted", "Customer", request.CustomerId.ToString(), oldValues: new { attachment.Id, attachment.FileName });
        await attachments.DeleteAsync(attachment, cancellationToken);
    }
}

internal static class CustomerAttachmentPaths
{
    public static string Download(Guid customerId, Guid attachmentId) => $"/api/v1/customers/{customerId}/attachments/{attachmentId}";
}

// ---------- Interaction history ----------

/// <param name="Types">Comma-separated activity types to include (e.g. ticket.created,note.added).</param>
public sealed record GetCustomerHistoryQuery(Guid CustomerId, int Page = 1, int PageSize = PaginationExtensions.DefaultPageSize, string? Types = null)
    : IRequest<PagedResult<CustomerActivityResponse>>;

internal sealed class GetCustomerHistoryValidator : AbstractValidator<GetCustomerHistoryQuery>
{
    public GetCustomerHistoryValidator()
    {
        RuleFor(q => q.Page).ValidPage();
        RuleFor(q => q.PageSize).ValidPageSize();
        RuleFor(q => q.Types).MaximumLength(500);
    }
}

/// <summary>Paged, read-optimized timeline from the denormalized activity table.</summary>
internal sealed class GetCustomerHistoryHandler(IApplicationDbContext db, IAccessScopeProvider scopes)
    : IRequestHandler<GetCustomerHistoryQuery, PagedResult<CustomerActivityResponse>>
{
    public async Task<PagedResult<CustomerActivityResponse>> Handle(GetCustomerHistoryQuery request, CancellationToken cancellationToken)
    {
        await CustomerQueries.EnsureAccessibleAsync(db, await scopes.GetAsync(cancellationToken), request.CustomerId, cancellationToken);

        var query = db.CustomerActivities.AsNoTracking().Where(a => a.CustomerId == request.CustomerId);
        if (!string.IsNullOrWhiteSpace(request.Types))
        {
            var types = request.Types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            query = query.Where(a => types.Contains(a.Type));
        }

        var page = await query
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Select(a => new
            {
                a.Id,
                a.Type,
                a.Summary,
                a.ActorUserId,
                ActorName = db.Users.Where(u => u.Id == a.ActorUserId).Select(u => u.DisplayName).FirstOrDefault(),
                a.TicketId,
                a.Data,
                a.OccurredAt,
            })
            .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);

        return page.Map(a => new CustomerActivityResponse(a.Id, a.Type, a.Summary, a.ActorUserId?.Value, a.ActorName, a.TicketId, CustomerQueries.ParseJson(a.Data), a.OccurredAt));
    }
}

// ---------- Endpoints ----------

internal sealed class CustomerDetailEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/customers/{customerId:guid}").WithTags("Customers");

        group.MapPost("/contacts", async (Guid customerId, CustomerContactRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SaveCustomerContactCommand(customerId, null, request.Type, request.Value, request.Label, request.IsPrimary), ct)))
            .RequireAuthorization(Permissions.CustomersUpdate)
            .WithName("AddCustomerContact")
            .Produces<ApiResponse<CustomerResponse>>();

        group.MapPut("/contacts/{contactId:guid}", async (Guid customerId, Guid contactId, CustomerContactRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SaveCustomerContactCommand(customerId, contactId, request.Type, request.Value, request.Label, request.IsPrimary), ct)))
            .RequireAuthorization(Permissions.CustomersUpdate)
            .WithName("UpdateCustomerContact")
            .Produces<ApiResponse<CustomerResponse>>();

        group.MapDelete("/contacts/{contactId:guid}", async (Guid customerId, Guid contactId, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new RemoveCustomerContactCommand(customerId, contactId), ct)))
            .RequireAuthorization(Permissions.CustomersUpdate)
            .WithName("RemoveCustomerContact")
            .Produces<ApiResponse<CustomerResponse>>();

        group.MapPost("/contacts/{contactId:guid}/primary", async (Guid customerId, Guid contactId, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SetPrimaryContactCommand(customerId, contactId), ct)))
            .RequireAuthorization(Permissions.CustomersUpdate)
            .WithName("SetPrimaryCustomerContact")
            .Produces<ApiResponse<CustomerResponse>>();

        group.MapGet("/notes", async (Guid customerId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new ListCustomerNotesQuery(customerId, page ?? 1, pageSize ?? PaginationExtensions.DefaultPageSize), ct);
                return ApiResults.Paged(result.Items, result.Meta);
            })
            .RequireAuthorization(Permissions.CustomersView)
            .WithName("ListCustomerNotes")
            .Produces<ApiResponse<IReadOnlyList<CustomerNoteResponse>>>();

        group.MapPost("/notes", async (Guid customerId, CustomerNoteRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SaveCustomerNoteCommand(customerId, null, request.Body, request.IsPinned), ct)))
            .RequireAuthorization(Permissions.CustomerNotesManage)
            .WithName("AddCustomerNote")
            .Produces<ApiResponse<CustomerNoteResponse>>();

        group.MapPut("/notes/{noteId:guid}", async (Guid customerId, Guid noteId, CustomerNoteRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new SaveCustomerNoteCommand(customerId, noteId, request.Body, request.IsPinned), ct)))
            .RequireAuthorization(Permissions.CustomerNotesManage)
            .WithName("UpdateCustomerNote")
            .Produces<ApiResponse<CustomerNoteResponse>>();

        group.MapDelete("/notes/{noteId:guid}", async (Guid customerId, Guid noteId, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new DeleteCustomerNoteCommand(customerId, noteId), ct);
                return ApiResults.Success();
            })
            .RequireAuthorization(Permissions.CustomerNotesManage)
            .WithName("DeleteCustomerNote")
            .Produces<ApiResponse<object?>>();

        group.MapGet("/attachments", async (Guid customerId, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new ListCustomerAttachmentsQuery(customerId), ct)))
            .RequireAuthorization(Permissions.CustomersView)
            .WithName("ListCustomerAttachments")
            .Produces<ApiResponse<IReadOnlyList<AttachmentResponse>>>();

        group.MapPost("/attachments", async (Guid customerId, IFormFile file, ISender sender, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return ApiResults.Ok(await sender.Send(new UploadCustomerAttachmentCommand(customerId, file.FileName, stream, file.Length), ct));
            })
            .RequireAuthorization(Permissions.CustomerAttachmentsManage)
            .DisableAntiforgery()
            .WithName("UploadCustomerAttachment")
            .Produces<ApiResponse<AttachmentResponse>>();

        group.MapGet("/attachments/{attachmentId:guid}", async (Guid customerId, Guid attachmentId, ISender sender, HttpContext http, CancellationToken ct) =>
                AttachmentService.ToDownload(await sender.Send(new DownloadCustomerAttachmentQuery(customerId, attachmentId), ct), http))
            .RequireAuthorization(Permissions.CustomersView)
            .WithName("DownloadCustomerAttachment");

        group.MapDelete("/attachments/{attachmentId:guid}", async (Guid customerId, Guid attachmentId, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new DeleteCustomerAttachmentCommand(customerId, attachmentId), ct);
                return ApiResults.Success();
            })
            .RequireAuthorization(Permissions.CustomerAttachmentsManage)
            .WithName("DeleteCustomerAttachment")
            .Produces<ApiResponse<object?>>();

        group.MapGet("/history", async (Guid customerId, int? page, int? pageSize, string? types, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new GetCustomerHistoryQuery(customerId, page ?? 1, pageSize ?? PaginationExtensions.DefaultPageSize, types), ct);
                return ApiResults.Paged(result.Items, result.Meta);
            })
            .RequireAuthorization(Permissions.CustomersView)
            .WithName("GetCustomerHistory")
            .Produces<ApiResponse<IReadOnlyList<CustomerActivityResponse>>>();
    }
}
