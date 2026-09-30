namespace CustomerSupportCrm.Domain.Common;

/// <summary>
/// Creation/modification stamps. Set by persistence, never by domain code.
/// </summary>
public interface IAuditableEntity
{
    DateTimeOffset CreatedAt { get; }

    Guid? CreatedBy { get; }

    DateTimeOffset? UpdatedAt { get; }

    Guid? UpdatedBy { get; }
}
