using System.Text.Json;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Pagination;
using CustomerSupportCrm.Contracts.Audit;
using CustomerSupportCrm.Domain.Audit;
using CustomerSupportCrm.Domain.Users;
using MediatR;

namespace CustomerSupportCrm.Application.Features.AuditLogs.List;

internal sealed class ListAuditLogsHandler(IApplicationDbContext db)
    : IRequestHandler<ListAuditLogsQuery, PagedResult<AuditLogResponse>>
{
    public async Task<PagedResult<AuditLogResponse>> Handle(ListAuditLogsQuery request, CancellationToken cancellationToken)
    {
        IQueryable<AuditLog> query = db.AuditLogs;

        if (request.Action is not null)
        {
            query = query.Where(a => a.Action == request.Action);
        }

        if (request.EntityType is not null)
        {
            query = query.Where(a => a.EntityType == request.EntityType);
        }

        if (request.EntityId is not null)
        {
            query = query.Where(a => a.EntityId == request.EntityId);
        }

        if (request.ActorUserId is { } actor)
        {
            var actorId = new UserId(actor);
            query = query.Where(a => a.ActorUserId == actorId);
        }

        if (request.From is { } from)
        {
            query = query.Where(a => a.OccurredAt >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(a => a.OccurredAt <= to);
        }

        var page = await query
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Select(a => new
            {
                a.Id,
                a.OccurredAt,
                a.ActorUserId,
                ActorDisplayName = db.Users.Where(u => u.Id == a.ActorUserId).Select(u => u.DisplayName).FirstOrDefault(),
                a.Action,
                a.EntityType,
                a.EntityId,
                a.OldValues,
                a.NewValues,
                a.CorrelationId,
                a.IpAddress,
                a.UserAgent,
            })
            .ToPagedResultAsync(request.Page, request.PageSize, cancellationToken);

        return page.Map(row => new AuditLogResponse(
            row.Id,
            row.OccurredAt,
            row.ActorUserId?.Value,
            row.ActorDisplayName,
            row.Action,
            row.EntityType,
            row.EntityId,
            ParseJson(row.OldValues),
            ParseJson(row.NewValues),
            row.CorrelationId,
            row.IpAddress,
            row.UserAgent));
    }

    private static JsonElement? ParseJson(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
