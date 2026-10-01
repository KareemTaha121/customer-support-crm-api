using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Messaging;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Authorization;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.CustomerPortal;
using CustomerSupportCrm.Application.Common.Pagination;
using CustomerSupportCrm.Application.Common.Validation;
using CustomerSupportCrm.Application.Features.Customers.Common;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Customers;
using CustomerSupportCrm.Domain.Customers;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Tickets;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Customers;

// ---------- Create ----------

public sealed record CreateCustomerCommand(
    string Type,
    string Name,
    string? CompanyName,
    string PreferredLanguage,
    Guid BranchId,
    Guid? DepartmentId,
    IReadOnlyList<string> Tags,
    IReadOnlyList<CustomerContactRequest> Contacts,
    bool IgnoreDuplicates) : IRequest<CustomerResponse>;

internal sealed class CreateCustomerValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerValidator()
    {
        RuleFor(c => c.Type).NotEmpty().IsEnumName(typeof(CustomerType), caseSensitive: false);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Customer.NameMaxLength);
        RuleFor(c => c.CompanyName).MaximumLength(Customer.NameMaxLength);
        RuleFor(c => c.PreferredLanguage).NotEmpty().Must(l => l is "en" or "ar").WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(c => c.BranchId).NotEmpty();
        RuleFor(c => c.Tags).NotNull().Must(t => t.Count <= Customer.MaxTags);
        RuleFor(c => c.Contacts).NotNull().Must(c => c.Count <= 50);
        RuleForEach(c => c.Contacts).ChildRules(contact =>
        {
            contact.RuleFor(x => x.Type).NotEmpty().IsEnumName(typeof(ContactType), caseSensitive: false);
            contact.RuleFor(x => x.Value).NotEmpty().MaximumLength(CustomerContact.ValueMaxLength);
        });
    }
}

internal sealed class CreateCustomerHandler(
    IApplicationDbContext db,
    IAccessScopeProvider scopes,
    ISequenceGenerator sequences,
    IAuditTrail audit)
    : IRequestHandler<CreateCustomerCommand, CustomerResponse>
{
    public async Task<CustomerResponse> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(cancellationToken);
        scope.EnsureCanAssign(request.BranchId, request.DepartmentId);
        await OrganizationUnits.EnsureValidAsync(db, request.BranchId, request.DepartmentId, cancellationToken);

        var number = Customer.FormatNumber(await sequences.NextValueAsync(Sequences.CustomerNumbers, cancellationToken));
        var customer = Customer.Create(
            number,
            Enum.Parse<CustomerType>(request.Type, ignoreCase: true),
            request.Name,
            request.CompanyName,
            request.PreferredLanguage,
            request.Tags,
            request.BranchId,
            request.DepartmentId);

        foreach (var contact in request.Contacts)
        {
            customer.AddContact(Enum.Parse<ContactType>(contact.Type, ignoreCase: true), contact.Value, contact.Label, contact.IsPrimary);
        }

        if (!request.IgnoreDuplicates)
        {
            var (emails, phones) = CustomerQueries.MatchableValues(customer.Contacts.Select(c => (c.Type, c.Value)));
            var duplicates = await CustomerQueries.FindDuplicatesAsync(db, emails, phones, null, cancellationToken);
            if (duplicates.Count > 0)
            {
                throw new ConflictException(
                    CustomerErrors.DuplicateCustomer,
                    $"A customer with the same {duplicates[0].MatchedOn.ToUpperInvariant()} already exists ({duplicates[0].Number}).");
            }
        }

        db.Customers.Add(customer);
        audit.Record("customers.created", "Customer", customer.Id.ToString(), newValues: new { customer.Number, customer.Name, customer.BranchId, customer.DepartmentId });
        await db.SaveChangesAsync(cancellationToken);

        return await CustomerQueries.GetResponseAsync(db, AccessScope.Everything, customer.Id, cancellationToken);
    }
}

// ---------- Update / delete ----------

