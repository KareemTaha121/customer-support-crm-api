using System.Text.Json;

namespace CustomerSupportCrm.Contracts.Audit;

public sealed record AuditLogResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    string? ActorDisplayName,
    string Action,
    string EntityType,
    string? EntityId,
    JsonElement? OldValues,
    JsonElement? NewValues,
    string? CorrelationId,
    string? IpAddress,
    string? UserAgent);
