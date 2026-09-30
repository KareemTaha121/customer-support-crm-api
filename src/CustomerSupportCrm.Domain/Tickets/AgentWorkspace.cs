using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Users;

namespace CustomerSupportCrm.Domain.Tickets;

/// <summary>A to-do or reminder for an agent, optionally linked to a ticket or customer.</summary>
public sealed class AgentTask : Entity<Guid>, IAuditableEntity
{
    public const int TitleMaxLength = 300;
    public const string InvalidCode = "INVALID_TASK";

    private AgentTask()
    {
        Title = string.Empty;
    }

    private AgentTask(Guid id)
        : base(id)
    {
        Title = string.Empty;
    }

    public string Title { get; private set; }

    public string? Notes { get; private set; }

    public UserId AssigneeId { get; private set; }

    public Guid? TicketId { get; private set; }

    public Guid? CustomerId { get; private set; }

    public DateTimeOffset? DueAt { get; private set; }

    public DateTimeOffset? RemindAt { get; private set; }

    public DateTimeOffset? ReminderSentAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public bool IsCompleted => CompletedAt is not null;

    public static AgentTask Create(UserId assigneeId, string title, string? notes, Guid? ticketId, Guid? customerId, DateTimeOffset? dueAt, DateTimeOffset? remindAt)
    {
        var task = new AgentTask(Guid.CreateVersion7()) { AssigneeId = assigneeId };
        task.Update(title, notes, ticketId, customerId, dueAt, remindAt);
        return task;
    }

    public void Update(string title, string? notes, Guid? ticketId, Guid? customerId, DateTimeOffset? dueAt, DateTimeOffset? remindAt)
    {
        var trimmed = title?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > TitleMaxLength)
        {
            throw new DomainException(InvalidCode, "The task title is not valid.");
        }

        Title = trimmed;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        TicketId = ticketId;
        CustomerId = customerId;
        DueAt = dueAt;
        if (RemindAt != remindAt)
        {
            RemindAt = remindAt;
            ReminderSentAt = null;
        }
    }

    public void Reassign(UserId assigneeId) => AssigneeId = assigneeId;

    public void Complete(DateTimeOffset now) => CompletedAt ??= now;

    public void Reopen() => CompletedAt = null;

    public void MarkReminderSent(DateTimeOffset now) => ReminderSentAt = now;
}

/// <summary>A canned reply. Shared replies are visible to everyone; personal ones to their owner only.</summary>
public sealed class QuickReply : Entity<Guid>, IAuditableEntity
{
    public const int TitleMaxLength = 150;
    public const int ShortcutMaxLength = 30;
    public const int BodyMaxLength = 10_000;
    public const string InvalidCode = "INVALID_QUICK_REPLY";

    private QuickReply()
    {
        Title = string.Empty;
        Body = string.Empty;
        Language = "en";
    }

    private QuickReply(Guid id)
        : base(id)
    {
        Title = string.Empty;
        Body = string.Empty;
        Language = "en";
    }

    public string Title { get; private set; }

    /// <summary>Typed as "/shortcut" in the reply box.</summary>
    public string? Shortcut { get; private set; }

    public string Body { get; private set; }

    public string Language { get; private set; }

    public Guid? CategoryId { get; private set; }

    /// <summary>Null for shared replies.</summary>
    public UserId? OwnerId { get; private set; }

    public int UsageCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static QuickReply Create(UserId? ownerId) => new(Guid.CreateVersion7()) { OwnerId = ownerId };

    public void Update(string title, string? shortcut, string body, string language, Guid? categoryId)
    {
        var trimmedTitle = title?.Trim();
        var trimmedBody = body?.Trim();
        if (string.IsNullOrEmpty(trimmedTitle) || trimmedTitle.Length > TitleMaxLength
            || string.IsNullOrEmpty(trimmedBody) || trimmedBody.Length > BodyMaxLength
            || language is not ("en" or "ar"))
        {
            throw new DomainException(InvalidCode, "The quick reply is not valid.");
        }

        Title = trimmedTitle;
        Shortcut = string.IsNullOrWhiteSpace(shortcut) ? null : shortcut.Trim().TrimStart('/').ToUpperInvariant();
        Body = trimmedBody;
        Language = language;
        CategoryId = categoryId;
    }

    public void RecordUse() => UsageCount++;
}
