using System.Text.Json;

namespace CustomerSupportCrm.Contracts.Notifications;

/// <param name="Type">Stable type code (e.g. ticket.assigned); clients localize by type + data.</param>
/// <param name="Data">Type-specific values such as ticketNumber.</param>
public sealed record NotificationResponse(
    Guid Id,
    string Type,
    string Title,
    string? Body,
    string? Link,
    JsonElement? Data,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record UnreadCountResponse(int Count);
