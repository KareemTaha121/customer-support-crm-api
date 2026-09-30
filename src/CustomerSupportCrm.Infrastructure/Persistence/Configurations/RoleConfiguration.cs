using CustomerSupportCrm.Domain.Roles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCrm.Infrastructure.Persistence.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(role => role.Id);
        builder.Property(role => role.Id).ValueGeneratedNever();

        builder.Property(role => role.Name).HasMaxLength(Role.NameMaxLength);
        builder.Property(role => role.NormalizedName).HasMaxLength(Role.NameMaxLength);
        builder.HasIndex(role => role.NormalizedName).IsUnique();
        builder.Property(role => role.Description).HasMaxLength(Role.DescriptionMaxLength);

        builder.HasMany(role => role.Permissions)
            .WithOne()
            .HasForeignKey(permission => permission.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(role => role.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(role => role.PermissionCodes);

        builder.HasXminConcurrencyToken();
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");
        builder.HasKey(permission => new { permission.RoleId, permission.Permission });
        builder.Property(permission => permission.Permission).HasMaxLength(100);
    }
}