public sealed record UpdateCustomerCommand(
    Guid CustomerId,
    string Type,
    string Name,
    string? CompanyName,
    string PreferredLanguage,
    string Status,
    Guid BranchId,
    Guid? DepartmentId,
    IReadOnlyList<string> Tags) : IRequest<CustomerResponse>;

internal sealed class UpdateCustomerValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerValidator()
    {
        RuleFor(c => c.Type).NotEmpty().IsEnumName(typeof(CustomerType), caseSensitive: false);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Customer.NameMaxLength);
        RuleFor(c => c.CompanyName).MaximumLength(Customer.NameMaxLength);
        RuleFor(c => c.PreferredLanguage).NotEmpty().Must(l => l is "en" or "ar").WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(c => c.Status).NotEmpty().IsEnumName(typeof(CustomerStatus), caseSensitive: false);
        RuleFor(c => c.BranchId).NotEmpty();
        RuleFor(c => c.Tags).NotNull().Must(t => t.Count <= Customer.MaxTags);
    }
}

internal sealed class UpdateCustomerHandler(IApplicationDbContext db, IAccessScopeProvider scopes, IAuditTrail audit, CustomerTimeline timeline)
    : IRequestHandler<UpdateCustomerCommand, CustomerResponse>
{
    public async Task<CustomerResponse> Handle(UpdateCustomerCommand request, CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(cancellationToken);
        var customer = await CustomerQueries.LoadAsync(db, scope, request.CustomerId, cancellationToken);

        if (customer.BranchId != request.BranchId || customer.DepartmentId != request.DepartmentId)
        {
            scope.EnsureCanAssign(request.BranchId, request.DepartmentId);
            await OrganizationUnits.EnsureValidAsync(db, request.BranchId, request.DepartmentId, cancellationToken);
            customer.MoveTo(request.BranchId, request.DepartmentId);
        }

        var before = new { customer.Name, customer.Type, customer.CompanyName, customer.PreferredLanguage, customer.Status, customer.Tags };
        customer.UpdateProfile(Enum.Parse<CustomerType>(request.Type, true), request.Name, request.CompanyName, request.PreferredLanguage, request.Tags);
        customer.SetStatus(Enum.Parse<CustomerStatus>(request.Status, true));
        await PortalAccountNames.FollowCustomerRenameAsync(db, customer.Id, before.Name, customer.Name, cancellationToken);

        audit.Record("customers.updated", "Customer", customer.Id.ToString(), before, new { customer.Name, customer.Type, customer.CompanyName, customer.PreferredLanguage, customer.Status, customer.Tags, customer.BranchId, customer.DepartmentId });
        timeline.Record(customer.Id, CustomerActivityTypes.CustomerUpdated, "Profile updated");
        await db.SaveChangesAsync(cancellationToken);

        return await CustomerQueries.GetResponseAsync(db, scope, customer.Id, cancellationToken);
    }
}

public sealed record DeleteCustomerCommand(Guid CustomerId) : IRequest;

/// <summary>Soft delete. Refused while the customer has active tickets.</summary>
internal sealed class DeleteCustomerHandler(IApplicationDbContext db, IAccessScopeProvider scopes, IAuditTrail audit) : IRequestHandler<DeleteCustomerCommand>
{
    public async Task Handle(DeleteCustomerCommand request, CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(cancellationToken);
        var customer = await CustomerQueries.LoadAsync(db, scope, request.CustomerId, cancellationToken);

        if (await db.Tickets.AnyAsync(t => t.CustomerId == customer.Id && t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed, cancellationToken))
        {
            throw new ConflictException(CustomerErrors.HasOpenTickets, "Resolve or close the customer's active tickets first.");
        }

        db.Customers.Remove(customer);
        audit.Record("customers.deleted", "Customer", customer.Id.ToString(), oldValues: new { customer.Number, customer.Name });
        await db.SaveChangesAsync(cancellationToken);
    }
}

// ---------- Queries ----------

public sealed record GetCustomerQuery(Guid CustomerId) : IRequest<CustomerResponse>;

