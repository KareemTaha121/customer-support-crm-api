using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Authentication.Common;

/// <summary>A user's role names and the union of the permissions those roles grant.</summary>
public sealed record UserAccessProfile(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)
{
    public static async Task<UserAccessProfile> LoadAsync(IApplicationDbContext db, UserId userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var roles = await db.Roles
            .Where(role => db.Users.Any(user => user.Id == userId && user.Roles.Any(membership => membership.RoleId == role.Id)))
            .Select(role => new { role.Name, Permissions = role.Permissions.Select(p => p.Permission).ToList() })
            .ToListAsync(cancellationToken);

        return new UserAccessProfile(
            [.. roles.Select(r => r.Name).Order(StringComparer.Ordinal)],
            [.. roles.SelectMany(r => r.Permissions).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
    }
}
