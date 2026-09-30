using CustomerSupportCrm.Application.Abstractions.Messaging;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Features.Notifications;
using CustomerSupportCrm.Application.Features.Tickets.Common;
using CustomerSupportCrm.Domain.Notifications;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Sla;
using CustomerSupportCrm.Domain.Tickets;
using CustomerSupportCrm.Domain.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Sla;

/// <summary>Chooses the SLA policy for a ticket and computes its due dates.</summary>
public sealed class SlaCalculator(IApplicationDbContext db)
{
    public async Task ApplyAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var policies = await db.SlaPolicies.AsNoTracking().Include(p => p.Targets).Where(p => p.IsActive).ToListAsync(cancellationToken);
        var policy = policies
            .Where(p => (p.CategoryId is null || p.CategoryId == ticket.CategoryId) && (p.DepartmentId is null || p.DepartmentId == ticket.DepartmentId))
            .Where(p => p.CategoryId is not null || p.DepartmentId is not null || p.IsDefault)
            .OrderByDescending(p => (p.CategoryId is null ? 0 : 2) + (p.DepartmentId is null ? 0 : 1))
            .FirstOrDefault();

        var target = policy?.TargetFor(ticket.Priority);
        if (policy is null || target is null)
        {
            ticket.ApplySla(null, null, null);
            return;
        }

        var timeZone = await OrganizationTimeZoneAsync(cancellationToken);
        var start = ticket.CreatedAt == default ? DateTimeOffset.UtcNow : ticket.CreatedAt;
        ticket.ApplySla(
            policy.Id,
            policy.AddWorkingMinutes(start, target.FirstResponseMinutes, timeZone),
            policy.AddWorkingMinutes(start, target.ResolutionMinutes, timeZone));
    }

    private async Task<TimeZoneInfo> OrganizationTimeZoneAsync(CancellationToken cancellationToken)
    {
        var id = await db.Organizations.AsNoTracking().Select(o => o.TimeZone).FirstOrDefaultAsync(cancellationToken);
        return id is not null && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;
    }
}

/// <summary>Runs assignment rules on a new ticket: routing first, then agent selection.</summary>
public sealed class AssignmentEngine(IApplicationDbContext db)
{
    public async Task ApplyAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var rules = await db.AssignmentRules.Where(r => r.IsActive).OrderBy(r => r.Order).ThenBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        var rule = rules.FirstOrDefault(r => r.Matches(ticket));
        if (rule is null)
        {
            return;
        }

        if (rule.SetDepartmentId is { } departmentId && departmentId != ticket.DepartmentId)
        {
            var branchId = await db.Departments.Where(d => d.Id == departmentId && d.IsActive).Select(d => (Guid?)d.BranchId).FirstOrDefaultAsync(cancellationToken);
            if (branchId is { } branch)
            {
                ticket.TransferTo(branch, departmentId);
            }
        }

        if (rule.SetPriority is { } priority)
        {
            ticket.ChangePriority(priority);
        }

        if (ticket.AssignedAgentId is not null || rule.Strategy == AssignmentStrategy.None)
        {
            return;
        }

        UserId? agent = rule.Strategy switch
        {
            AssignmentStrategy.SpecificAgent when rule.AgentId is { } specific
                && await TicketQueries.CanHandleAsync(db, specific, ticket.BranchId, ticket.DepartmentId, cancellationToken) => specific,
            AssignmentStrategy.RoundRobin => rule.PickRoundRobin(await CandidatesAsync(ticket, cancellationToken)),
            AssignmentStrategy.LeastLoaded => await LeastLoadedAsync(ticket, cancellationToken),
            _ => null,
        };

