namespace CustomerSupportCrm.Domain.Common;

/// <summary>A business fact raised by an aggregate. Handlers live outside the domain.</summary>
public interface IDomainEvent;

public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DequeueDomainEvents();
}

/// <summary>
/// Consistency boundary that records domain events. Events are dispatched by persistence
/// before the transaction commits, so handlers join the same unit of work.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id)
        : base(id)
    {
    }

    protected AggregateRoot()
    {
    }

    public IReadOnlyList<IDomainEvent> DequeueDomainEvents()
    {
        var events = _domainEvents.ToList();
        _domainEvents.Clear();
        return events;
    }

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
