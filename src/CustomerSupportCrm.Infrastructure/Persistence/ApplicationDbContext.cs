using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Domain.Audit;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Users;
using CustomerSupportCrm.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<UserId>().HaveConversion<StronglyTypedIdConverters.UserIdConverter>();
        configurationBuilder.Properties<RoleId>().HaveConversion<StronglyTypedIdConverters.RoleIdConverter>();
    }
}
