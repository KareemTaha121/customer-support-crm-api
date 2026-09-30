using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Authorization;
using CustomerSupportCrm.Application.Common.Pagination;
using CustomerSupportCrm.Application.Common.Validation;
using CustomerSupportCrm.Application.Features.Tickets.Common;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Tickets;
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

/// <param name="Status">Comma-separated statuses, or "active" for everything not resolved/closed.</param>
/// <param name="Assignee">"me", "unassigned" or a user id.</param>
/// <param name="Sla">"breached", "warning" or "at_risk" (warning or breached).</param>
/// <param name="SortBy">createdAt (default), updatedAt, priority, dueAt, number.</param>
public sealed record ListTicketsQuery(
    int Page = 1,
    int PageSize = PaginationExtensions.DefaultPageSize,
    string? Search = null,
    string? Status = null,
    string? Priority = null,
    string? Channel = null,
    string? Assignee = null,
    Guid? CustomerId = null,
    Guid? CategoryId = null,
    Guid? BranchId = null,
    Guid? DepartmentId = null,
    string? Sla = null,
    bool? Escalated = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    string? SortBy = null,
    string? SortDirection = null) : IRequest<PagedResult<TicketListItemResponse>>;

internal sealed class ListTicketsValidator : AbstractValidator<ListTicketsQuery>
{
    public ListTicketsValidator()
    {
        RuleFor(q => q.Page).ValidPage();
        RuleFor(q => q.PageSize).ValidPageSize();
        RuleFor(q => q.Search).MaximumLength(200);
        RuleFor(q => q.Status).Must(s => s is null || s == "active" || s.Split(',').All(x => Enum.TryParse<TicketStatus>(x.Trim(), out _))).WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(q => q.Priority).Must(p => p is null || p.Split(',').All(x => Enum.TryParse<TicketPriority>(x.Trim(), out _))).WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(q => q.Channel).Must(c => c is null || Enum.TryParse<TicketChannel>(c, out _)).WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(q => q.Assignee).Must(a => a is null or "me" or "unassigned" || Guid.TryParse(a, out _)).WithErrorCode(ErrorCodes.InvalidValue);
        RuleFor(q => q.Sla).OneOf("breached", "warning", "at_risk");
        RuleFor(q => q.SortBy).OneOf("createdAt", "updatedAt", "priority", "dueAt", "number");
        RuleFor(q => q.SortDirection).ValidSortDirection();
    }
}

internal sealed class ListTicketsHandler(IApplicationDbContext db, IAccessScopeProvider scopes, ICurrentUser currentUser)
    : IRequestHandler<ListTicketsQuery, PagedResult<TicketListItemResponse>>
{
    public async Task<PagedResult<TicketListItemResponse>> Handle(ListTicketsQuery request, CancellationToken cancellationToken)
    {
        var scope = await scopes.GetAsync(cancellationToken);
        var query = db.Tickets.AsNoTracking().WhereInScope(scope);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var upper = request.Search.Trim().ToUpperInvariant();
#pragma warning disable CA1304, CA1311, CA1862 // Translated to SQL upper().
            query = query.Where(t =>
                t.Number.ToUpper() == upper
                || t.Subject.ToUpper().Contains(upper)
                || db.Customers.Any(c => c.Id == t.CustomerId && (c.Name.ToUpper().Contains(upper) || c.Number.ToUpper() == upper)));
#pragma warning restore CA1304, CA1311, CA1862
        }

        if (request.Status == "active")
        {
            query = query.Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed);
        }
        else if (request.Status is not null)
        {
            var statuses = request.Status.Split(',').Select(s => Enum.Parse<TicketStatus>(s.Trim())).ToList();
            query = query.Where(t => statuses.Contains(t.Status));
        }

        if (request.Priority is not null)
        {
            var priorities = request.Priority.Split(',').Select(p => Enum.Parse<TicketPriority>(p.Trim())).ToList();
            query = query.Where(t => priorities.Contains(t.Priority));
        }

        if (request.Channel is not null)
        {
            var channel = Enum.Parse<TicketChannel>(request.Channel);
            query = query.Where(t => t.Channel == channel);
        }

        switch (request.Assignee)
        {
            case "me":
                var me = currentUser.UserId;
                query = query.Where(t => t.AssignedAgentId == me);
                break;
            case "unassigned":
                query = query.Where(t => t.AssignedAgentId == null);
                break;
            case { } id:
                var agent = new UserId(Guid.Parse(id));
                query = query.Where(t => t.AssignedAgentId == agent);
                break;
        }

        if (request.CustomerId is { } customerId)
        {
            query = query.Where(t => t.CustomerId == customerId);
        }

        if (request.CategoryId is { } categoryId)
        {
            query = query.Where(t => t.CategoryId == categoryId);
        }

        if (request.BranchId is { } branchId)
        {
            query = query.Where(t => t.BranchId == branchId);
        }

        if (request.DepartmentId is { } departmentId)
        {
            query = query.Where(t => t.DepartmentId == departmentId);
        }

        query = request.Sla switch
        {
            "breached" => query.Where(t => t.FirstResponseBreached || t.ResolutionBreached),
            "warning" => query.Where(t => !t.FirstResponseBreached && !t.ResolutionBreached && (t.FirstResponseWarnedAt != null || t.ResolutionWarnedAt != null)),
            "at_risk" => query.Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed
                && (t.FirstResponseBreached || t.ResolutionBreached || t.FirstResponseWarnedAt != null || t.ResolutionWarnedAt != null)),
            _ => query,
        };

        if (request.Escalated == true)
        {
            query = query.Where(t => t.EscalationLevel > 0);
        }

        if (request.CreatedFrom is { } from)
        {
            query = query.Where(t => t.CreatedAt >= from);
        }

        if (request.CreatedTo is { } to)
        {
            query = query.Where(t => t.CreatedAt <= to);
        }

        var descending = request.SortDirection != "asc";
        var ordered = request.SortBy switch
        {
            "updatedAt" => descending ? query.OrderByDescending(t => t.UpdatedAt) : query.OrderBy(t => t.UpdatedAt),
            "priority" => descending ? query.OrderByDescending(t => t.Priority == TicketPriority.Urgent ? 3 : t.Priority == TicketPriority.High ? 2 : t.Priority == TicketPriority.Medium ? 1 : 0)
                : query.OrderBy(t => t.Priority == TicketPriority.Urgent ? 3 : t.Priority == TicketPriority.High ? 2 : t.Priority == TicketPriority.Medium ? 1 : 0),
            "dueAt" => descending ? query.OrderByDescending(t => t.ResolutionDueAt) : query.OrderBy(t => t.ResolutionDueAt),
            "number" => descending ? query.OrderByDescending(t => t.Number) : query.OrderBy(t => t.Number),
            _ => descending ? query.OrderByDescending(t => t.CreatedAt) : query.OrderBy(t => t.CreatedAt),
        };

        var totalCount = await query.LongCountAsync(cancellationToken);
        var items = await TicketQueries.ProjectListAsync(ordered.ThenBy(t => t.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize), db, cancellationToken);
        return new PagedResult<TicketListItemResponse>(items, PaginationMeta.Create(request.Page, request.PageSize, totalCount));
    }
}

