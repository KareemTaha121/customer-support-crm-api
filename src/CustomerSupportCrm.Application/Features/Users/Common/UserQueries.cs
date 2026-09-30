using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Resources;
using CustomerSupportCrm.Contracts.Users;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Users;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace CustomerSupportCrm.Application.Features.Users.Common;

/// <summary>Reads and checks shared by the user-management slices.</summary>
internal static class UserQueries
{
    public static async Task<UserResponse> GetResponseAsync(IApplicationDbContext db, UserId userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var row = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                u.Status,
                u.LockoutEndsAt,
                u.LastLoginAt,
                u.CreatedAt,
                u.UpdatedAt,
                Roles = db.Roles
                    .Where(r => u.Roles.Any(membership => membership.RoleId == r.Id))
                    .OrderBy(r => r.Name)
                    .Select(r => new { r.Id, r.Name })
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(UserErrors.UserNotFound, "The user was not found.");

        return new UserResponse(
            row.Id.Value,
            row.Email,
            row.DisplayName,
            row.Status.ToString(),
            row.LockoutEndsAt > now,
            [.. row.Roles.Select(r => new RoleReference(r.Id.Value, r.Name))],
            row.LastLoginAt,
            row.CreatedAt,
            row.UpdatedAt);
    }

    /// <summary>Maps requested role ids to existing roles, failing validation for unknown ids.</summary>
    public static async Task<IReadOnlyList<RoleId>> ResolveRoleIdsAsync(
        IApplicationDbContext db,
        IReadOnlyList<Guid> roleIds,
        IStringLocalizer<Messages> localizer,
        CancellationToken cancellationToken)
    {
        // Roles are a small set; loading their ids avoids translating a converted-id IN list.
        var existing = (await db.Roles.Select(r => r.Id).ToListAsync(cancellationToken)).ToHashSet();
        var requested = roleIds.Distinct().Select(id => new RoleId(id)).ToList();

        if (requested.Any(id => !existing.Contains(id)))
        {
            throw new ValidationException(
            [
                new ValidationFailure("RoleIds", localizer[UserErrors.UnknownRole]) { ErrorCode = UserErrors.UnknownRole },
            ]);
        }

        return requested;
    }

    public static async Task<RoleId?> GetAdministratorRoleIdAsync(IApplicationDbContext db, CancellationToken cancellationToken)
    {
        var normalized = Role.Normalize(Role.AdministratorName);
        return await db.Roles
            .Where(r => r.IsSystem && r.NormalizedName == normalized)
            .Select(r => (RoleId?)r.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Blocks a change that would leave no active administrator.</summary>
    public static async Task EnsureAnotherActiveAdministratorAsync(IApplicationDbContext db, RoleId administratorRoleId, UserId excluding, CancellationToken cancellationToken)
    {
        var anotherExists = await db.Users.AnyAsync(
            u => u.Id != excluding
                && u.Status == UserStatus.Active
                && u.Roles.Any(membership => membership.RoleId == administratorRoleId),
            cancellationToken);

        if (!anotherExists)
        {
            throw new ConflictException(UserErrors.LastAdministrator, "At least one active administrator is required.");
        }
    }
}
