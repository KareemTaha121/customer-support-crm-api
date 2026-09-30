using CustomerSupportCrm.Domain.Sla;
using CustomerSupportCrm.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Abstractions.Persistence;

public partial interface IApplicationDbContext
{
    DbSet<SlaPolicy> SlaPolicies { get; }

    DbSet<AssignmentRule> AssignmentRules { get; }

    DbSet<EscalationRule> EscalationRules { get; }

    DbSet<EscalationRun> EscalationRuns { get; }

    DbSet<AgentTask> AgentTasks { get; }

    DbSet<QuickReply> QuickReplies { get; }
}
