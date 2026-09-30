using CustomerSupportCrm.Domain.Sla;
using CustomerSupportCrm.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Infrastructure.Persistence;

public sealed partial class ApplicationDbContext
{
    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();

    public DbSet<AssignmentRule> AssignmentRules => Set<AssignmentRule>();

    public DbSet<EscalationRule> EscalationRules => Set<EscalationRule>();

    public DbSet<EscalationRun> EscalationRuns => Set<EscalationRun>();

    public DbSet<AgentTask> AgentTasks => Set<AgentTask>();

    public DbSet<QuickReply> QuickReplies => Set<QuickReply>();
}
