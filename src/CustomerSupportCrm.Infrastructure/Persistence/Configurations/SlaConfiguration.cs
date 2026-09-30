using CustomerSupportCrm.Domain.Customers;
using CustomerSupportCrm.Domain.Organizations;
using CustomerSupportCrm.Domain.Sla;
using CustomerSupportCrm.Domain.Tickets;
using CustomerSupportCrm.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCrm.Infrastructure.Persistence.Configurations;

internal sealed class SlaPolicyConfiguration : IEntityTypeConfiguration<SlaPolicy>
{
    public void Configure(EntityTypeBuilder<SlaPolicy> builder)
    {
        builder.ToTable("sla_policies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(SlaPolicy.NameMaxLength);
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.WorkDays).HasColumnType("integer[]");
        builder.HasOne<TicketCategory>().WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Department>().WithMany().HasForeignKey(p => p.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(p => p.Targets).WithOne().HasForeignKey(t => t.PolicyId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Targets).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasXminConcurrencyToken();
    }
}

internal sealed class SlaTargetTimeConfiguration : IEntityTypeConfiguration<SlaTargetTime>
{
    public void Configure(EntityTypeBuilder<SlaTargetTime> builder)
    {
        builder.ToTable("sla_targets");
        builder.HasKey(t => new { t.PolicyId, t.Priority });
        builder.Property(t => t.Priority).HasConversion<string>().HasMaxLength(20);
    }
}

internal sealed class AssignmentRuleConfiguration : IEntityTypeConfiguration<AssignmentRule>
{
    public void Configure(EntityTypeBuilder<AssignmentRule> builder)
    {
        builder.ToTable("assignment_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Name).HasMaxLength(AssignmentRule.NameMaxLength);
        builder.Property(r => r.MatchChannel).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.MatchPriority).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.SetPriority).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Strategy).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.MatchKeyword).HasMaxLength(200);
        builder.HasIndex(r => r.Order);
        builder.HasXminConcurrencyToken();
    }
}

internal sealed class EscalationRuleConfiguration : IEntityTypeConfiguration<EscalationRule>
{
    public void Configure(EntityTypeBuilder<EscalationRule> builder)
    {
        builder.ToTable("escalation_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Name).HasMaxLength(EscalationRule.NameMaxLength);
        builder.Property(r => r.Trigger).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Target).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.MatchPriority).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.RaisePriorityTo).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.NotifyUserIds).HasColumnType("uuid[]");
        builder.HasXminConcurrencyToken();
    }
}

internal sealed class EscalationRunConfiguration : IEntityTypeConfiguration<EscalationRun>
{
    public void Configure(EntityTypeBuilder<EscalationRun> builder)
    {
        builder.ToTable("escalation_runs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.HasIndex(r => new { r.RuleId, r.TicketId }).IsUnique();
        builder.HasOne<EscalationRule>().WithMany().HasForeignKey(r => r.RuleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Ticket>().WithMany().HasForeignKey(r => r.TicketId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AgentTaskConfiguration : IEntityTypeConfiguration<AgentTask>
{
    public void Configure(EntityTypeBuilder<AgentTask> builder)
    {
        builder.ToTable("agent_tasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Title).HasMaxLength(AgentTask.TitleMaxLength);
        builder.Property(t => t.Notes).HasMaxLength(4000);
        builder.Ignore(t => t.IsCompleted);
        builder.HasIndex(t => new { t.AssigneeId, t.CompletedAt, t.DueAt });
        builder.HasIndex(t => new { t.RemindAt, t.ReminderSentAt });
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.AssigneeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Ticket>().WithMany().HasForeignKey(t => t.TicketId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Customer>().WithMany().HasForeignKey(t => t.CustomerId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class QuickReplyConfiguration : IEntityTypeConfiguration<QuickReply>
{
    public void Configure(EntityTypeBuilder<QuickReply> builder)
    {
        builder.ToTable("quick_replies");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).ValueGeneratedNever();
        builder.Property(q => q.Title).HasMaxLength(QuickReply.TitleMaxLength);
        builder.Property(q => q.Shortcut).HasMaxLength(QuickReply.ShortcutMaxLength);
        builder.Property(q => q.Body).HasMaxLength(QuickReply.BodyMaxLength);
        builder.Property(q => q.Language).HasMaxLength(5);
        builder.HasIndex(q => new { q.OwnerId, q.Shortcut });
        builder.HasOne<User>().WithMany().HasForeignKey(q => q.OwnerId).OnDelete(DeleteBehavior.Cascade);
    }
}
