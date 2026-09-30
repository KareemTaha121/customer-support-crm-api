using CustomerSupportCrm.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCrm.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();

        builder.Property(entry => entry.Action).HasMaxLength(AuditLog.ActionMaxLength);
        builder.Property(entry => entry.EntityType).HasMaxLength(AuditLog.EntityTypeMaxLength);
        builder.Property(entry => entry.EntityId).HasMaxLength(AuditLog.EntityIdMaxLength);
        builder.Property(entry => entry.CorrelationId).HasMaxLength(AuditLog.CorrelationIdMaxLength);
        builder.Property(entry => entry.IpAddress).HasMaxLength(AuditLog.IpAddressMaxLength);
        builder.Property(entry => entry.UserAgent).HasMaxLength(AuditLog.UserAgentMaxLength);
        builder.Property(entry => entry.OldValues).HasColumnType("jsonb");
        builder.Property(entry => entry.NewValues).HasColumnType("jsonb");

        // No foreign key to users: audit history outlives the rows it describes.
        builder.HasIndex(entry => entry.OccurredAt);
        builder.HasIndex(entry => new { entry.EntityType, entry.EntityId });
        builder.HasIndex(entry => entry.ActorUserId);
        builder.HasIndex(entry => entry.Action);
    }
}
