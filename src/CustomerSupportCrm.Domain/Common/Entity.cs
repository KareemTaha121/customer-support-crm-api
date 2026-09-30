namespace CustomerSupportCrm.Domain.Common;

public abstract class Entity<TId>
    where TId : notnull
{
    protected Entity(TId id) => Id = id;

    /// <summary>For EF Core materialization.</summary>
    protected Entity() => Id = default!;

    public TId Id { get; private init; }
}