        if (agent is not null)
        {
            ticket.AssignTo(agent);
        }
    }

    private async Task<IReadOnlyList<UserId>> CandidatesAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        var candidates = await TicketQueries.EligibleAgents(db, ticket.BranchId, ticket.DepartmentId, Permissions.TicketsUpdate)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
        return [.. candidates.OrderBy(id => id.Value)];
    }

    private async Task<UserId?> LeastLoadedAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        var candidates = await CandidatesAsync(ticket, cancellationToken);
        if (candidates.Count == 0)
        {
            return null;
        }

        var loads = await db.Tickets.AsNoTracking()
            .Where(t => t.AssignedAgentId != null && t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
            .GroupBy(t => t.AssignedAgentId)
            .Select(g => new { Agent = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return candidates.OrderBy(c => loads.FirstOrDefault(l => l.Agent == c)?.Count ?? 0).ThenBy(c => c.Value).First();
    }
}

/// <summary>Applies an escalation rule's actions to a ticket.</summary>
public sealed class EscalationExecutor(IApplicationDbContext db, NotificationSender notifications, TimeProvider time)
{
    public async Task ExecuteAsync(EscalationRule rule, Ticket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(ticket);
        if (!ticket.IsActive)
        {
            return;
        }

        if (rule.RaisePriorityTo is { } priority && priority > ticket.Priority)
        {
            ticket.ChangePriority(priority);
        }

        if (rule.ReassignToAgentId is { } agent && await TicketQueries.CanHandleAsync(db, agent, ticket.BranchId, ticket.DepartmentId, cancellationToken))
        {
            ticket.AssignTo(agent);
        }

        if (rule.EscalateTicket && ticket.Status != TicketStatus.Resolved)
        {
            ticket.Escalate($"Automatic: {rule.Name}", time.GetUtcNow(), automatic: true);
        }

        var recipients = new List<UserId>(rule.NotifyUserIds.Select(id => new UserId(id)));
        if (rule.NotifyAssignee && ticket.AssignedAgentId is { } assignee)
        {
            recipients.Add(assignee);
        }

        if (rule.NotifyManagers)
        {
            recipients.AddRange(await TicketQueries.EligibleAgents(db, ticket.BranchId, ticket.DepartmentId, Permissions.TicketsAssign).Select(u => u.Id).ToListAsync(cancellationToken));
        }

        notifications.NotifyMany(recipients, NotificationTypes.TicketEscalated, $"{ticket.Number}: {rule.Name}", TicketQueries.TicketLink(ticket.Id), new { ticketNumber = ticket.Number, rule = rule.Name });
    }
}

// ---------- Event handlers ----------

internal sealed class TicketRoutingHandler(IApplicationDbContext db, AssignmentEngine assignment, SlaCalculator sla)
    : INotificationHandler<DomainEventNotification<TicketCreatedDomainEvent>>
{
    public async Task Handle(DomainEventNotification<TicketCreatedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var ticket = await TicketQueries.FindTrackedAsync(db, notification.DomainEvent.TicketId, cancellationToken);
        if (ticket is null)
        {
            return;
        }

        await assignment.ApplyAsync(ticket, cancellationToken);
        await sla.ApplyAsync(ticket, cancellationToken);
    }
}

/// <summary>Recomputes due dates when the inputs of policy selection change.</summary>
internal sealed class SlaRecalculationHandler(IApplicationDbContext db, SlaCalculator sla)
    : INotificationHandler<DomainEventNotification<TicketPriorityChangedDomainEvent>>,
      INotificationHandler<DomainEventNotification<TicketCategoryChangedDomainEvent>>,
      INotificationHandler<DomainEventNotification<TicketTransferredDomainEvent>>
{
    public Task Handle(DomainEventNotification<TicketPriorityChangedDomainEvent> notification, CancellationToken cancellationToken) =>
        RecalculateAsync(notification.DomainEvent.TicketId, cancellationToken);

    public Task Handle(DomainEventNotification<TicketCategoryChangedDomainEvent> notification, CancellationToken cancellationToken) =>
        RecalculateAsync(notification.DomainEvent.TicketId, cancellationToken);

    public Task Handle(DomainEventNotification<TicketTransferredDomainEvent> notification, CancellationToken cancellationToken) =>
        RecalculateAsync(notification.DomainEvent.TicketId, cancellationToken);

    private async Task RecalculateAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        var ticket = await TicketQueries.FindTrackedAsync(db, ticketId, cancellationToken);
        if (ticket is { IsActive: true } && db.Tickets.Entry(ticket).State != EntityState.Added)
        {
            await sla.ApplyAsync(ticket, cancellationToken);
        }
    }
}

