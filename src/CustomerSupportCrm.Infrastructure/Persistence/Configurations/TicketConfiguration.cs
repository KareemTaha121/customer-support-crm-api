using CustomerSupportCrm.Domain.Customers;
using CustomerSupportCrm.Domain.Organizations;
using CustomerSupportCrm.Domain.Shared;
using CustomerSupportCrm.Domain.Sla;
using CustomerSupportCrm.Domain.Tickets;
using CustomerSupportCrm.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCrm.Infrastructure.Persistence.Configurations;

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.Number).HasMaxLength(Ticket.NumberMaxLength);
        builder.HasIndex(t => t.Number).IsUnique();
        builder.Property(t => t.Subject).HasMaxLength(Ticket.SubjectMaxLength);
        builder.Property(t => t.Description).HasMaxLength(Ticket.DescriptionMaxLength);
        builder.Property(t => t.Priority).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.ReplyAddress).HasMaxLength(EmailAddress.MaxLength);
        builder.Property(t => t.Tags).HasColumnType("text[]");
        builder.Property(t => t.SatisfactionComment).HasMaxLength(2000);
        builder.Ignore(t => t.IsActive);

        builder.HasIndex(t => new { t.Status, t.Priority });
        builder.HasIndex(t => new { t.AssignedAgentId, t.Status });
        builder.HasIndex(t => new { t.BranchId, t.DepartmentId, t.Status });
        builder.HasIndex(t => new { t.CustomerId, t.CreatedAt });
        builder.HasIndex(t => t.CreatedAt);
        builder.HasIndex(t => t.ResolutionDueAt);

        builder.HasOne<Customer>().WithMany().HasForeignKey(t => t.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TicketCategory>().WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Branch>().WithMany().HasForeignKey(t => t.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(t => t.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.AssignedAgentId).OnDelete(DeleteBehavior.SetNull);

        // Deleting a policy clears the reference; tickets keep their computed due dates.
        builder.HasOne<SlaPolicy>().WithMany().HasForeignKey(t => t.SlaPolicyId).OnDelete(DeleteBehavior.SetNull);

        builder.HasXminConcurrencyToken();
    }
}

internal sealed class TicketMessageConfiguration : IEntityTypeConfiguration<TicketMessage>
{
    public void Configure(EntityTypeBuilder<TicketMessage> builder)
    {
        builder.ToTable("ticket_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.AuthorType).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Body).HasMaxLength(TicketMessage.BodyMaxLength);
        builder.Property(m => m.ExternalMessageId).HasMaxLength(300);
        builder.Property(m => m.MentionedUserIds).HasColumnType("uuid[]");
        builder.HasIndex(m => new { m.TicketId, m.CreatedAt });
        builder.HasIndex(m => m.ExternalMessageId);
        builder.HasOne<Ticket>().WithMany().HasForeignKey(m => m.TicketId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TicketHistoryConfiguration : IEntityTypeConfiguration<TicketHistory>
{
    public void Configure(EntityTypeBuilder<TicketHistory> builder)
    {
        builder.ToTable("ticket_history");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.Action).HasMaxLength(40);
        builder.Property(h => h.OldValue).HasMaxLength(500);
        builder.Property(h => h.NewValue).HasMaxLength(500);
        builder.HasIndex(h => new { h.TicketId, h.OccurredAt });
        builder.HasOne<Ticket>().WithMany().HasForeignKey(h => h.TicketId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TicketCategoryConfiguration : IEntityTypeConfiguration<TicketCategory>
{
    public void Configure(EntityTypeBuilder<TicketCategory> builder)
    {
        builder.ToTable("ticket_categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(TicketCategory.NameMaxLength);
        builder.Property(c => c.NameAr).HasMaxLength(TicketCategory.NameMaxLength);
        builder.Property(c => c.DefaultPriority).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<TicketCategory>().WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(c => c.DefaultDepartmentId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class CustomerAccountConfiguration : IEntityTypeConfiguration<CustomerAccount>
{
    public void Configure(EntityTypeBuilder<CustomerAccount> builder)
    {
        builder.ToTable("customer_accounts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Email).HasMaxLength(EmailAddress.MaxLength);
        builder.HasIndex(a => a.Email).IsUnique();
        builder.HasIndex(a => a.CustomerId);
        builder.Property(a => a.DisplayName).HasMaxLength(Customer.NameMaxLength);
        builder.Property(a => a.PasswordHash).HasMaxLength(512);
        builder.Property(a => a.VerificationCodeHash).HasMaxLength(128);
        builder.Property(a => a.PasswordResetTokenHash).HasMaxLength(128);
        builder.HasIndex(a => a.PasswordResetTokenHash).HasFilter("password_reset_token_hash IS NOT NULL");
        builder.HasOne<Customer>().WithMany().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasXminConcurrencyToken();
    }
}
