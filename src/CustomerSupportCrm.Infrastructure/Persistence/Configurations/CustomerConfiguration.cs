using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Domain.Attachments;
using CustomerSupportCrm.Domain.Customers;
using CustomerSupportCrm.Domain.Organizations;
using CustomerSupportCrm.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCrm.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Number).HasMaxLength(Customer.NumberMaxLength);
        builder.HasIndex(c => c.Number).IsUnique();
        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Name).HasMaxLength(Customer.NameMaxLength);
        builder.HasIndex(c => c.Name);
        builder.Property(c => c.CompanyName).HasMaxLength(Customer.NameMaxLength);
        builder.Property(c => c.PreferredLanguage).HasMaxLength(5);
        builder.Property(c => c.Tags).HasColumnType("text[]");
        builder.Property(c => c.PrimaryEmail).HasMaxLength(EmailAddress.MaxLength);
        builder.HasIndex(c => c.PrimaryEmail);
        builder.Property(c => c.PrimaryPhone).HasMaxLength(PhoneNumber.MaxLength);
        builder.HasIndex(c => c.PrimaryPhone);
        builder.Property(c => c.ExternalSystem).HasMaxLength(50);
        builder.Property(c => c.ExternalId).HasMaxLength(100);
        builder.HasIndex(c => new { c.ExternalSystem, c.ExternalId }).IsUnique().HasFilter("external_id IS NOT NULL");
        builder.HasIndex(c => new { c.BranchId, c.DepartmentId });

        builder.HasOne<Branch>().WithMany().HasForeignKey(c => c.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(c => c.DepartmentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Contacts).WithOne().HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Contacts).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasXminConcurrencyToken();
    }
}

internal sealed class CustomerContactConfiguration : IEntityTypeConfiguration<CustomerContact>
{
    public void Configure(EntityTypeBuilder<CustomerContact> builder)
    {
        builder.ToTable("customer_contacts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Value).HasMaxLength(CustomerContact.ValueMaxLength);
        builder.Property(c => c.Label).HasMaxLength(CustomerContact.LabelMaxLength);
        builder.HasIndex(c => new { c.Type, c.Value });
    }
}

internal sealed class CustomerNoteConfiguration : IEntityTypeConfiguration<CustomerNote>
{
    public void Configure(EntityTypeBuilder<CustomerNote> builder)
    {
        builder.ToTable("customer_notes");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Body).HasMaxLength(CustomerNote.BodyMaxLength);
        builder.HasIndex(n => new { n.CustomerId, n.CreatedAt });
        builder.HasOne<Customer>().WithMany().HasForeignKey(n => n.CustomerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CustomerActivityConfiguration : IEntityTypeConfiguration<CustomerActivity>
{
    public void Configure(EntityTypeBuilder<CustomerActivity> builder)
    {
        builder.ToTable("customer_activities");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Type).HasMaxLength(CustomerActivity.TypeMaxLength);
        builder.Property(a => a.Summary).HasMaxLength(CustomerActivity.SummaryMaxLength);
        builder.Property(a => a.Data).HasColumnType("jsonb");
        builder.HasIndex(a => new { a.CustomerId, a.OccurredAt });
        builder.HasIndex(a => a.TicketId);
        builder.HasOne<Customer>().WithMany().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("attachments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.OwnerType).HasMaxLength(30);
        builder.Property(a => a.FileName).HasMaxLength(Attachment.FileNameMaxLength);
        builder.Property(a => a.ContentType).HasMaxLength(150);
        builder.Property(a => a.StorageKey).HasMaxLength(300);
        builder.HasIndex(a => new { a.OwnerType, a.OwnerId });
        builder.HasIndex(a => a.ParentId);
    }
}

internal static class SequenceConfiguration
{
    public static void AddSequences(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>(Sequences.CustomerNumbers);
        modelBuilder.HasSequence<long>(Sequences.TicketNumbers);
    }
}