internal sealed class GetCustomerHandler(IApplicationDbContext db, IAccessScopeProvider scopes) : IRequestHandler<GetCustomerQuery, CustomerResponse>
{
    public async Task<CustomerResponse> Handle(GetCustomerQuery request, CancellationToken cancellationToken) =>
        await CustomerQueries.GetResponseAsync(db, await scopes.GetAsync(cancellationToken), request.CustomerId, cancellationToken);
}

/// <param name="Search">Name, company, number, email or phone (any contact).</param>
/// <param name="SortBy">name (default), number, createdAt, updatedAt.</param>
public sealed record ListCustomersQuery(
    int Page = 1,
    int PageSize = PaginationExtensions.DefaultPageSize,
    string? Search = null,
    string? Status = null,
    string? Type = null,
    Guid? BranchId = null,
    Guid? DepartmentId = null,
    string? Tag = null,
    string? SortBy = null,
    string? SortDirection = null) : IRequest<PagedResult<CustomerListItemResponse>>;

internal sealed class ListCustomersValidator : AbstractValidator<ListCustomersQuery>
{
    public ListCustomersValidator()
    {
        RuleFor(q => q.Page).ValidPage();
        RuleFor(q => q.PageSize).ValidPageSize();
        RuleFor(q => q.Search).MaximumLength(200);
        RuleFor(q => q.Status).OneOf(nameof(CustomerStatus.Active), nameof(CustomerStatus.Inactive));
        RuleFor(q => q.Type).OneOf(nameof(CustomerType.Individual), nameof(CustomerType.Company));
        RuleFor(q => q.SortBy).OneOf("name", "number", "createdAt", "updatedAt");
        RuleFor(q => q.SortDirection).ValidSortDirection();
    }
}

internal sealed class ListCustomersHandler(IApplicationDbContext db, IAccessScopeProvider scopes)
    : IRequestHandler<ListCustomersQuery, PagedResult<CustomerListItemResponse>>
{
    public async Task<PagedResult<CustomerListItemResponse>> Handle(ListCustomersQuery request, CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(cancellationToken);
        var query = db.Customers.AsNoTracking().WhereInScope(scope);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            var upper = term.ToUpperInvariant();
            var phone = Domain.Shared.PhoneNumber.Normalize(term);
#pragma warning disable CA1304, CA1311, CA1862 // Translated to SQL upper().
            query = query.Where(c =>
                c.Name.ToUpper().Contains(upper)
                || (c.CompanyName != null && c.CompanyName.ToUpper().Contains(upper))
                || c.Number.ToUpper() == upper
                || c.Contacts.Any(x => x.Value.ToUpper().Contains(upper) || (phone.Length >= 6 && x.Value.Contains(phone))));
#pragma warning restore CA1304, CA1311, CA1862
        }

        if (request.Status is not null)
        {
            var status = Enum.Parse<CustomerStatus>(request.Status);
            query = query.Where(c => c.Status == status);
        }

        if (request.Type is not null)
        {
            var type = Enum.Parse<CustomerType>(request.Type);
            query = query.Where(c => c.Type == type);
        }

        if (request.BranchId is { } branchId)
        {
            query = query.Where(c => c.BranchId == branchId);
        }

        if (request.DepartmentId is { } departmentId)
        {
            query = query.Where(c => c.DepartmentId == departmentId);
        }

        if (!string.IsNullOrWhiteSpace(request.Tag))
        {
            var tag = request.Tag.Trim();
            query = query.Where(c => c.Tags.Contains(tag));
        }

        var descending = request.SortDirection == "desc";
        var ordered = request.SortBy switch
        {
            "number" => descending ? query.OrderByDescending(c => c.Number) : query.OrderBy(c => c.Number),
            "createdAt" => descending ? query.OrderByDescending(c => c.CreatedAt) : query.OrderBy(c => c.CreatedAt),
            "updatedAt" => descending ? query.OrderByDescending(c => c.UpdatedAt) : query.OrderBy(c => c.UpdatedAt),
            _ => descending ? query.OrderByDescending(c => c.Name) : query.OrderBy(c => c.Name),
        };

        return await ordered.ThenBy(c => c.Id).ProjectListItems(db).ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);
    }
}

