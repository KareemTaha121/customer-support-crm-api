namespace CustomerSupportCrm.Contracts.Sla;

public sealed record SlaTargetDto(string Priority, int FirstResponseMinutes, int ResolutionMinutes);

/// <param name="WorkDays">0 = Sunday … 6 = Saturday.</param>
/// <param name="WorkStart">HH:mm in the organization time zone.</param>
public sealed record SlaPolicyRequest(
    string Name,
    string? Description,
    bool IsActive,
    bool IsDefault,
    Guid? CategoryId,
    Guid? DepartmentId,
    bool BusinessHoursOnly,
    IReadOnlyList<int>? WorkDays,
    string? WorkStart,
    string? WorkEnd,
    IReadOnlyList<SlaTargetDto> Targets);

public sealed record SlaPolicyResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    bool IsDefault,
    Guid? CategoryId,
    Guid? DepartmentId,
    bool BusinessHoursOnly,
    IReadOnlyList<int> WorkDays,
    string WorkStart,
    string WorkEnd,
    IReadOnlyList<SlaTargetDto> Targets);

/// <param name="Strategy">None, SpecificAgent, RoundRobin or LeastLoaded.</param>
public sealed record AssignmentRuleRequest(
    string Name,
    bool IsActive,
    int Order,
    Guid? MatchCategoryId,
    Guid? MatchDepartmentId,
    string? MatchChannel,
    string? MatchPriority,
    string? MatchKeyword,
    Guid? SetDepartmentId,
    string? SetPriority,
    string Strategy,
    Guid? AgentId);

public sealed record AssignmentRuleResponse(
    Guid Id,
    string Name,
    bool IsActive,
    int Order,
    Guid? MatchCategoryId,
    Guid? MatchDepartmentId,
    string? MatchChannel,
    string? MatchPriority,
    string? MatchKeyword,
    Guid? SetDepartmentId,
    string? SetPriority,
    string Strategy,
    Guid? AgentId,
    string? AgentName);

/// <param name="Trigger">SlaWarning, SlaBreached, Unassigned or NoAgentReply.</param>
/// <param name="Target">FirstResponse or Resolution (SLA triggers); null = either.</param>
public sealed record EscalationRuleRequest(
    string Name,
    bool IsActive,
    string Trigger,
    string? Target,
    int? AfterMinutes,
    string? MatchPriority,
    Guid? MatchDepartmentId,
    bool EscalateTicket,
    string? RaisePriorityTo,
    Guid? ReassignToAgentId,
    bool NotifyAssignee,
    bool NotifyManagers,
    IReadOnlyList<Guid>? NotifyUserIds);

public sealed record EscalationRuleResponse(
    Guid Id,
    string Name,
    bool IsActive,
    string Trigger,
    string? Target,
    int? AfterMinutes,
    string? MatchPriority,
    Guid? MatchDepartmentId,
    bool EscalateTicket,
    string? RaisePriorityTo,
    Guid? ReassignToAgentId,
    bool NotifyAssignee,
    bool NotifyManagers,
    IReadOnlyList<Guid> NotifyUserIds);
