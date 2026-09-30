using CustomerSupportCrm.Application.Common.Pagination;
using CustomerSupportCrm.Contracts.Audit;
using MediatR;

namespace CustomerSupportCrm.Application.Features.AuditLogs.List;

/// <summary>Audit entries, newest first, with optional exact-match filters and a time window.</summary>
public sealed record ListAuditLogsQuery(
    int Page = 1,
    int PageSize = PaginationExtensions.DefaultPageSize,
    string? Action = null,
    string? EntityType = null,
    string? EntityId = null,
    Guid? ActorUserId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null)
    : IRequest<PagedResult<AuditLogResponse>>;
