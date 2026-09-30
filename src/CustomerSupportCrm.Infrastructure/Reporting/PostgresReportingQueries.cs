using CustomerSupportCrm.Application.Abstractions.Reporting;
using CustomerSupportCrm.Contracts.Reports;
using CustomerSupportCrm.Infrastructure.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace CustomerSupportCrm.Infrastructure.Reporting;

/// <summary>
/// Report aggregates computed in PostgreSQL. Every query applies the caller's branch/department
/// scope and excludes soft-deleted tickets. Periods are bucketed in the organization time zone.
/// </summary>
internal sealed class PostgresReportingQueries(SqlRunner sql) : IReportingQueries
{
    /// <summary>Scope + optional unit filter on the <c>t</c> (tickets) alias.</summary>
    private const string Scope = """
        NOT t.is_deleted
        AND (@all OR t.branch_id = ANY(@branches) OR t.department_id = ANY(@departments))
        AND (@branch::uuid IS NULL OR t.branch_id = @branch)
        AND (@department::uuid IS NULL OR t.department_id = @department)
        """;

    /// <summary>Period bucket of a timestamp column in the organization time zone.</summary>
    private static string PeriodOf(string column) => $"date_trunc(@unit, {column} AT TIME ZONE @tz) AT TIME ZONE @tz";

    public async Task<TicketVolumeReport> TicketVolumeAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var series = await SeriesAsync(filter, filter.From, filter.To, cancellationToken);
        var created = await BreakdownAsync(filter, "t.status", null, cancellationToken);

