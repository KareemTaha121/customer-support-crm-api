using CustomerSupportCrm.Domain.Customers;
using CustomerSupportCrm.Domain.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCrm.Infrastructure.Persistence.Configurations;

internal sealed class OutboundMessageConfiguration : IEntityTypeConfiguration<OutboundMessage>
{
    public void Configure(EntityTypeBuilder<OutboundMessage> builder)
    {
        builder.ToTable("outbound_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.To).HasMaxLength(320);
        builder.Property(m => m.Subject).HasMaxLength(500);
        builder.Property(m => m.LastError).HasMaxLength(1000);
        builder.Property(m => m.ProviderMessageId).HasMaxLength(300);
        builder.HasIndex(m => new { m.Status, m.NextAttemptAt });
        builder.HasIndex(m => m.TicketId);
    }
}

internal sealed class InboundReceiptConfiguration : IEntityTypeConfiguration<InboundReceipt>
{
    public void Configure(EntityTypeBuilder<InboundReceipt> builder)
    {
        builder.ToTable("inbound_receipts");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.ExternalId).HasMaxLength(300);
        builder.HasIndex(r => new { r.Channel, r.ExternalId }).IsUnique();
    }
}

internal sealed class ChatConversationConfiguration : IEntityTypeConfiguration<ChatConversation>
{
    public void Configure(EntityTypeBuilder<ChatConversation> builder)
    {
        builder.ToTable("chat_conversations");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.VisitorName).HasMaxLength(200);
        builder.Property(c => c.AccessTokenHash).HasMaxLength(128);
        builder.HasIndex(c => new { c.Status, c.StartedAt });
        builder.HasIndex(c => c.TicketId).IsUnique();
        builder.HasOne<Ticket>().WithMany().HasForeignKey(c => c.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Customer>().WithMany().HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasXminConcurrencyToken();
    }
}
