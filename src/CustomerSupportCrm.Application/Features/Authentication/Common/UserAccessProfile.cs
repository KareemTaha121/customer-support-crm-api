using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Authentication.Common;

/// <summary>
/// A user's role names, the union of the permissions those roles grant, and whether the user can see
/// any branch data at all (<c>data.all_branches</c> or at least one branch/department scope).
/// </summary>
public sealed record UserAccessProfile(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, bool HasDataAccess)
{
    public static async Task<UserAccessProfile> LoadAsync(IApplicationDbContext db, UserId userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var roles = await db.Roles
            .Where(role => db.Users.Any(user => user.Id == userId && user.Roles.Any(membership => membership.RoleId == role.Id)))
            .Select(role => new { role.Name, Permissions = role.Permissions.Select(p => p.Permission).ToList() })
            .ToListAsync(cancellationToken);

        string[] permissions = [.. roles.SelectMany(r => r.Permissions).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        var hasDataAccess = permissions.Contains(Domain.Roles.Permissions.DataAllBranches, StringComparer.Ordinal)
            || await db.Users.AnyAsync(user => user.Id == userId && user.Scopes.Any(), cancellationToken);

        return new UserAccessProfile(
            [.. roles.Select(r => r.Name).Order(StringComparer.Ordinal)],
            permissions,
            hasDataAccess);
    }
}