        return new TicketVolumeReport(
            filter.From,
            filter.To,
            filter.GroupBy,
            series.Sum(p => p.Created),
            series.Sum(p => p.Resolved),
            series,
            created,
            await BreakdownAsync(filter, "t.priority", null, cancellationToken),
            await BreakdownAsync(filter, "t.channel", null, cancellationToken),
            await BreakdownAsync(filter, "t.category_id::text", "(SELECT c.name FROM ticket_categories c WHERE c.id = t.category_id)", cancellationToken));
    }

    public async Task<SlaPerformanceReport> SlaPerformanceAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var totals = (await SlaRowsAsync(filter, "'all'", "NULL", cancellationToken)).FirstOrDefault();
        var averages = await sql.QueryAsync(
            $"""
            SELECT avg(extract(epoch FROM (t.first_responded_at - t.created_at)) / 60) FILTER (WHERE t.first_responded_at IS NOT NULL),
                   avg(extract(epoch FROM (t.resolved_at - t.created_at)) / 60) FILTER (WHERE t.resolved_at IS NOT NULL)
            FROM tickets t
            WHERE {Scope} AND t.created_at >= @from AND t.created_at < @to
            """,
            Parameters(filter),
            r => (FirstResponse: r.GetNullableDouble(0), Resolution: r.GetNullableDouble(1)),
            cancellationToken);

        return new SlaPerformanceReport(
            filter.From,
            filter.To,
            totals?.Tickets ?? 0,
            totals?.FirstResponseCompliance,
            totals?.ResolutionCompliance,
            await ScalarCountAsync(filter, "t.first_response_breached", cancellationToken),
            await ScalarCountAsync(filter, "t.resolution_breached", cancellationToken),
            Round(averages[0].FirstResponse),
            Round(averages[0].Resolution),
            await SlaRowsAsync(filter, "t.priority", "NULL", cancellationToken),
            await SlaRowsAsync(filter, "coalesce(t.department_id::text, '-')", "(SELECT d.name FROM departments d WHERE d.id = t.department_id)", cancellationToken));
    }

    public async Task<AgentPerformanceReport> AgentPerformanceAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var rows = await sql.QueryAsync(
            $"""
            SELECT u.id,
                   u.display_name,
                   count(*) FILTER (WHERE t.created_at >= @from AND t.created_at < @to),
                   count(*) FILTER (WHERE t.resolved_at >= @from AND t.resolved_at < @to),
                   count(*) FILTER (WHERE t.status NOT IN ('Resolved', 'Closed')),
                   avg(extract(epoch FROM (t.first_responded_at - t.created_at)) / 60) FILTER (WHERE t.first_responded_at >= @from AND t.first_responded_at < @to),
                   avg(extract(epoch FROM (t.resolved_at - t.created_at)) / 60) FILTER (WHERE t.resolved_at >= @from AND t.resolved_at < @to),
                   avg(CASE WHEN t.resolution_breached THEN 0.0 ELSE 1.0 END) FILTER (WHERE t.resolution_due_at IS NOT NULL AND t.resolved_at >= @from AND t.resolved_at < @to),
                   avg(t.satisfaction_rating) FILTER (WHERE t.satisfaction_submitted_at >= @from AND t.satisfaction_submitted_at < @to),
                   count(t.satisfaction_rating) FILTER (WHERE t.satisfaction_submitted_at >= @from AND t.satisfaction_submitted_at < @to)
            FROM tickets t
            JOIN users u ON u.id = t.assigned_agent_id
            WHERE {Scope}
            GROUP BY u.id, u.display_name
            HAVING count(*) FILTER (WHERE t.created_at >= @from AND t.created_at < @to) > 0
                OR count(*) FILTER (WHERE t.resolved_at >= @from AND t.resolved_at < @to) > 0
                OR count(*) FILTER (WHERE t.status NOT IN ('Resolved', 'Closed')) > 0
            ORDER BY 4 DESC, 2
            """,
            Parameters(filter),
            r => new AgentPerformanceRow(
                r.GetGuid(0),
                r.GetString(1),
                r.GetInt64(2),
                r.GetInt64(3),
                r.GetInt64(4),
                Round(r.GetNullableDouble(5)),
                Round(r.GetNullableDouble(6)),
                Percent(r.GetNullableDouble(7)),
                Round(r.GetNullableDouble(8)),
                r.GetInt64(9)),
            cancellationToken);

        return new AgentPerformanceReport(filter.From, filter.To, rows);
    }

    public async Task<CustomerSatisfactionReport> CustomerSatisfactionAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        const string Rated = "t.satisfaction_rating IS NOT NULL AND t.satisfaction_submitted_at >= @from AND t.satisfaction_submitted_at < @to";

        var summary = (await sql.QueryAsync(
            $"SELECT avg(t.satisfaction_rating), count(*), avg(CASE WHEN t.satisfaction_rating >= 4 THEN 1.0 ELSE 0.0 END) FROM tickets t WHERE {Scope} AND {Rated}",
            Parameters(filter),
            r => (Average: r.GetNullableDouble(0), Count: r.GetInt64(1), Positive: r.GetNullableDouble(2)),
            cancellationToken))[0];

        var distribution = await sql.QueryAsync(
            $"SELECT t.satisfaction_rating, count(*) FROM tickets t WHERE {Scope} AND {Rated} GROUP BY 1 ORDER BY 1",
            Parameters(filter),
            r => new RatingCount(r.GetInt32(0), r.GetInt64(1)),
            cancellationToken);

        var trend = await sql.QueryAsync(
            $"SELECT {PeriodOf("t.satisfaction_submitted_at")}, avg(t.satisfaction_rating), count(*) FROM tickets t WHERE {Scope} AND {Rated} GROUP BY 1 ORDER BY 1",
            Parameters(filter),
            r => new SatisfactionPoint(r.GetUtc(0), Round(r.GetNullableDouble(1)), r.GetInt64(2)),
            cancellationToken);

        var comments = await sql.QueryAsync(
            $"SELECT t.id, t.number, t.satisfaction_rating, t.satisfaction_comment, t.satisfaction_submitted_at FROM tickets t WHERE {Scope} AND {Rated} AND t.satisfaction_comment IS NOT NULL ORDER BY t.satisfaction_submitted_at DESC LIMIT 20",
            Parameters(filter),
            r => new SatisfactionComment(r.GetGuid(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetUtc(4)),
            cancellationToken);

        var full = Enumerable.Range(1, 5).Select(rating => distribution.FirstOrDefault(d => d.Rating == rating) ?? new RatingCount(rating, 0)).ToList();
        return new CustomerSatisfactionReport(filter.From, filter.To, Round(summary.Average), summary.Count, Percent(summary.Positive), full, trend, comments);
    }

    public async Task<ManagementDashboard> ManagementDashboardAsync(ReportFilter filter, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var current = (await sql.QueryAsync(
            $"""
            SELECT count(*) FILTER (WHERE t.status NOT IN ('Resolved', 'Closed')),
                   count(*) FILTER (WHERE t.status NOT IN ('Resolved', 'Closed') AND t.assigned_agent_id IS NULL),
                   count(*) FILTER (WHERE t.status = 'Escalated'),
                   count(*) FILTER (WHERE t.status NOT IN ('Resolved', 'Closed')
                        AND (t.first_response_breached OR t.resolution_breached OR t.first_response_warned_at IS NOT NULL OR t.resolution_warned_at IS NOT NULL)),
                   count(*) FILTER (WHERE t.created_at >= date_trunc('day', @now AT TIME ZONE @tz) AT TIME ZONE @tz),
                   count(*) FILTER (WHERE t.resolved_at >= date_trunc('day', @now AT TIME ZONE @tz) AT TIME ZONE @tz)
            FROM tickets t
            WHERE {Scope}
            """,
            [.. Parameters(filter), new NpgsqlParameter("now", now)],
            r => (Open: r.GetInt64(0), Unassigned: r.GetInt64(1), Escalated: r.GetInt64(2), AtRisk: r.GetInt64(3), CreatedToday: r.GetInt64(4), ResolvedToday: r.GetInt64(5)),
            cancellationToken))[0];

        var last30 = filter with { From = now.AddDays(-30), To = now, GroupBy = "day" };
        var sla = await SlaPerformanceAsync(last30, cancellationToken);
        var csat = await CustomerSatisfactionAsync(last30, cancellationToken);

        var backlog = await sql.QueryAsync(
            $"""
            SELECT coalesce(t.department_id::text, '-'), (SELECT d.name FROM departments d WHERE d.id = t.department_id), count(*)
            FROM tickets t
            WHERE {Scope} AND t.status NOT IN ('Resolved', 'Closed')
            GROUP BY 1, 2
            ORDER BY 3 DESC
            LIMIT 10
            """,
            Parameters(filter),
            r => new CountByKey(r.GetString(0), r.GetNullableString(1), r.GetInt64(2)),
            cancellationToken);

        var categories = (await BreakdownAsync(last30, "t.category_id::text", "(SELECT c.name FROM ticket_categories c WHERE c.id = t.category_id)", cancellationToken)).Take(5).ToList();
        var series = await SeriesAsync(filter with { GroupBy = "day" }, now.AddDays(-14), now, cancellationToken);

        return new ManagementDashboard(
            current.Open,
            current.Unassigned,
            current.Escalated,
            current.AtRisk,
            current.CreatedToday,
            current.ResolvedToday,
            sla.ResolutionCompliance,
            csat.Average,
            sla.AverageFirstResponseMinutes,
            sla.AverageResolutionMinutes,
            backlog,
            categories,
            series);
    }

    private async Task<List<VolumePoint>> SeriesAsync(ReportFilter filter, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var createdPeriod = PeriodOf("t.created_at");
        var resolvedPeriod = PeriodOf("t.resolved_at");
        return await sql.QueryAsync(
            $"""
            WITH created AS (
                SELECT {createdPeriod} AS period, count(*) AS n FROM tickets t
                WHERE {Scope} AND t.created_at >= @rangeFrom AND t.created_at < @rangeTo GROUP BY 1),
            resolved AS (
                SELECT {resolvedPeriod} AS period, count(*) AS n FROM tickets t
                WHERE {Scope} AND t.resolved_at >= @rangeFrom AND t.resolved_at < @rangeTo GROUP BY 1)
            SELECT coalesce(c.period, r.period), coalesce(c.n, 0), coalesce(r.n, 0)
            FROM created c FULL JOIN resolved r ON c.period = r.period
            ORDER BY 1
            """,
            [.. Parameters(filter), new NpgsqlParameter("rangeFrom", from), new NpgsqlParameter("rangeTo", to)],
            r => new VolumePoint(r.GetUtc(0), r.GetInt64(1), r.GetInt64(2)),
            cancellationToken);
    }

    /// <param name="keyExpression">Grouping expression on <c>t</c>, cast to text.</param>
    /// <param name="labelExpression">Optional display label expression.</param>
    private Task<List<CountByKey>> BreakdownAsync(ReportFilter filter, string keyExpression, string? labelExpression, CancellationToken cancellationToken) =>
        sql.QueryAsync(
            $"""
            SELECT coalesce({keyExpression}, '-'), {labelExpression ?? "NULL"}, count(*)
            FROM tickets t
            WHERE {Scope} AND t.created_at >= @from AND t.created_at < @to
            GROUP BY 1, 2
            ORDER BY 3 DESC
            """,
            Parameters(filter),
            r => new CountByKey(r.GetString(0), r.GetNullableString(1), r.GetInt64(2)),
            cancellationToken);

    private Task<List<SlaBreakdownRow>> SlaRowsAsync(ReportFilter filter, string keyExpression, string labelExpression, CancellationToken cancellationToken) =>
        sql.QueryAsync(
            $"""
            SELECT {keyExpression}, {labelExpression},
                   count(*),
                   avg(CASE WHEN t.first_response_breached THEN 0.0 ELSE 1.0 END)
                       FILTER (WHERE t.first_response_due_at IS NOT NULL AND (t.first_responded_at IS NOT NULL OR t.first_response_breached)),
                   avg(CASE WHEN t.resolution_breached THEN 0.0 ELSE 1.0 END)
                       FILTER (WHERE t.resolution_due_at IS NOT NULL AND (t.resolved_at IS NOT NULL OR t.resolution_breached)),
                   count(*) FILTER (WHERE t.first_response_breached OR t.resolution_breached)
            FROM tickets t
            WHERE {Scope} AND t.sla_policy_id IS NOT NULL AND t.created_at >= @from AND t.created_at < @to
            GROUP BY 1, 2
            ORDER BY 3 DESC
            """,
            Parameters(filter),
            r => new SlaBreakdownRow(r.GetString(0), r.GetNullableString(1), r.GetInt64(2), Percent(r.GetNullableDouble(3)), Percent(r.GetNullableDouble(4)), r.GetInt64(5)),
            cancellationToken);

    private async Task<long> ScalarCountAsync(ReportFilter filter, string condition, CancellationToken cancellationToken) =>
        await sql.ScalarAsync<long>($"SELECT count(*) FROM tickets t WHERE {Scope} AND t.created_at >= @from AND t.created_at < @to AND {condition}", Parameters(filter), cancellationToken);

    /// <summary>Fresh parameters per command (Npgsql parameters cannot be shared).</summary>
    private static List<NpgsqlParameter> Parameters(ReportFilter filter) =>
    [
        new("from", filter.From),
        new("to", filter.To),
        new("all", filter.Scope.AllBranches),
        new("branches", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = filter.Scope.BranchIds.ToArray() },
        new("departments", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = filter.Scope.DepartmentIds.ToArray() },
        new("branch", NpgsqlDbType.Uuid) { Value = (object?)filter.BranchId ?? DBNull.Value },
        new("department", NpgsqlDbType.Uuid) { Value = (object?)filter.DepartmentId ?? DBNull.Value },
        new("tz", filter.TimeZone),
        new("unit", filter.GroupBy),
    ];

    private static double? Round(double? value) => value is { } v ? Math.Round(v, 2) : null;

    private static double? Percent(double? share) => share is { } v ? Math.Round(v * 100, 1) : null;
}
