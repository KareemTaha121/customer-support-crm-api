using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Messaging;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Features.Customers.Common;
using CustomerSupportCrm.Application.Features.Notifications;
using CustomerSupportCrm.Application.Features.Tickets.Common;
using CustomerSupportCrm.Domain.Customers;
using CustomerSupportCrm.Domain.Notifications;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Tickets;
using CustomerSupportCrm.Domain.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Tickets;

/// <summary>Writes history rows with the acting staff user or customer.</summary>
public sealed class TicketHistoryRecorder(IApplicationDbContext db, ICurrentUser currentUser, ICurrentCustomer currentCustomer, TimeProvider time)
{
    public void Record(Guid ticketId, string action, string? oldValue, string? newValue) =>
        db.TicketHistory.Add(TicketHistory.Record(
            ticketId,
            action,
            Truncate(oldValue),
            Truncate(newValue),
            currentUser.IsAuthenticated ? currentUser.UserId : null,
            currentCustomer.IsAuthenticated ? currentCustomer.CustomerId : null,
            time.GetUtcNow()));

    public UserId? ActingUser => currentUser.IsAuthenticated ? currentUser.UserId : null;

    private static string? Truncate(string? value) => value is { Length: > 500 } ? value[..500] : value;
}

internal sealed class TicketCreatedHandler(IApplicationDbContext db, TicketHistoryRecorder history, CustomerTimeline timeline)
    : INotificationHandler<DomainEventNotification<TicketCreatedDomainEvent>>
{
    public async Task Handle(DomainEventNotification<TicketCreatedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        var ticket = await TicketQueries.FindTrackedAsync(db, e.TicketId, cancellationToken);
        history.Record(e.TicketId, TicketHistoryActions.Created, null, e.Channel.ToString());
        timeline.Record(e.CustomerId, CustomerActivityTypes.TicketCreated, $"{ticket?.Number} {ticket?.Subject}", e.TicketId, new { number = ticket?.Number, channel = e.Channel.ToString() });
    }
}

internal sealed class TicketAssignedHandler(IApplicationDbContext db, TicketHistoryRecorder history, NotificationSender notifications)
    : INotificationHandler<DomainEventNotification<TicketAssignedDomainEvent>>
{
    public async Task Handle(DomainEventNotification<TicketAssignedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        var ids = new[] { e.PreviousAgentId, e.AgentId }.Where(id => id is not null).Select(id => id!.Value).ToList();
        var names = await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        history.Record(
            e.TicketId,
            TicketHistoryActions.Assigned,
            e.PreviousAgentId is { } previous ? names.GetValueOrDefault(previous) : null,
            e.AgentId is { } current ? names.GetValueOrDefault(current) : null);

        if (e.AgentId is { } agent && agent != history.ActingUser)
        {
            var ticket = await TicketQueries.FindTrackedAsync(db, e.TicketId, cancellationToken);
            notifications.Notify(agent, NotificationTypes.TicketAssigned, $"Ticket {ticket?.Number} was assigned to you", TicketQueries.TicketLink(e.TicketId), new { ticketNumber = ticket?.Number, subject = ticket?.Subject });
        }
    }
}

internal sealed class TicketStatusChangedHandler(IApplicationDbContext db, TicketHistoryRecorder history, CustomerTimeline timeline)
    : INotificationHandler<DomainEventNotification<TicketStatusChangedDomainEvent>>
{
    public async Task Handle(DomainEventNotification<TicketStatusChangedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        history.Record(e.TicketId, TicketHistoryActions.StatusChanged, e.PreviousStatus.ToString(), e.Status.ToString());

        if (e.Status is TicketStatus.Resolved or TicketStatus.Closed || !e.PreviousStatus.IsActive())
        {
            var ticket = await TicketQueries.FindTrackedAsync(db, e.TicketId, cancellationToken);
            if (ticket is not null)
            {
                timeline.Record(ticket.CustomerId, CustomerActivityTypes.TicketStatusChanged, $"{ticket.Number}: {e.PreviousStatus} → {e.Status}", ticket.Id, new { number = ticket.Number, from = e.PreviousStatus.ToString(), to = e.Status.ToString() });
            }
        }
    }
}

internal sealed class TicketFieldChangeHandlers(IApplicationDbContext db, TicketHistoryRecorder history)
    : INotificationHandler<DomainEventNotification<TicketPriorityChangedDomainEvent>>,
      INotificationHandler<DomainEventNotification<TicketCategoryChangedDomainEvent>>,
      INotificationHandler<DomainEventNotification<TicketTransferredDomainEvent>>
{
    public Task Handle(DomainEventNotification<TicketPriorityChangedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        history.Record(e.TicketId, TicketHistoryActions.PriorityChanged, e.PreviousPriority.ToString(), e.Priority.ToString());
        return Task.CompletedTask;
    }

    public async Task Handle(DomainEventNotification<TicketCategoryChangedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        var ids = new[] { e.PreviousCategoryId, e.CategoryId }.Where(id => id is not null).Select(id => id!.Value).ToList();
        var names = await db.TicketCategories.Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        history.Record(e.TicketId, TicketHistoryActions.CategoryChanged, e.PreviousCategoryId is { } p ? names.GetValueOrDefault(p) : null, e.CategoryId is { } c2 ? names.GetValueOrDefault(c2) : null);
    }

    public async Task Handle(DomainEventNotification<TicketTransferredDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        var ids = new[] { e.PreviousDepartmentId, e.DepartmentId }.Where(id => id is not null).Select(id => id!.Value).ToList();
        var names = await db.Departments.Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);
        history.Record(e.TicketId, TicketHistoryActions.Transferred, e.PreviousDepartmentId is { } p ? names.GetValueOrDefault(p) : null, e.DepartmentId is { } d2 ? names.GetValueOrDefault(d2) : null);
    }
}

