using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Tickets;
using CustomerSupportCrm.Domain.Users;

namespace CustomerSupportCrm.Domain.Sla;

public enum AssignmentStrategy
{
    /// <summary>Only apply routing (department/priority); leave unassigned.</summary>
    None,
    SpecificAgent,
    RoundRobin,
    LeastLoaded,
}

/// <summary>
/// Routes new tickets: conditions (all optional, all must match) → set department/priority and
/// pick an agent. Rules run in <see cref="Order"/>; the first match wins.
/// </summary>
public sealed class AssignmentRule : Entity<Guid>, IAuditableEntity
{
    public const int NameMaxLength = 150;
    public const string InvalidCode = "INVALID_ASSIGNMENT_RULE";

    private AssignmentRule()
    {
        Name = string.Empty;
    }

    private AssignmentRule(Guid id)
        : base(id)
    {
        Name = string.Empty;
    }

    public string Name { get; private set; }

    public bool IsActive { get; private set; }

    public int Order { get; private set; }

    public Guid? MatchCategoryId { get; private set; }

    public Guid? MatchDepartmentId { get; private set; }

    public TicketChannel? MatchChannel { get; private set; }

    public TicketPriority? MatchPriority { get; private set; }

    /// <summary>Case-insensitive text that must appear in the subject or description.</summary>
    public string? MatchKeyword { get; private set; }

    public Guid? SetDepartmentId { get; private set; }

    public TicketPriority? SetPriority { get; private set; }

    public AssignmentStrategy Strategy { get; private set; }

    public UserId? AgentId { get; private set; }

    /// <summary>Round-robin cursor.</summary>
    public UserId? LastAssignedAgentId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static AssignmentRule Create() => new(Guid.CreateVersion7()) { IsActive = true };

