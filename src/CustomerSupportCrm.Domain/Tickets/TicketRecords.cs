using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Users;

namespace CustomerSupportCrm.Domain.Tickets;

/// <summary>A reply, customer message or internal note on a ticket.</summary>
public sealed class TicketMessage : Entity<Guid>
{
    public const int BodyMaxLength = 50_000;
    public const string InvalidCode = "INVALID_MESSAGE";

    private TicketMessage()
    {
        Body = string.Empty;
    }

    private TicketMessage(Guid id)
        : base(id)
    {
        Body = string.Empty;
    }

    public Guid TicketId { get; private set; }

    public MessageAuthorType AuthorType { get; private set; }

    public UserId? AuthorUserId { get; private set; }

    public Guid? AuthorCustomerId { get; private set; }

    public string Body { get; private set; }

    /// <summary>Internal notes are visible to staff only (never portal, email or chat).</summary>
    public bool IsInternal { get; private set; }

    public TicketChannel Channel { get; private set; }

    /// <summary>Provider message id (email Message-ID, WhatsApp wamid) for threading and dedupe.</summary>
    public string? ExternalMessageId { get; private set; }

    public List<Guid> MentionedUserIds { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public static TicketMessage Create(
        Guid ticketId,
        MessageAuthorType authorType,
        UserId? authorUserId,
        Guid? authorCustomerId,
        string body,
        bool isInternal,
        TicketChannel channel,
        string? externalMessageId,
        IEnumerable<Guid> mentionedUserIds,
        DateTimeOffset now)
    {
        var trimmed = body?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > BodyMaxLength)
        {
            throw new DomainException(InvalidCode, "The message is empty or too long.");
        }

        if (isInternal && authorType != MessageAuthorType.Agent)
        {
            throw new DomainException(InvalidCode, "Only agents can write internal notes.");
        }

        return new TicketMessage(Guid.CreateVersion7())
        {
            TicketId = ticketId,
            AuthorType = authorType,
            AuthorUserId = authorUserId,
            AuthorCustomerId = authorCustomerId,
            Body = trimmed,
            IsInternal = isInternal,
            Channel = channel,
            ExternalMessageId = externalMessageId,
            MentionedUserIds = [.. mentionedUserIds.Distinct()],
            CreatedAt = now,
        };
    }
}

/// <summary>Append-only change log of a ticket (status, assignment, priority, SLA, escalation...).</summary>
public sealed class TicketHistory : Entity<Guid>
{
    private TicketHistory()
    {
        Action = string.Empty;
    }

    private TicketHistory(Guid id)
        : base(id)
    {
        Action = string.Empty;
    }

    public Guid TicketId { get; private set; }

    public string Action { get; private set; }

    public string? OldValue { get; private set; }

    public string? NewValue { get; private set; }

    public UserId? ActorUserId { get; private set; }

    public Guid? ActorCustomerId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public static TicketHistory Record(Guid ticketId, string action, string? oldValue, string? newValue, UserId? actorUserId, Guid? actorCustomerId, DateTimeOffset now) =>
        new(Guid.CreateVersion7())
        {
            TicketId = ticketId,
            Action = action,
            OldValue = oldValue,
            NewValue = newValue,
            ActorUserId = actorUserId,
            ActorCustomerId = actorCustomerId,
            OccurredAt = now,
        };
}

public static class TicketHistoryActions
{
    public const string Created = "created";
    public const string StatusChanged = "status";
    public const string Assigned = "assignee";
    public const string PriorityChanged = "priority";
    public const string CategoryChanged = "category";
    public const string Transferred = "department";
    public const string Escalated = "escalated";
    public const string SlaWarning = "sla_warning";
    public const string SlaBreached = "sla_breached";
    public const string Feedback = "feedback";
}

/// <summary>A hierarchical ticket category with optional routing defaults.</summary>
public sealed class TicketCategory : Entity<Guid>, IAuditableEntity
{
    public const int NameMaxLength = 150;
    public const string InvalidCode = "INVALID_CATEGORY";

    private TicketCategory()
    {
        Name = string.Empty;
    }

    private TicketCategory(Guid id)
        : base(id)
    {
        Name = string.Empty;
    }

    public string Name { get; private set; }

    public string? NameAr { get; private set; }

    public Guid? ParentId { get; private set; }

    public Guid? DefaultDepartmentId { get; private set; }

    public TicketPriority? DefaultPriority { get; private set; }

    public bool IsActive { get; private set; }

    public int SortOrder { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static TicketCategory Create(string name, string? nameAr, Guid? parentId, Guid? defaultDepartmentId, TicketPriority? defaultPriority, int sortOrder)
    {
        var category = new TicketCategory(Guid.CreateVersion7()) { IsActive = true };
        category.Update(name, nameAr, parentId, defaultDepartmentId, defaultPriority, sortOrder);
        return category;
    }

    public void Update(string name, string? nameAr, Guid? parentId, Guid? defaultDepartmentId, TicketPriority? defaultPriority, int sortOrder)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > NameMaxLength)
        {
            throw new DomainException(InvalidCode, "The category name is not valid.");
        }

        if (parentId == Id)
        {
            throw new DomainException(InvalidCode, "A category cannot be its own parent.");
        }

        Name = trimmed;
        NameAr = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr.Trim();
        ParentId = parentId;
        DefaultDepartmentId = defaultDepartmentId;
        DefaultPriority = defaultPriority;
        SortOrder = sortOrder;
    }

    public void SetActive(bool active) => IsActive = active;
}
