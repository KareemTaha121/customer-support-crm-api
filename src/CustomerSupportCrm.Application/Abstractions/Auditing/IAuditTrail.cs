using CustomerSupportCrm.Domain.Users;

namespace CustomerSupportCrm.Application.Abstractions.Auditing;

/// <summary>
/// Adds an audit entry to the current unit of work; it is persisted by the handler's
/// SaveChangesAsync, so the entry commits or rolls back with the change it describes.
/// Values are serialized to JSON and must never contain secrets.
/// </summary>
public interface IAuditTrail
{
    /// <param name="actorUserId">Overrides the current user, e.g. during sign-in.</param>
    void Record(
        string action,
        string entityType,
        string? entityId,
        object? oldValues = null,
        object? newValues = null,
        UserId? actorUserId = null);
}
