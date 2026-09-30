using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Domain.Organizations;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Shared;
using CustomerSupportCrm.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportCrm.Infrastructure.Persistence.Seed;

/// <summary>
/// Applies migrations and idempotent reference data: the Administrator system role (kept in
/// sync with the permission catalog), default roles on first run, and the first administrator.
/// </summary>
internal sealed partial class DatabaseInitializer(
    ApplicationDbContext db,
    IPasswordHasher passwordHasher,
    IOptions<BootstrapOptions> bootstrap,
    ILogger<DatabaseInitializer> logger)
{
    private static readonly (string Name, string Description, IReadOnlyList<string> Permissions)[] DefaultRoles =
    [
        ("Manager", "Supervises teams: assignment, escalation, content, automation and reports.", Permissions.ManagerDefaults),
        ("Agent", "Handles tickets, customers and live chat.", Permissions.AgentDefaults),
    ];

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await db.Database.MigrateAsync(cancellationToken);

        await SeedOrganizationAsync(cancellationToken);
        var administrator = await SeedRolesAsync(cancellationToken);
        await SeedAdministratorAsync(administrator, cancellationToken);
    }

    /// <summary>First run only: the organization, a head-office branch and a support department.</summary>
    private async Task SeedOrganizationAsync(CancellationToken cancellationToken)
    {
        if (await db.Organizations.AnyAsync(cancellationToken))
        {
            return;
        }

        db.Organizations.Add(Organization.Create(bootstrap.Value.OrganizationName));

        if (!await db.Branches.AnyAsync(cancellationToken))
        {
            var branch = Branch.Create("HQ", "Head Office", null, null);
            db.Branches.Add(branch);
            db.Departments.Add(Department.Create(branch.Id, "SUPPORT", "Customer Support", null));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Role> SeedRolesAsync(CancellationToken cancellationToken)
    {
        var firstRun = !await db.Roles.AnyAsync(cancellationToken);
        var normalizedAdministrator = Role.Normalize(Role.AdministratorName);

        var administrator = await db.Roles
            .Include(role => role.Permissions)
            .SingleOrDefaultAsync(role => role.IsSystem && role.NormalizedName == normalizedAdministrator, cancellationToken);

        if (administrator is null)
        {
            administrator = Role.CreateAdministrator();
            db.Roles.Add(administrator);
        }
        else
        {
            administrator.GrantAllPermissions();
        }

        if (firstRun)
        {
            foreach (var (name, description, permissions) in DefaultRoles)
            {
                db.Roles.Add(Role.Create(name, description, permissions));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return administrator;
    }

    private async Task SeedAdministratorAsync(Role administrator, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var options = bootstrap.Value;
        if (string.IsNullOrWhiteSpace(options.AdminEmail) || string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            LogBootstrapSkipped(logger);
            return;
        }

        var user = User.Create(
            EmailAddress.Create(options.AdminEmail),
            options.AdminDisplayName,
            passwordHasher.Hash(options.AdminPassword),
            [administrator.Id]);

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        LogAdministratorCreated(logger, user.Email);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No users exist and Bootstrap:AdminEmail/AdminPassword are not set; no administrator was created")]
    private static partial void LogBootstrapSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created initial administrator {Email}")]
    private static partial void LogAdministratorCreated(ILogger logger, string email);
}

public static class DatabaseInitializerExtensions
{
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(cancellationToken);
    }
}
