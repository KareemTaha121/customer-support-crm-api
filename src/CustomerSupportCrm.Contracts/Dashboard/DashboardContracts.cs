using CustomerSupportCrm.Contracts.Tickets;

namespace CustomerSupportCrm.Contracts.Dashboard;

public sealed record AgentDashboardCounts(
    int MyOpen,
    int MyPendingCustomer,
    int MyAtRisk,
    int MyResolvedToday,
    int UnassignedInScope,
    int EscalatedInScope,
    int OpenTasks,
    int OverdueTasks,
    int UnreadNotifications);

public sealed record RecentCustomerResponse(Guid Id, string Number, string Name, DateTimeOffset LastInteractionAt);

public sealed record TaskResponse(
    Guid Id,
    string Title,
    string? Notes,
    Guid AssigneeId,
    string AssigneeName,
    Guid? TicketId,
    string? TicketNumber,
    Guid? CustomerId,
    string? CustomerName,
    DateTimeOffset? DueAt,
    DateTimeOffset? RemindAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt);

public sealed record AgentDashboardResponse(
    AgentDashboardCounts Counts,
    IReadOnlyList<TicketListItemResponse> MyTickets,
    IReadOnlyList<TicketListItemResponse> SlaAtRisk,
    IReadOnlyList<TicketListItemResponse> PendingEscalations,
    IReadOnlyList<RecentCustomerResponse> RecentCustomers,
    IReadOnlyList<TaskResponse> MyTasks);

/// <param name="AssigneeId">Defaults to the caller; assigning others requires tickets.assign.</param>
public sealed record TaskRequest(string Title, string? Notes, Guid? AssigneeId, Guid? TicketId, Guid? CustomerId, DateTimeOffset? DueAt, DateTimeOffset? RemindAt);

/// <param name="Shared">Shared with everyone (requires quickreplies.manage) or personal.</param>
public sealed record QuickReplyRequest(string Title, string? Shortcut, string Body, string Language, Guid? CategoryId, bool Shared);

public sealed record QuickReplyResponse(
    Guid Id,
    string Title,
    string? Shortcut,
    string Body,
    string Language,
    Guid? CategoryId,
    bool Shared,
    bool CanEdit,
    int UsageCount);

public sealed record RenderedQuickReplyResponse(string Body);
