using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Users;

namespace CustomerSupportCrm.Domain.Tickets;

/// <summary>
/// A support request. The aggregate owns status transitions, assignment, priority, SLA clocks,
/// escalation and satisfaction. Messages and history are separate append-only records written
/// by domain-event handlers, so the aggregate stays small.
/// </summary>
public sealed class Ticket : AggregateRoot<Guid>, IAuditableEntity, ISoftDeletable, IScopedEntity
{
    public const int NumberMaxLength = 20;
    public const int SubjectMaxLength = 300;
    public const int DescriptionMaxLength = 20_000;
    public const int MaxTags = 20;

    public const string InvalidCode = "INVALID_TICKET";
    public const string InvalidTransitionCode = "INVALID_STATUS_TRANSITION";
    public const string ClosedCode = "TICKET_CLOSED";
    public const string FeedbackNotAllowedCode = "FEEDBACK_NOT_ALLOWED";

    private static readonly Dictionary<TicketStatus, TicketStatus[]> Transitions = new()
    {
        [TicketStatus.New] = [TicketStatus.Open, TicketStatus.PendingCustomer, TicketStatus.PendingInternal, TicketStatus.Resolved],
        [TicketStatus.Open] = [TicketStatus.PendingCustomer, TicketStatus.PendingInternal, TicketStatus.Resolved],
        [TicketStatus.PendingCustomer] = [TicketStatus.Open, TicketStatus.PendingInternal, TicketStatus.Resolved],
        [TicketStatus.PendingInternal] = [TicketStatus.Open, TicketStatus.PendingCustomer, TicketStatus.Resolved],
        [TicketStatus.Escalated] = [TicketStatus.Open, TicketStatus.PendingCustomer, TicketStatus.PendingInternal, TicketStatus.Resolved],
        [TicketStatus.Resolved] = [TicketStatus.Closed],
        [TicketStatus.Closed] = [],
    };

    private Ticket()
    {
        Number = string.Empty;
        Subject = string.Empty;
        Description = string.Empty;
        Tags = [];
    }

    private Ticket(Guid id, string number)
        : base(id)
    {
        Number = number;
        Subject = string.Empty;
        Description = string.Empty;
        Tags = [];
    }

    /// <summary>Human-friendly unique number, e.g. T-000123. Also used to thread email replies.</summary>
    public string Number { get; private set; }

    public string Subject { get; private set; }

