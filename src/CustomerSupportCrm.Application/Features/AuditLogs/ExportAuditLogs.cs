using System.Globalization;
using System.Text;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Domain.Roles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.AuditLogs;

/// <summary>CSV export of the audit log for a time window (max 100,000 rows, newest first).</summary>
internal sealed class ExportAuditLogsEndpoint : IEndpoint
{
    private const int MaxRows = 100_000;

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/audit-logs/export.csv", async (DateTimeOffset? from, DateTimeOffset? to, string? action, IApplicationDbContext db, TimeProvider time, CancellationToken ct) =>
            {
                var end = to ?? time.GetUtcNow();
                var start = from ?? end.AddDays(-30);
                var query = db.AuditLogs.AsNoTracking().Where(a => a.OccurredAt >= start && a.OccurredAt <= end);
                if (!string.IsNullOrWhiteSpace(action))
                {
                    query = query.Where(a => a.Action == action);
                }

                var rows = await query.OrderByDescending(a => a.OccurredAt).Take(MaxRows)
                    .Select(a => new
                    {
                        a.OccurredAt,
                        Actor = db.Users.Where(u => u.Id == a.ActorUserId).Select(u => u.Email).FirstOrDefault(),
                        a.Action,
                        a.EntityType,
                        a.EntityId,
                        a.IpAddress,
                        a.CorrelationId,
                        a.OldValues,
                        a.NewValues,
                    })
                    .ToListAsync(ct);

                var csv = new StringBuilder("occurred_at,actor,action,entity_type,entity_id,ip_address,correlation_id,old_values,new_values\n");
                foreach (var r in rows)
                {
                    csv.AppendJoin(',', [
                        r.OccurredAt.ToString("O", CultureInfo.InvariantCulture), Escape(r.Actor), Escape(r.Action), Escape(r.EntityType), Escape(r.EntityId),
                        Escape(r.IpAddress), Escape(r.CorrelationId), Escape(r.OldValues), Escape(r.NewValues)]).Append('\n');
                }

                return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", "audit-log.csv");
            })
            .RequireAuthorization(Permissions.AuditExport)
            .WithTags("Audit")
            .WithName("ExportAuditLogs");

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '-' or '@')
        {
            value = "'" + value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
