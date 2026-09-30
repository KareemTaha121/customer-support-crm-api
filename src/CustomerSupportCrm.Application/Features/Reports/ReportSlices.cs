using System.Globalization;
using System.Text;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Abstractions.Reporting;
using CustomerSupportCrm.Application.Common.Authorization;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Reports;
using CustomerSupportCrm.Domain.Roles;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Reports;

/// <summary>Query-string filter common to all reports. Defaults to the last 30 days.</summary>
public sealed record ReportQuery(DateTimeOffset? From = null, DateTimeOffset? To = null, Guid? BranchId = null, Guid? DepartmentId = null, string? GroupBy = null);

/// <summary>Resolves a report query into a validated, scoped filter.</summary>
public sealed class ReportFilterFactory(IApplicationDbContext db, IAccessScopeProvider scopes, TimeProvider time)
{
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(366);

    public async Task<ReportFilter> CreateAsync(ReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var to = query.To ?? time.GetUtcNow();
        var from = query.From ?? to.AddDays(-30);
        var groupBy = query.GroupBy ?? "day";

        if (from >= to || to - from > MaxRange || groupBy is not ("day" or "week" or "month"))
        {
            throw new ValidationException([new ValidationFailure("From", "The range must be positive, at most 366 days, grouped by day, week or month.") { ErrorCode = ErrorCodes.OutOfRange }]);
        }

        var timeZone = await db.Organizations.AsNoTracking().Select(o => o.TimeZone).FirstOrDefaultAsync(cancellationToken) ?? "UTC";
        return new ReportFilter(from, to, query.BranchId, query.DepartmentId, await scopes.GetAsync(cancellationToken), timeZone, groupBy);
    }
}

internal static class Csv
{
    public static IResult File(string fileName, IEnumerable<string> header, IEnumerable<IEnumerable<object?>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', header.Select(Escape)));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(',', row.Select(value => Escape(Format(value)))));
        }

        // BOM so spreadsheet apps detect UTF-8 (Arabic names).
        return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray(), "text/csv; charset=utf-8", fileName);
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTimeOffset date => date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>Quotes fields and neutralizes spreadsheet formula injection.</summary>
    private static string Escape(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            value = "'" + value;
        }

        return value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }
}

internal sealed class ReportEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reports").WithTags("Reports").RequireAuthorization(Permissions.ReportsView);

        group.MapGet("/ticket-volume", async ([AsParameters] ReportQuery query, ReportFilterFactory filters, IReportingQueries reports, CancellationToken ct) =>
                ApiResults.Ok(await reports.TicketVolumeAsync(await filters.CreateAsync(query, ct), ct)))
            .WithName("TicketVolumeReport")
            .Produces<ApiResponse<TicketVolumeReport>>();

        group.MapGet("/sla-performance", async ([AsParameters] ReportQuery query, ReportFilterFactory filters, IReportingQueries reports, CancellationToken ct) =>
                ApiResults.Ok(await reports.SlaPerformanceAsync(await filters.CreateAsync(query, ct), ct)))
            .WithName("SlaPerformanceReport")
            .Produces<ApiResponse<SlaPerformanceReport>>();

        group.MapGet("/agent-performance", async ([AsParameters] ReportQuery query, ReportFilterFactory filters, IReportingQueries reports, CancellationToken ct) =>
                ApiResults.Ok(await reports.AgentPerformanceAsync(await filters.CreateAsync(query, ct), ct)))
            .WithName("AgentPerformanceReport")
            .Produces<ApiResponse<AgentPerformanceReport>>();

        group.MapGet("/customer-satisfaction", async ([AsParameters] ReportQuery query, ReportFilterFactory filters, IReportingQueries reports, CancellationToken ct) =>
                ApiResults.Ok(await reports.CustomerSatisfactionAsync(await filters.CreateAsync(query, ct), ct)))
            .WithName("CustomerSatisfactionReport")
            .Produces<ApiResponse<CustomerSatisfactionReport>>();

        group.MapGet("/management-dashboard", async ([AsParameters] ReportQuery query, ReportFilterFactory filters, IReportingQueries reports, TimeProvider time, CancellationToken ct) =>
                ApiResults.Ok(await reports.ManagementDashboardAsync(await filters.CreateAsync(query, ct), time.GetUtcNow(), ct)))
            .WithName("ManagementDashboard")
            .Produces<ApiResponse<ManagementDashboard>>();

        var export = group.MapGroup("/export").RequireAuthorization(Permissions.ReportsExport);

        export.MapGet("/ticket-volume.csv", async ([AsParameters] ReportQuery query, ReportFilterFactory filters, IReportingQueries reports, CancellationToken ct) =>
            {
                var report = await reports.TicketVolumeAsync(await filters.CreateAsync(query, ct), ct);
                return Csv.File("ticket-volume.csv", ["period", "created", "resolved"], report.Series.Select(p => new object?[] { p.Period, p.Created, p.Resolved }));
            })
            .WithName("ExportTicketVolume");

        export.MapGet("/sla-performance.csv", async ([AsParameters] ReportQuery query, ReportFilterFactory filters, IReportingQueries reports, CancellationToken ct) =>
            {
                var report = await reports.SlaPerformanceAsync(await filters.CreateAsync(query, ct), ct);
                return Csv.File(
                    "sla-performance.csv",
                    ["department", "tickets", "first_response_compliance_pct", "resolution_compliance_pct", "breached"],
                    report.ByDepartment.Select(r => new object?[] { r.Label ?? r.Key, r.Tickets, r.FirstResponseCompliance, r.ResolutionCompliance, r.Breached }));
            })
            .WithName("ExportSlaPerformance");

        export.MapGet("/agent-performance.csv", async ([AsParameters] ReportQuery query, ReportFilterFactory filters, IReportingQueries reports, CancellationToken ct) =>
            {
                var report = await reports.AgentPerformanceAsync(await filters.CreateAsync(query, ct), ct);
                return Csv.File(
                    "agent-performance.csv",
                    ["agent", "assigned", "resolved", "open_now", "avg_first_response_min", "avg_resolution_min", "sla_compliance_pct", "avg_csat", "csat_responses"],
                    report.Agents.Select(a => new object?[] { a.AgentName, a.Assigned, a.Resolved, a.OpenNow, a.AverageFirstResponseMinutes, a.AverageResolutionMinutes, a.SlaCompliance, a.AverageSatisfaction, a.SatisfactionResponses }));
            })
            .WithName("ExportAgentPerformance");

        export.MapGet("/customer-satisfaction.csv", async ([AsParameters] ReportQuery query, ReportFilterFactory filters, IReportingQueries reports, CancellationToken ct) =>
            {
                var report = await reports.CustomerSatisfactionAsync(await filters.CreateAsync(query, ct), ct);
                return Csv.File("customer-satisfaction.csv", ["period", "average", "responses"], report.Trend.Select(p => new object?[] { p.Period, p.Average, p.Responses }));
            })
            .WithName("ExportCustomerSatisfaction");
    }
}