    public string Description { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid? CategoryId { get; private set; }

    public TicketPriority Priority { get; private set; }

    public TicketStatus Status { get; private set; }

    public TicketChannel Channel { get; private set; }

    /// <summary>Address replies go to on the originating channel (email or E.164 phone).</summary>
    public string? ReplyAddress { get; private set; }

    public UserId? AssignedAgentId { get; private set; }

    public Guid BranchId { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public List<string> Tags { get; private set; }

    public Guid? SlaPolicyId { get; private set; }

    public DateTimeOffset? FirstResponseDueAt { get; private set; }

    public DateTimeOffset? ResolutionDueAt { get; private set; }

    public DateTimeOffset? FirstRespondedAt { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public bool FirstResponseBreached { get; private set; }

    public bool ResolutionBreached { get; private set; }

    public DateTimeOffset? FirstResponseWarnedAt { get; private set; }

    public DateTimeOffset? ResolutionWarnedAt { get; private set; }

    public int EscalationLevel { get; private set; }

    public DateTimeOffset? EscalatedAt { get; private set; }

    public DateTimeOffset? LastCustomerMessageAt { get; private set; }

    public DateTimeOffset? LastAgentMessageAt { get; private set; }

    public int? SatisfactionRating { get; private set; }

    public string? SatisfactionComment { get; private set; }

    public DateTimeOffset? SatisfactionSubmittedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public bool IsActive => Status.IsActive();

    /// <summary>Statuses reachable with <see cref="ChangeStatus"/> from <paramref name="status"/>.</summary>
    public static IReadOnlyList<TicketStatus> AllowedTransitions(TicketStatus status) => Transitions[status];

    public static string FormatNumber(long sequence) => $"T-{sequence:D6}";

    public static Ticket Create(
        string number,
        string subject,
        string description,
        Guid customerId,
        Guid? categoryId,
        TicketPriority priority,
        TicketChannel channel,
        string? replyAddress,
        Guid branchId,
        Guid? departmentId,
        IEnumerable<string> tags,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        if (customerId == Guid.Empty || branchId == Guid.Empty)
        {
            throw new DomainException(InvalidCode, "A ticket needs a customer and a branch.");
        }

        var ticket = new Ticket(Guid.CreateVersion7(), number)
        {
            CustomerId = customerId,
            CategoryId = categoryId,
            Priority = priority,
            Status = TicketStatus.New,
            Channel = channel,
            ReplyAddress = replyAddress,
            BranchId = branchId,
            DepartmentId = departmentId,
            CreatedAt = now,
        };
        ticket.Edit(subject, description, tags);

        if (channel is not TicketChannel.Agent)
        {
            ticket.LastCustomerMessageAt = now;
        }

        ticket.Raise(new TicketCreatedDomainEvent(ticket.Id, ticket.CustomerId, channel));
        return ticket;
    }

    public void Edit(string subject, string description, IEnumerable<string> tags)
    {
        EnsureNotClosed();
        var trimmedSubject = subject?.Trim();
        if (string.IsNullOrEmpty(trimmedSubject) || trimmedSubject.Length > SubjectMaxLength)
        {
            throw new DomainException(InvalidCode, "The subject is empty or too long.");
        }

        var trimmedDescription = description?.Trim() ?? string.Empty;
        if (trimmedDescription.Length > DescriptionMaxLength)
        {
            throw new DomainException(InvalidCode, "The description is too long.");
        }

        var cleanTags = tags.Select(t => t.Trim()).Where(t => t.Length is > 0 and <= 50).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxTags).ToList();

        Subject = trimmedSubject;
        Description = trimmedDescription;
        Tags = cleanTags;
    }

    public void AssignTo(UserId? agentId)
    {
        EnsureNotClosed();
        if (AssignedAgentId == agentId)
        {
            return;
        }

        var previous = AssignedAgentId;
        AssignedAgentId = agentId;
        if (agentId is not null && Status == TicketStatus.New)
        {
            ChangeStatusInternal(TicketStatus.Open, null);
        }

        Raise(new TicketAssignedDomainEvent(Id, previous, agentId));
    }

    public void ChangePriority(TicketPriority priority)
    {
        EnsureNotClosed();
        if (Priority == priority)
        {
            return;
        }

        var previous = Priority;
        Priority = priority;
        Raise(new TicketPriorityChangedDomainEvent(Id, previous, priority));
    }

    public void Categorize(Guid? categoryId)
    {
        EnsureNotClosed();
        if (CategoryId == categoryId)
        {
            return;
        }

        var previous = CategoryId;
        CategoryId = categoryId;
        Raise(new TicketCategoryChangedDomainEvent(Id, previous, categoryId));
    }

    public void TransferTo(Guid branchId, Guid? departmentId)
    {
        EnsureNotClosed();
        if (branchId == Guid.Empty)
        {
            throw new DomainException(InvalidCode, "A ticket must belong to a branch.");
        }

        if (BranchId == branchId && DepartmentId == departmentId)
        {
            return;
        }

        var previousDepartment = DepartmentId;
        BranchId = branchId;
        DepartmentId = departmentId;
        Raise(new TicketTransferredDomainEvent(Id, previousDepartment, departmentId));
    }

    /// <summary>Explicit status change. Resolved → Closed is allowed; reopening uses <see cref="Reopen"/>.</summary>
    public void ChangeStatus(TicketStatus target, DateTimeOffset now)
    {
        if (target == Status)
        {
            return;
        }

        if (target == TicketStatus.Escalated)
        {
            throw new DomainException(InvalidTransitionCode, "Use escalation to escalate a ticket.");
        }

        if (!Transitions[Status].Contains(target))
        {
            throw new DomainException(InvalidTransitionCode, $"A ticket cannot move from {Status} to {target}.");
        }

        ChangeStatusInternal(target, now);
    }

    public void Reopen(DateTimeOffset now)
    {
        if (Status is not (TicketStatus.Resolved or TicketStatus.Closed))
        {
            throw new DomainException(InvalidTransitionCode, "Only resolved or closed tickets can be reopened.");
        }

        ResolvedAt = null;
        ClosedAt = null;
        ChangeStatusInternal(TicketStatus.Open, now);
    }

    public void Escalate(string reason, DateTimeOffset now, bool automatic)
    {
        EnsureNotClosed();
        if (Status == TicketStatus.Resolved)
        {
            throw new DomainException(InvalidTransitionCode, "Resolved tickets cannot be escalated; reopen first.");
        }

        EscalationLevel++;
        EscalatedAt = now;
        if (Status != TicketStatus.Escalated)
        {
            ChangeStatusInternal(TicketStatus.Escalated, now);
        }

        Raise(new TicketEscalatedDomainEvent(Id, EscalationLevel, reason, automatic));
    }

    /// <summary>
    /// Records a message and applies its effects: first-response time, customer replies reopening
    /// pending/resolved tickets. The message row itself is persisted by the caller.
    /// </summary>
    public void RecordMessage(Guid messageId, MessageAuthorType author, bool isInternal, DateTimeOffset now)
    {
        if (Status == TicketStatus.Closed && author == MessageAuthorType.Customer)
        {
            throw new DomainException(ClosedCode, "This ticket is closed. Please open a new request.");
        }

        if (author == MessageAuthorType.Agent && !isInternal)
        {
            LastAgentMessageAt = now;
            FirstRespondedAt ??= now;
            if (Status == TicketStatus.New)
            {
                ChangeStatusInternal(TicketStatus.Open, now);
            }
        }
        else if (author == MessageAuthorType.Customer)
        {
            LastCustomerMessageAt = now;
            if (Status is TicketStatus.PendingCustomer or TicketStatus.Resolved)
            {
                if (Status == TicketStatus.Resolved)
                {
                    ResolvedAt = null;
                }

                ChangeStatusInternal(TicketStatus.Open, now);
            }
        }

        Raise(new TicketMessageAddedDomainEvent(Id, messageId, author, isInternal));
    }

    public void ApplySla(Guid? policyId, DateTimeOffset? firstResponseDueAt, DateTimeOffset? resolutionDueAt)
    {
        SlaPolicyId = policyId;
        FirstResponseDueAt = FirstRespondedAt is null ? firstResponseDueAt : FirstResponseDueAt;
        ResolutionDueAt = resolutionDueAt;
        FirstResponseWarnedAt = null;
        ResolutionWarnedAt = null;
    }

    /// <summary>Called by the SLA monitor. Idempotent: each warning/breach is raised once.</summary>
    public void EvaluateSla(DateTimeOffset now, double warningThreshold)
    {
        if (!IsActive)
        {
            return;
        }

        if (FirstRespondedAt is null && FirstResponseDueAt is { } firstDue)
        {
            Check(SlaTarget.FirstResponse, CreatedAt, firstDue, now, warningThreshold);
        }

        if (ResolutionDueAt is { } resolutionDue)
        {
            Check(SlaTarget.Resolution, CreatedAt, resolutionDue, now, warningThreshold);
        }
    }

    public void SubmitFeedback(int rating, string? comment, DateTimeOffset now)
    {
        if (Status is not (TicketStatus.Resolved or TicketStatus.Closed))
        {
            throw new DomainException(FeedbackNotAllowedCode, "Feedback can be given once the request is resolved.");
        }

        if (rating is < 1 or > 5)
        {
            throw new DomainException(InvalidCode, "The rating must be between 1 and 5.");
        }

        SatisfactionRating = rating;
        SatisfactionComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim()[..Math.Min(comment.Trim().Length, 2000)];
        SatisfactionSubmittedAt = now;
        Raise(new TicketFeedbackSubmittedDomainEvent(Id, rating));
    }

    private void Check(SlaTarget target, DateTimeOffset start, DateTimeOffset due, DateTimeOffset now, double warningThreshold)
    {
        var breached = target == SlaTarget.FirstResponse ? FirstResponseBreached : ResolutionBreached;
        if (breached)
        {
            return;
        }

        if (now >= due)
        {
            if (target == SlaTarget.FirstResponse)
            {
                FirstResponseBreached = true;
            }
            else
            {
                ResolutionBreached = true;
            }

            Raise(new TicketSlaBreachedDomainEvent(Id, target));
            return;
        }

        var warned = target == SlaTarget.FirstResponse ? FirstResponseWarnedAt : ResolutionWarnedAt;
        var elapsed = (now - start).TotalSeconds / Math.Max(1, (due - start).TotalSeconds);
        if (warned is null && elapsed >= warningThreshold)
        {
            if (target == SlaTarget.FirstResponse)
            {
                FirstResponseWarnedAt = now;
            }
            else
            {
                ResolutionWarnedAt = now;
            }

            Raise(new TicketSlaWarningDomainEvent(Id, target, due));
        }
    }

    private void ChangeStatusInternal(TicketStatus target, DateTimeOffset? now)
    {
        var previous = Status;
        Status = target;

        if (target == TicketStatus.Resolved && now is not null)
        {
            ResolvedAt = now;
        }

        if (target == TicketStatus.Closed && now is not null)
        {
            ClosedAt = now;
            ResolvedAt ??= now;
        }

        Raise(new TicketStatusChangedDomainEvent(Id, previous, target));
    }

    private void EnsureNotClosed()
    {
        if (Status == TicketStatus.Closed)
        {
            throw new DomainException(ClosedCode, "Closed tickets cannot be changed. Reopen it first.");
        }
    }
}