public sealed record FindDuplicateCustomersQuery(string? Email, string? Phone, Guid? ExcludeCustomerId) : IRequest<IReadOnlyList<DuplicateCandidateResponse>>;

internal sealed class FindDuplicateCustomersHandler(IApplicationDbContext db)
    : IRequestHandler<FindDuplicateCustomersQuery, IReadOnlyList<DuplicateCandidateResponse>>
{
    public Task<IReadOnlyList<DuplicateCandidateResponse>> Handle(FindDuplicateCustomersQuery request, CancellationToken cancellationToken)
    {
        List<string> emails = string.IsNullOrWhiteSpace(request.Email) ? [] : [Domain.Shared.EmailAddress.Normalize(request.Email)];
        List<string> phones = Domain.Shared.PhoneNumber.TryCreate(request.Phone, out var phone) ? [phone!.Value] : [];
        return CustomerQueries.FindDuplicatesAsync(db, emails, phones, request.ExcludeCustomerId, cancellationToken);
    }
}

// ---------- Timeline ----------

internal sealed class CustomerCreatedTimelineHandler(CustomerTimeline timeline) : INotificationHandler<DomainEventNotification<CustomerCreatedDomainEvent>>
{
    public Task Handle(DomainEventNotification<CustomerCreatedDomainEvent> notification, CancellationToken cancellationToken)
    {
        timeline.Record(notification.DomainEvent.CustomerId, CustomerActivityTypes.CustomerCreated, "Customer created");
        return Task.CompletedTask;
    }
}

// ---------- Endpoints ----------

internal sealed class CustomerProfileEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/customers").WithTags("Customers");

        group.MapGet("/", async ([AsParameters] ListCustomersQuery query, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(query, ct);
                return ApiResults.Paged(result.Items, result.Meta);
            })
            .RequireAuthorization(Permissions.CustomersView)
            .WithName("ListCustomers")
            .Produces<ApiResponse<IReadOnlyList<CustomerListItemResponse>>>();

        group.MapGet("/duplicates", async (string? email, string? phone, Guid? excludeCustomerId, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new FindDuplicateCustomersQuery(email, phone, excludeCustomerId), ct)))
            .RequireAuthorization(Permissions.CustomersView)
            .WithName("FindDuplicateCustomers")
            .Produces<ApiResponse<IReadOnlyList<DuplicateCandidateResponse>>>();

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new GetCustomerQuery(id), ct)))
            .RequireAuthorization(Permissions.CustomersView)
            .WithName("GetCustomer")
            .Produces<ApiResponse<CustomerResponse>>();

        group.MapPost("/", async (CreateCustomerRequest request, ISender sender, CancellationToken ct) =>
            {
                var customer = await sender.Send(
                    new CreateCustomerCommand(
                        request.Type,
                        request.Name,
                        request.CompanyName,
                        request.PreferredLanguage,
                        request.BranchId,
                        request.DepartmentId,
                        request.Tags ?? [],
                        request.Contacts ?? [],
                        request.IgnoreDuplicates),
                    ct);
                return ApiResults.Created($"/api/v1/customers/{customer.Id}", customer);
            })
            .RequireAuthorization(Permissions.CustomersCreate)
            .WithName("CreateCustomer")
            .Produces<ApiResponse<CustomerResponse>>(StatusCodes.Status201Created);

        group.MapPut("/{id:guid}", async (Guid id, UpdateCustomerRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(
                    new UpdateCustomerCommand(id, request.Type, request.Name, request.CompanyName, request.PreferredLanguage, request.Status, request.BranchId, request.DepartmentId, request.Tags ?? []),
                    ct)))
            .RequireAuthorization(Permissions.CustomersUpdate)
            .WithName("UpdateCustomer")
            .Produces<ApiResponse<CustomerResponse>>();

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new DeleteCustomerCommand(id), ct);
                return ApiResults.Success();
            })
            .RequireAuthorization(Permissions.CustomersDelete)
            .WithName("DeleteCustomer")
            .Produces<ApiResponse<object?>>();
    }
}
