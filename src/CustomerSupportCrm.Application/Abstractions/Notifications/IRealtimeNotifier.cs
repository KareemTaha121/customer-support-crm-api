using CustomerSupportCrm.Domain.Users;

namespace CustomerSupportCrm.Application.Abstractions.Notifications;

/// <summary>Pushes events to connected clients (SignalR). Best effort; never the source of truth.</summary>
public interface IRealtimeNotifier
{
    Task SendToUserAsync(UserId userId, string eventName, object payload, CancellationToken cancellationToken);

    Task SendToGroupAsync(string group, string eventName, object payload, CancellationToken cancellationToken);
}

public static class RealtimeEvents
{
    public const string NotificationCreated = "notificationCreated";
    public const string ChatMessage = "chatMessage";
    public const string ChatUpdated = "chatUpdated";
    public const string TicketUpdated = "ticketUpdated";
}

public static class RealtimeGroups
{
    /// <summary>Agents who handle live chat.</summary>
    public const string ChatAgents = "chat-agents";

    public static string ChatConversation(Guid conversationId) => $"chat:{conversationId:N}";
}
