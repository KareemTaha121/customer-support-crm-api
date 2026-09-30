using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Contracts.Roles;
using CustomerSupportCrm.Domain.Roles;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Roles.Common;

public static class RoleErrors
{
    public const string RoleNotFound = "ROLE_NOT_FOUND";
    public const string RoleNameTaken = "ROLE_NAME_TAKEN";
    public const string RoleInUse = "ROLE_IN_USE";
}

internal static class RoleQueries
{
    public static IQueryable<RoleResponse> ProjectToResponse(this IQueryable<Role> roles, IApplicationDbContext db) =>
        roles
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Description,
                r.IsSystem,
                Permissions = r.Permissions.Select(p => p.Permission).OrderBy(p => p).ToList(),
                UserCount = db.Users.Count(u => u.Roles.Any(membership => membership.RoleId == r.Id)),
            })
            .Select(r => new RoleResponse(r.Id.Value, r.Name, r.Description, r.IsSystem, r.Permissions, r.UserCount));

    public static async Task<RoleResponse> GetResponseAsync(IApplicationDbContext db, RoleId roleId, CancellationToken cancellationToken) =>
        await db.Roles.AsNoTracking().Where(r => r.Id == roleId).ProjectToResponse(db).SingleOrDefaultAsync(cancellationToken)
        ?? throw NotFound();

    public static NotFoundException NotFound() => new(RoleErrors.RoleNotFound, "The role was not found.");

    public static Task<bool> NameTakenAsync(IApplicationDbContext db, string name, RoleId? excluding, CancellationToken cancellationToken)
    {
        var normalized = Role.Normalize(name);
        return db.Roles.AnyAsync(r => r.NormalizedName == normalized && r.Id != excluding, cancellationToken);
    }
}