internal sealed class SlaEscalationHandler(IApplicationDbContext db, EscalationExecutor executor)
    : INotificationHandler<DomainEventNotification<TicketSlaWarningDomainEvent>>,
      INotificationHandler<DomainEventNotification<TicketSlaBreachedDomainEvent>>
{
    public Task Handle(DomainEventNotification<TicketSlaWarningDomainEvent> notification, CancellationToken cancellationToken) =>
        RunAsync(notification.DomainEvent.TicketId, EscalationTrigger.SlaWarning, notification.DomainEvent.Target, cancellationToken);

    public Task Handle(DomainEventNotification<TicketSlaBreachedDomainEvent> notification, CancellationToken cancellationToken) =>
        RunAsync(notification.DomainEvent.TicketId, EscalationTrigger.SlaBreached, notification.DomainEvent.Target, cancellationToken);

    private async Task RunAsync(Guid ticketId, EscalationTrigger trigger, SlaTarget target, CancellationToken cancellationToken)
    {
        var ticket = await TicketQueries.FindTrackedAsync(db, ticketId, cancellationToken);
        if (ticket is null)
        {
            return;
        }

        var rules = await db.EscalationRules.AsNoTracking().Where(r => r.IsActive && r.Trigger == trigger).ToListAsync(cancellationToken);
        foreach (var rule in rules.Where(r => r.Matches(ticket, trigger, target)))
        {
            await executor.ExecuteAsync(rule, ticket, cancellationToken);
        }
    }
}

// ---------- Background evaluation ----------

/// <summary>
/// Every minute: raises SLA warnings/breaches and fires time-based escalation rules. Idempotent
/// through the ticket's SLA flags and <see cref="EscalationRun"/> rows.
/// </summary>
public sealed record EvaluateSlaCommand : IRequest;

internal sealed class EvaluateSlaHandler(IApplicationDbContext db, EscalationExecutor executor, TimeProvider time) : IRequestHandler<EvaluateSlaCommand>
{
    public const double WarningThreshold = 0.8;
    private const int BatchSize = 500;

    public async Task Handle(EvaluateSlaCommand request, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        var tickets = await db.Tickets
            .Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
            .Where(t => (t.FirstRespondedAt == null && t.FirstResponseDueAt != null && !t.FirstResponseBreached)
                || (t.ResolutionDueAt != null && !t.ResolutionBreached))
            .OrderBy(t => t.ResolutionDueAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var ticket in tickets)
        {
            ticket.EvaluateSla(now, WarningThreshold);
        }

        var timeRules = await db.EscalationRules.AsNoTracking()
            .Where(r => r.IsActive && (r.Trigger == EscalationTrigger.Unassigned || r.Trigger == EscalationTrigger.NoAgentReply))
            .ToListAsync(cancellationToken);

        foreach (var rule in timeRules)
        {
            var cutoff = now.AddMinutes(-rule.AfterMinutes!.Value);
            var candidates = db.Tickets.Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed
                && !db.EscalationRuns.Any(run => run.RuleId == rule.Id && run.TicketId == t.Id));

            candidates = rule.Trigger == EscalationTrigger.Unassigned
                ? candidates.Where(t => t.AssignedAgentId == null && t.CreatedAt <= cutoff)
                : candidates.Where(t => t.LastCustomerMessageAt != null && t.LastCustomerMessageAt <= cutoff
                    && (t.LastAgentMessageAt == null || t.LastAgentMessageAt < t.LastCustomerMessageAt));

            foreach (var ticket in await candidates.Take(BatchSize).ToListAsync(cancellationToken))
            {
                if (!rule.Matches(ticket, rule.Trigger, null))
                {
                    continue;
                }

                await executor.ExecuteAsync(rule, ticket, cancellationToken);
                db.EscalationRuns.Add(EscalationRun.Record(rule.Id, ticket.Id, now));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