internal sealed class TicketEscalatedHandler(IApplicationDbContext db, TicketHistoryRecorder history, NotificationSender notifications)
    : INotificationHandler<DomainEventNotification<TicketEscalatedDomainEvent>>
{
    public async Task Handle(DomainEventNotification<TicketEscalatedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        history.Record(e.TicketId, TicketHistoryActions.Escalated, null, $"Level {e.Level}: {e.Reason}");

        var ticket = await TicketQueries.FindTrackedAsync(db, e.TicketId, cancellationToken);
        if (ticket is null)
        {
            return;
        }

        var managers = await TicketQueries.EligibleAgents(db, ticket.BranchId, ticket.DepartmentId, Permissions.TicketsAssign).Select(u => u.Id).ToListAsync(cancellationToken);
        if (ticket.AssignedAgentId is { } assignee)
        {
            managers.Add(assignee);
        }

        notifications.NotifyMany(managers, NotificationTypes.TicketEscalated, $"Ticket {ticket.Number} escalated (level {e.Level})", TicketQueries.TicketLink(ticket.Id), new { ticketNumber = ticket.Number, level = e.Level, reason = e.Reason }, except: history.ActingUser);
    }
}

internal sealed class TicketMessageAddedHandler(IApplicationDbContext db, NotificationSender notifications, CustomerTimeline timeline, ICurrentUser currentUser)
    : INotificationHandler<DomainEventNotification<TicketMessageAddedDomainEvent>>
{
    public async Task Handle(DomainEventNotification<TicketMessageAddedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        var ticket = await TicketQueries.FindTrackedAsync(db, e.TicketId, cancellationToken);
        var message = db.TicketMessages.Local.FirstOrDefault(m => m.Id == e.MessageId);
        if (ticket is null || message is null)
        {
            return;
        }

        var link = TicketQueries.TicketLink(ticket.Id);
        var data = new { ticketNumber = ticket.Number, subject = ticket.Subject };

        if (e.AuthorType == MessageAuthorType.Customer && ticket.AssignedAgentId is { } assignee)
        {
            notifications.Notify(assignee, NotificationTypes.TicketReplied, $"Customer replied on {ticket.Number}", link, data);
        }

        if (message.MentionedUserIds.Count > 0)
        {
            var me = currentUser.IsAuthenticated ? currentUser.UserId : (UserId?)null;
            notifications.NotifyMany(message.MentionedUserIds.Select(id => new UserId(id)), NotificationTypes.TicketMentioned, $"You were mentioned on {ticket.Number}", link, data, except: me);
        }

        if (!e.IsInternal)
        {
            var summary = e.AuthorType == MessageAuthorType.Customer ? "Customer message" : "Agent reply";
            timeline.Record(ticket.CustomerId, CustomerActivityTypes.TicketMessage, $"{ticket.Number}: {summary}", ticket.Id, new { number = ticket.Number, author = e.AuthorType.ToString(), channel = message.Channel.ToString() });
        }
    }
}

internal sealed class TicketSlaEventHandlers(IApplicationDbContext db, TicketHistoryRecorder history, NotificationSender notifications)
    : INotificationHandler<DomainEventNotification<TicketSlaWarningDomainEvent>>,
      INotificationHandler<DomainEventNotification<TicketSlaBreachedDomainEvent>>
{
    public async Task Handle(DomainEventNotification<TicketSlaWarningDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        history.Record(e.TicketId, TicketHistoryActions.SlaWarning, null, e.Target.ToString());
        await NotifyAssigneeAsync(e.TicketId, NotificationTypes.SlaWarning, e.Target, cancellationToken);
    }

    public async Task Handle(DomainEventNotification<TicketSlaBreachedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        history.Record(e.TicketId, TicketHistoryActions.SlaBreached, null, e.Target.ToString());
        await NotifyAssigneeAsync(e.TicketId, NotificationTypes.SlaBreached, e.Target, cancellationToken);
    }

    private async Task NotifyAssigneeAsync(Guid ticketId, string type, SlaTarget target, CancellationToken cancellationToken)
    {
        var ticket = await TicketQueries.FindTrackedAsync(db, ticketId, cancellationToken);
        if (ticket?.AssignedAgentId is { } assignee)
        {
            var verb = type == NotificationTypes.SlaBreached ? "breached" : "at risk";
            notifications.Notify(assignee, type, $"{ticket.Number}: {target} SLA {verb}", TicketQueries.TicketLink(ticket.Id), new { ticketNumber = ticket.Number, target = target.ToString() });
        }
    }
}

internal sealed class TicketFeedbackHandler(IApplicationDbContext db, TicketHistoryRecorder history, CustomerTimeline timeline)
    : INotificationHandler<DomainEventNotification<TicketFeedbackSubmittedDomainEvent>>
{
    public async Task Handle(DomainEventNotification<TicketFeedbackSubmittedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        history.Record(e.TicketId, TicketHistoryActions.Feedback, null, e.Rating.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var ticket = await TicketQueries.FindTrackedAsync(db, e.TicketId, cancellationToken);
        if (ticket is not null)
        {
            timeline.Record(ticket.CustomerId, CustomerActivityTypes.TicketFeedback, $"{ticket.Number}: rated {e.Rating}/5", ticket.Id, new { number = ticket.Number, rating = e.Rating });
        }
    }
}