    public void Update(
        string name,
        bool isActive,
        int order,
        Guid? matchCategoryId,
        Guid? matchDepartmentId,
        TicketChannel? matchChannel,
        TicketPriority? matchPriority,
        string? matchKeyword,
        Guid? setDepartmentId,
        TicketPriority? setPriority,
        AssignmentStrategy strategy,
        UserId? agentId)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > NameMaxLength)
        {
            throw new DomainException(InvalidCode, "The rule name is not valid.");
        }

        if (strategy == AssignmentStrategy.SpecificAgent && agentId is null)
        {
            throw new DomainException(InvalidCode, "Choose the agent to assign.");
        }

        Name = trimmed;
        IsActive = isActive;
        Order = order;
        MatchCategoryId = matchCategoryId;
        MatchDepartmentId = matchDepartmentId;
        MatchChannel = matchChannel;
        MatchPriority = matchPriority;
        MatchKeyword = string.IsNullOrWhiteSpace(matchKeyword) ? null : matchKeyword.Trim();
        SetDepartmentId = setDepartmentId;
        SetPriority = setPriority;
        Strategy = strategy;
        AgentId = strategy == AssignmentStrategy.SpecificAgent ? agentId : null;
    }

    public bool Matches(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return IsActive
            && (MatchCategoryId is null || MatchCategoryId == ticket.CategoryId)
            && (MatchDepartmentId is null || MatchDepartmentId == ticket.DepartmentId)
            && (MatchChannel is null || MatchChannel == ticket.Channel)
            && (MatchPriority is null || MatchPriority == ticket.Priority)
            && (MatchKeyword is null
                || ticket.Subject.Contains(MatchKeyword, StringComparison.OrdinalIgnoreCase)
                || ticket.Description.Contains(MatchKeyword, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Next agent after the cursor in a stable candidate order.</summary>
    public UserId? PickRoundRobin(IReadOnlyList<UserId> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            return null;
        }

        var index = LastAssignedAgentId is { } last ? candidates.ToList().IndexOf(last) : -1;
        var next = candidates[(index + 1) % candidates.Count];
        LastAssignedAgentId = next;
        return next;
    }
}

public enum EscalationTrigger
{
    SlaWarning,
    SlaBreached,

    /// <summary>Still unassigned <see cref="EscalationRule.AfterMinutes"/> after creation.</summary>
    Unassigned,

    /// <summary>Customer waiting on an agent reply for <see cref="EscalationRule.AfterMinutes"/>.</summary>
    NoAgentReply,
}

/// <summary>What to do when a ticket hits an SLA warning/breach or waits too long.</summary>
public sealed class EscalationRule : Entity<Guid>, IAuditableEntity
{
    public const int NameMaxLength = 150;
    public const string InvalidCode = "INVALID_ESCALATION_RULE";

    private EscalationRule()
    {
        Name = string.Empty;
        NotifyUserIds = [];
    }

    private EscalationRule(Guid id)
        : base(id)
    {
        Name = string.Empty;
        NotifyUserIds = [];
    }

    public string Name { get; private set; }

    public bool IsActive { get; private set; }

    public EscalationTrigger Trigger { get; private set; }

    /// <summary>For SLA triggers: which clock; null means either.</summary>
    public SlaTarget? Target { get; private set; }

    public int? AfterMinutes { get; private set; }

    public TicketPriority? MatchPriority { get; private set; }

    public Guid? MatchDepartmentId { get; private set; }

    public bool EscalateTicket { get; private set; }

    public TicketPriority? RaisePriorityTo { get; private set; }

    public UserId? ReassignToAgentId { get; private set; }

    public bool NotifyAssignee { get; private set; }

    /// <summary>Notify users holding tickets.assign whose scope covers the ticket.</summary>
    public bool NotifyManagers { get; private set; }

    public List<Guid> NotifyUserIds { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static EscalationRule Create() => new(Guid.CreateVersion7()) { IsActive = true };

    public void Update(
        string name,
        bool isActive,
        EscalationTrigger trigger,
        SlaTarget? target,
        int? afterMinutes,
        TicketPriority? matchPriority,
        Guid? matchDepartmentId,
        bool escalateTicket,
        TicketPriority? raisePriorityTo,
        UserId? reassignToAgentId,
        bool notifyAssignee,
        bool notifyManagers,
        IEnumerable<Guid> notifyUserIds)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > NameMaxLength)
        {
            throw new DomainException(InvalidCode, "The rule name is not valid.");
        }

        if (trigger is EscalationTrigger.Unassigned or EscalationTrigger.NoAgentReply && afterMinutes is null or <= 0)
        {
            throw new DomainException(InvalidCode, "Time-based triggers need a positive number of minutes.");
        }

        Name = trimmed;
        IsActive = isActive;
        Trigger = trigger;
        Target = target;
        AfterMinutes = trigger is EscalationTrigger.Unassigned or EscalationTrigger.NoAgentReply ? afterMinutes : null;
        MatchPriority = matchPriority;
        MatchDepartmentId = matchDepartmentId;
        EscalateTicket = escalateTicket;
        RaisePriorityTo = raisePriorityTo;
        ReassignToAgentId = reassignToAgentId;
        NotifyAssignee = notifyAssignee;
        NotifyManagers = notifyManagers;
        NotifyUserIds = [.. notifyUserIds.Distinct()];
    }

    public bool Matches(Ticket ticket, EscalationTrigger trigger, SlaTarget? target)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return IsActive
            && Trigger == trigger
            && (Target is null || target is null || Target == target)
            && (MatchPriority is null || MatchPriority == ticket.Priority)
            && (MatchDepartmentId is null || MatchDepartmentId == ticket.DepartmentId);
    }
}

/// <summary>Marks a time-based escalation as applied to a ticket so it fires once.</summary>
public sealed class EscalationRun : Entity<Guid>
{
    private EscalationRun()
    {
    }

    private EscalationRun(Guid id)
        : base(id)
    {
    }

    public Guid RuleId { get; private set; }

    public Guid TicketId { get; private set; }

    public DateTimeOffset AppliedAt { get; private set; }

    public static EscalationRun Record(Guid ruleId, Guid ticketId, DateTimeOffset now) =>
        new(Guid.CreateVersion7()) { RuleId = ruleId, TicketId = ticketId, AppliedAt = now };
}
