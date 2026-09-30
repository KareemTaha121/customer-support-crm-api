using CustomerSupportCrm.Domain.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCrm.Infrastructure.Persistence.Configurations;

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).ValueGeneratedNever();
        builder.Property(k => k.Name).HasMaxLength(150);
        builder.Property(k => k.DisplayPrefix).HasMaxLength(20);
        builder.Property(k => k.KeyHash).HasMaxLength(128);
        builder.HasIndex(k => k.KeyHash).IsUnique();
        builder.Property(k => k.Scopes).HasColumnType("text[]");
        builder.HasXminConcurrencyToken();
    }
}

internal sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> builder)
    {
        builder.ToTable("webhook_subscriptions");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();
        builder.Property(w => w.Name).HasMaxLength(150);
        builder.Property(w => w.Url).HasMaxLength(500);
        builder.Property(w => w.Events).HasColumnType("text[]");
        builder.Property(w => w.ProtectedSecret).HasMaxLength(1000);
        builder.HasXminConcurrencyToken();
    }
}

internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("webhook_deliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.EventType).HasMaxLength(50);
        builder.Property(d => d.Payload).HasColumnType("jsonb");
        builder.Property(d => d.LastError).HasMaxLength(1000);
        builder.HasIndex(d => new { d.Delivered, d.Failed, d.NextAttemptAt });
        builder.HasIndex(d => new { d.SubscriptionId, d.CreatedAt });
        builder.HasOne<WebhookSubscription>().WithMany().HasForeignKey(d => d.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("system_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("key").HasMaxLength(100);
        builder.Property(s => s.Value).HasMaxLength(2000);
    }
}