public sealed record GetTicketQuery(Guid TicketId) : IRequest<TicketResponse>;

internal sealed class GetTicketHandler(IApplicationDbContext db, IAccessScopeProvider scopes) : IRequestHandler<GetTicketQuery, TicketResponse>
{
    public async Task<TicketResponse> Handle(GetTicketQuery request, CancellationToken cancellationToken) =>
        await TicketQueries.GetResponseAsync(db, await scopes.GetAsync(cancellationToken), request.TicketId, cancellationToken);
}

public sealed record GetTicketMessagesQuery(Guid TicketId) : IRequest<IReadOnlyList<TicketMessageResponse>>;

internal sealed class GetTicketMessagesHandler(IApplicationDbContext db, IAccessScopeProvider scopes) : IRequestHandler<GetTicketMessagesQuery, IReadOnlyList<TicketMessageResponse>>
{
    public async Task<IReadOnlyList<TicketMessageResponse>> Handle(GetTicketMessagesQuery request, CancellationToken cancellationToken)
    {
        await TicketQueries.EnsureAccessibleAsync(db, await scopes.GetAsync(cancellationToken), request.TicketId, cancellationToken);
        return await TicketQueries.GetMessagesAsync(db, request.TicketId, publicOnly: false, id => TicketQueries.DownloadPath(request.TicketId, id), cancellationToken);
    }
}

public sealed record GetTicketHistoryQuery(Guid TicketId) : IRequest<IReadOnlyList<TicketHistoryResponse>>;

internal sealed class GetTicketHistoryHandler(IApplicationDbContext db, IAccessScopeProvider scopes) : IRequestHandler<GetTicketHistoryQuery, IReadOnlyList<TicketHistoryResponse>>
{
    public async Task<IReadOnlyList<TicketHistoryResponse>> Handle(GetTicketHistoryQuery request, CancellationToken cancellationToken)
    {
        await TicketQueries.EnsureAccessibleAsync(db, await scopes.GetAsync(cancellationToken), request.TicketId, cancellationToken);
        var rows = await db.TicketHistory.AsNoTracking()
            .Where(h => h.TicketId == request.TicketId)
            .OrderBy(h => h.OccurredAt)
            .Select(h => new
            {
                h.Id,
                h.Action,
                h.OldValue,
                h.NewValue,
                ActorName = h.ActorUserId != null
                    ? db.Users.Where(u => u.Id == h.ActorUserId).Select(u => u.DisplayName).FirstOrDefault()
                    : db.Customers.IgnoreQueryFilters().Where(c => c.Id == h.ActorCustomerId).Select(c => c.Name).FirstOrDefault(),
                h.OccurredAt,
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(h => new TicketHistoryResponse(h.Id, h.Action, h.OldValue, h.NewValue, h.ActorName, h.OccurredAt))];
    }
}

internal sealed class TicketQueryEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tickets").WithTags("Tickets");

        group.MapGet("/", async ([AsParameters] ListTicketsQuery query, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(query, ct);
                return ApiResults.Paged(result.Items, result.Meta);
            })
            .RequireAuthorization(Permissions.TicketsView)
            .WithName("ListTickets")
            .Produces<ApiResponse<IReadOnlyList<TicketListItemResponse>>>();

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new GetTicketQuery(id), ct)))
            .RequireAuthorization(Permissions.TicketsView)
            .WithName("GetTicket")
            .Produces<ApiResponse<TicketResponse>>();

        group.MapGet("/{id:guid}/messages", async (Guid id, ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new GetTicketMessagesQuery(id), ct)))
            .RequireAuthorization(Permissions.TicketsView)
            .WithName("GetTicketMessages")
            .Produces<ApiResponse<IReadOnlyList<TicketMessageResponse>>>();

        group.MapGet("/{id:guid}/history", async (Guid id, ISender sender, CancellationToken ct) => ApiResults.Ok(await sender.Send(new GetTicketHistoryQuery(id), ct)))
            .RequireAuthorization(Permissions.TicketsView)
            .WithName("GetTicketHistory")
            .Produces<ApiResponse<IReadOnlyList<TicketHistoryResponse>>>();
    }
}
