using CustomerSupportCrm.Application.Common.Pagination;
using CustomerSupportCrm.Contracts.Users;
using MediatR;

namespace CustomerSupportCrm.Application.Features.Users.List;

/// <param name="Search">Case-insensitive match on email or display name.</param>
/// <param name="Status">Active or Disabled.</param>
/// <param name="SortBy">displayName (default), email, createdAt or lastLoginAt.</param>
/// <param name="SortDirection">asc (default) or desc.</param>
public sealed record ListUsersQuery(
    int Page = 1,
    int PageSize = PaginationExtensions.DefaultPageSize,
    string? Search = null,
    string? Status = null,
    string? SortBy = null,
    string? SortDirection = null)
    : IRequest<PagedResult<UserListItemResponse>>;
