using CustomerSupportCrm.Domain.Notifications;
using CustomerSupportCrm.Domain.Organizations;
using CustomerSupportCrm.Domain.Shared;
using CustomerSupportCrm.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCrm.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Name).HasMaxLength(Organization.NameMaxLength);
        builder.Property(o => o.SupportEmail).HasMaxLength(Organization.ContactMaxLength);
        builder.Property(o => o.SupportPhone).HasMaxLength(32);
        builder.Property(o => o.DefaultCulture).HasMaxLength(5);
        builder.Property(o => o.TimeZone).HasMaxLength(Organization.TimeZoneMaxLength);
        builder.Property(o => o.PrimaryColor).HasMaxLength(7);
        builder.Property(o => o.AccentColor).HasMaxLength(7);
        builder.Property(o => o.LogoStorageKey).HasMaxLength(300);
        builder.Property(o => o.LogoContentType).HasMaxLength(100);
        builder.HasXminConcurrencyToken();
    }
}

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("branches");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Code).HasMaxLength(Branch.CodeMaxLength);
        builder.HasIndex(b => b.Code).IsUnique();
        builder.Property(b => b.Name).HasMaxLength(Branch.NameMaxLength);
        builder.Property(b => b.Address).HasMaxLength(500);
        builder.Property(b => b.Phone).HasMaxLength(32);
        builder.HasXminConcurrencyToken();
    }
}

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("departments");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Code).HasMaxLength(Department.CodeMaxLength);
        builder.HasIndex(d => new { d.BranchId, d.Code }).IsUnique();
        builder.Property(d => d.Name).HasMaxLength(Department.NameMaxLength);
        builder.Property(d => d.Email).HasMaxLength(EmailAddress.MaxLength);
        builder.HasIndex(d => d.Email);
        builder.HasOne<Branch>().WithMany().HasForeignKey(d => d.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasXminConcurrencyToken();
    }
}

internal sealed class UserScopeConfiguration : IEntityTypeConfiguration<UserScope>
{
    public void Configure(EntityTypeBuilder<UserScope> builder)
    {
        builder.ToTable("user_scopes");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.HasIndex(s => new { s.UserId, s.BranchId, s.DepartmentId }).IsUnique().AreNullsDistinct(false);
        builder.HasOne<Branch>().WithMany().HasForeignKey(s => s.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(s => s.DepartmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Type).HasMaxLength(Notification.TypeMaxLength);
        builder.Property(n => n.Title).HasMaxLength(Notification.TitleMaxLength);
        builder.Property(n => n.Body).HasMaxLength(2000);
        builder.Property(n => n.Link).HasMaxLength(Notification.LinkMaxLength);
        builder.Property(n => n.Data).HasColumnType("jsonb");
        builder.HasIndex(n => new { n.RecipientId, n.ReadAt, n.CreatedAt });
        builder.HasOne<User>().WithMany().HasForeignKey(n => n.RecipientId).OnDelete(DeleteBehavior.Cascade);
    }
}
