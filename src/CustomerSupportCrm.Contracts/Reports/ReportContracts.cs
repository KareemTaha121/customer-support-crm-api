namespace CustomerSupportCrm.Contracts.Reports;

public sealed record CountByKey(string Key, string? Label, long Count);

public sealed record VolumePoint(DateTimeOffset Period, long Created, long Resolved);

public sealed record TicketVolumeReport(
    DateTimeOffset From,
    DateTimeOffset To,
    string GroupBy,
    long TotalCreated,
    long TotalResolved,
    IReadOnlyList<VolumePoint> Series,
    IReadOnlyList<CountByKey> ByStatus,
    IReadOnlyList<CountByKey> ByPriority,
    IReadOnlyList<CountByKey> ByChannel,
    IReadOnlyList<CountByKey> ByCategory);

public sealed record SlaBreakdownRow(string Key, string? Label, long Tickets, double? FirstResponseCompliance, double? ResolutionCompliance, long Breached);

public sealed record SlaPerformanceReport(
    DateTimeOffset From,
    DateTimeOffset To,
    long TicketsWithSla,
    double? FirstResponseCompliance,
    double? ResolutionCompliance,
    long FirstResponseBreached,
    long ResolutionBreached,
    double? AverageFirstResponseMinutes,
    double? AverageResolutionMinutes,
    IReadOnlyList<SlaBreakdownRow> ByPriority,
    IReadOnlyList<SlaBreakdownRow> ByDepartment);

public sealed record AgentPerformanceRow(
    Guid AgentId,
    string AgentName,
    long Assigned,
    long Resolved,
    long OpenNow,
    double? AverageFirstResponseMinutes,
    double? AverageResolutionMinutes,
    double? SlaCompliance,
    double? AverageSatisfaction,
    long SatisfactionResponses);

public sealed record AgentPerformanceReport(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<AgentPerformanceRow> Agents);

public sealed record RatingCount(int Rating, long Count);

public sealed record SatisfactionPoint(DateTimeOffset Period, double? Average, long Responses);

public sealed record SatisfactionComment(Guid TicketId, string TicketNumber, int Rating, string Comment, DateTimeOffset SubmittedAt);

public sealed record CustomerSatisfactionReport(
    DateTimeOffset From,
    DateTimeOffset To,
    double? Average,
    long Responses,
    double? PositiveShare,
    IReadOnlyList<RatingCount> Distribution,
    IReadOnlyList<SatisfactionPoint> Trend,
    IReadOnlyList<SatisfactionComment> RecentComments);

public sealed record ManagementDashboard(
    long OpenTickets,
    long UnassignedTickets,
    long EscalatedTickets,
    long AtRiskTickets,
    long CreatedToday,
    long ResolvedToday,
    double? SlaCompliance30d,
    double? Satisfaction30d,
    double? AverageFirstResponseMinutes30d,
    double? AverageResolutionMinutes30d,
    IReadOnlyList<CountByKey> BacklogByDepartment,
    IReadOnlyList<CountByKey> TopCategories30d,
    IReadOnlyList<VolumePoint> Last14Days);
