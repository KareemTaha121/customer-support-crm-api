namespace CustomerSupportCrm.Domain.Common;

/// <summary>
/// Deleting marks the row instead of removing it; normal queries never see deleted rows.
/// Set by persistence, never by domain code.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }

    DateTimeOffset? DeletedAt { get; }

    Guid? DeletedBy { get; }
}
