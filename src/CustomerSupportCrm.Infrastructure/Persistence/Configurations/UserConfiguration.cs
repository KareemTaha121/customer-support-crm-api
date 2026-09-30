using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Shared;
using CustomerSupportCrm.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCrm.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();

        builder.Property(user => user.Email).HasMaxLength(EmailAddress.MaxLength);
        builder.HasIndex(user => user.Email).IsUnique();

        builder.Property(user => user.DisplayName).HasMaxLength(User.DisplayNameMaxLength);
        builder.Property(user => user.PasswordHash).HasMaxLength(512);
        builder.Property(user => user.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasMany(user => user.Roles)
            .WithOne()
            .HasForeignKey(membership => membership.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(user => user.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(user => user.IsActive);
        builder.Ignore(user => user.RoleIds);

        builder.HasXminConcurrencyToken();
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");
        builder.HasKey(membership => new { membership.UserId, membership.RoleId });
        builder.HasIndex(membership => membership.RoleId);

        // Restrict: roles are deleted only when unassigned (enforced by the delete slice).
        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(membership => membership.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
