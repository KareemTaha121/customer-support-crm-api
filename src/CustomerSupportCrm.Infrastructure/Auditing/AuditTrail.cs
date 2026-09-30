using System.Text.Json;
using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Domain.Audit;
using CustomerSupportCrm.Domain.Users;
using CustomerSupportCrm.Infrastructure.Persistence;

namespace CustomerSupportCrm.Infrastructure.Auditing;

internal sealed class AuditTrail(
    ApplicationDbContext db,
    ICurrentUser currentUser,
    IRequestContext request,
    TimeProvider time)
    : IAuditTrail
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Record(
        string action,
        string entityType,
        string? entityId,
        object? oldValues = null,
        object? newValues = null,
        UserId? actorUserId = null)
    {
        var actor = actorUserId ?? (currentUser.IsAuthenticated ? currentUser.UserId : null);

        db.AuditLogs.Add(AuditLog.Record(
            time.GetUtcNow(),
            actor,
            action,
            entityType,
            entityId,
            Serialize(oldValues),
            Serialize(newValues),
            request.CorrelationId,
            request.IpAddress,
            request.UserAgent));
    }

    private static string? Serialize(object? values) =>
        values is null ? null : JsonSerializer.Serialize(values, JsonOptions);
}
