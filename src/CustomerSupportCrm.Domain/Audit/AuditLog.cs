using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Users;

namespace CustomerSupportCrm.Domain.Audit;

/// <summary>
/// An append-only record of a security or business action. Values must never contain
/// passwords, password hashes, tokens or other secrets.
/// </summary>
public sealed class AuditLog : Entity<Guid>
{
    public const int ActionMaxLength = 100;
    public const int EntityTypeMaxLength = 100;
    public const int EntityIdMaxLength = 100;
    public const int CorrelationIdMaxLength = 64;
    public const int IpAddressMaxLength = 64;
    public const int UserAgentMaxLength = 512;

    private AuditLog()
    {
        Action = string.Empty;
        EntityType = string.Empty;
    }

    private AuditLog(Guid id)
        : base(id)
    {
        Action = string.Empty;
        EntityType = string.Empty;
    }

    public DateTimeOffset OccurredAt { get; private set; }

    public UserId? ActorUserId { get; private set; }

    public string Action { get; private set; }

    public string EntityType { get; private set; }

    public string? EntityId { get; private set; }

    public string? CorrelationId { get; private set; }

    /// <summary>JSON snapshot before the change.</summary>
    public string? OldValues { get; private set; }

    /// <summary>JSON snapshot after the change.</summary>
    public string? NewValues { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public static AuditLog Record(
        DateTimeOffset occurredAt,
        UserId? actorUserId,
        string action,
        string entityType,
        string? entityId,
        string? oldValues,
        string? newValues,
        string? correlationId,
        string? ipAddress,
        string? userAgent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);

        return new AuditLog(Guid.CreateVersion7())
        {
            OccurredAt = occurredAt,
            ActorUserId = actorUserId,
            Action = Truncate(action, ActionMaxLength)!,
            EntityType = Truncate(entityType, EntityTypeMaxLength)!,
            EntityId = Truncate(entityId, EntityIdMaxLength),
            OldValues = oldValues,
            NewValues = newValues,
            CorrelationId = Truncate(correlationId, CorrelationIdMaxLength),
            IpAddress = Truncate(ipAddress, IpAddressMaxLength),
            UserAgent = Truncate(userAgent, UserAgentMaxLength),
        };
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is { Length: > 0 } ? value[..Math.Min(value.Length, maxLength)] : null;
}
