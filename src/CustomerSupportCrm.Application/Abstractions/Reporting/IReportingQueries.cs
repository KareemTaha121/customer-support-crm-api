using CustomerSupportCrm.Application.Common.Authorization;
using CustomerSupportCrm.Contracts.Reports;

namespace CustomerSupportCrm.Application.Abstractions.Reporting;

/// <param name="GroupBy">day, week or month (in the organization time zone).</param>
public sealed record ReportFilter(
    DateTimeOffset From,
    DateTimeOffset To,
    Guid? BranchId,
    Guid? DepartmentId,
    AccessScope Scope,
    string TimeZone,
    string GroupBy = "day");

/// <summary>
/// Database-side aggregation for reports (never loads ticket rows into memory). Implemented
/// with PostgreSQL SQL in Infrastructure.
/// </summary>
public interface IReportingQueries
{
    Task<TicketVolumeReport> TicketVolumeAsync(ReportFilter filter, CancellationToken cancellationToken);

    Task<SlaPerformanceReport> SlaPerformanceAsync(ReportFilter filter, CancellationToken cancellationToken);

    Task<AgentPerformanceReport> AgentPerformanceAsync(ReportFilter filter, CancellationToken cancellationToken);

    Task<CustomerSatisfactionReport> CustomerSatisfactionAsync(ReportFilter filter, CancellationToken cancellationToken);

    Task<ManagementDashboard> ManagementDashboardAsync(ReportFilter filter, DateTimeOffset now, CancellationToken cancellationToken);
}
