using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Pagination;
using CustomerSupportCrm.Contracts.Users;
using CustomerSupportCrm.Domain.Users;
using MediatR;

namespace CustomerSupportCrm.Application.Features.Users.List;

internal sealed class ListUsersHandler(IApplicationDbContext db)
    : IRequestHandler<ListUsersQuery, PagedResult<UserListItemResponse>>
{
    public async Task<PagedResult<UserListItemResponse>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        IQueryable<User> query = db.Users;

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToUpperInvariant();
            // Translated to SQL upper(); culture-sensitive .NET overloads do not apply.
#pragma warning disable CA1304, CA1311, CA1862
            query = query.Where(u => u.Email.ToUpper().Contains(term) || u.DisplayName.ToUpper().Contains(term));
#pragma warning restore CA1304, CA1311, CA1862
        }

        if (request.Status is not null)
        {
            var status = Enum.Parse<UserStatus>(request.Status);
            query = query.Where(u => u.Status == status);
        }

        var descending = request.SortDirection == "desc";
        var ordered = request.SortBy switch
        {
            "email" => descending ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
            "createdAt" => descending ? query.OrderByDescending(u => u.CreatedAt) : query.OrderBy(u => u.CreatedAt),
            "lastLoginAt" => descending ? query.OrderByDescending(u => u.LastLoginAt) : query.OrderBy(u => u.LastLoginAt),
            _ => descending ? query.OrderByDescending(u => u.DisplayName) : query.OrderBy(u => u.DisplayName),
        };

        var page = await ordered
            .ThenBy(u => u.Id)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                u.Status,
                u.LastLoginAt,
                u.CreatedAt,
                Roles = db.Roles
                    .Where(r => u.Roles.Any(membership => membership.RoleId == r.Id))
                    .OrderBy(r => r.Name)
                    .Select(r => r.Name)
                    .ToList(),
            })
            .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);

        return page.Map(row => new UserListItemResponse(
            row.Id.Value,
            row.Email,
            row.DisplayName,
            row.Status.ToString(),
            row.Roles,
            row.LastLoginAt,
            row.CreatedAt));
    }
}
