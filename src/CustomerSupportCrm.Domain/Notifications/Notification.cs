using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Users;

namespace CustomerSupportCrm.Domain.Notifications;

/// <summary>
/// An in-app notification for one staff user. <see cref="Type"/> plus <see cref="Data"/>
/// (JSON) let clients render localized text; <see cref="Title"/> is an English fallback.
/// </summary>
public sealed class Notification : Entity<Guid>
{
    public const int TypeMaxLength = 64;
    public const int TitleMaxLength = 300;
    public const int LinkMaxLength = 500;

    private Notification()
    {
        Type = string.Empty;
        Title = string.Empty;
    }

    private Notification(Guid id)
        : base(id)
    {
        Type = string.Empty;
        Title = string.Empty;
    }

    public UserId RecipientId { get; private set; }

    public string Type { get; private set; }

    public string Title { get; private set; }

    public string? Body { get; private set; }

    public string? Link { get; private set; }

    public string? Data { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public static Notification Create(UserId recipientId, string type, string title, string? body, string? link, string? data, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new Notification(Guid.CreateVersion7())
        {
            RecipientId = recipientId,
            Type = type,
            Title = title.Length > TitleMaxLength ? title[..TitleMaxLength] : title,
            Body = body,
            Link = link,
            Data = data,
            CreatedAt = now,
        };
    }

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}

public static class NotificationTypes
{
    public const string TicketAssigned = "ticket.assigned";
    public const string TicketReplied = "ticket.replied";
    public const string TicketMentioned = "ticket.mentioned";
    public const string TicketEscalated = "ticket.escalated";
    public const string SlaWarning = "sla.warning";
    public const string SlaBreached = "sla.breached";
    public const string TaskReminder = "task.reminder";
    public const string ChatWaiting = "chat.waiting";
}
