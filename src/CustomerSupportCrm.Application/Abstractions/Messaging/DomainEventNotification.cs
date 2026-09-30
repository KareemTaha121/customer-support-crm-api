using CustomerSupportCrm.Domain.Common;
using MediatR;

namespace CustomerSupportCrm.Application.Abstractions.Messaging;

/// <summary>
/// Carries a domain event through MediatR (the domain itself does not reference MediatR).
/// Handlers run before the unit of work commits and must not call SaveChangesAsync.
/// </summary>
public sealed record DomainEventNotification<TEvent>(TEvent DomainEvent) : INotification
    where TEvent : IDomainEvent;

public static class DomainEventNotification
{
    public static INotification Wrap(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var type = typeof(DomainEventNotification<>).MakeGenericType(domainEvent.GetType());
        return (INotification)Activator.CreateInstance(type, domainEvent)!;
    }
}
