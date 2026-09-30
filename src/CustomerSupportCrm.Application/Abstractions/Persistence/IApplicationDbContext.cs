using CustomerSupportCrm.Domain.Audit;
using CustomerSupportCrm.Domain.Notifications;
using CustomerSupportCrm.Domain.Organizations;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Abstractions.Persistence;

/// <summary>
/// The EF Core unit of work handlers use directly (no generic repositories). Partial: each
/// feature declares its own sets in <c>IApplicationDbContext.&lt;Feature&gt;.cs</c>.
/// </summary>
public partial interface IApplicationDbContext
{
    DbSet<User> Users { get; }

    DbSet<Role> Roles { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<AuditLog> AuditLogs { get; }

    DbSet<Organization> Organizations { get; }

    DbSet<Branch> Branches { get; }

    DbSet<Department> Departments { get; }

    DbSet<Notification> Notifications { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
