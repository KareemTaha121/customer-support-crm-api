using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Users.Common;
using CustomerSupportCrm.Application.Resources;
using CustomerSupportCrm.Contracts.Users;
using CustomerSupportCrm.Domain.Audit;
using CustomerSupportCrm.Domain.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace CustomerSupportCrm.Application.Features.Users.SetRoles;

internal sealed class SetUserRolesHandler(
    IApplicationDbContext db,
    IAuditTrail audit,
    IStringLocalizer<Messages> localizer,
    TimeProvider time)
    : IRequestHandler<SetUserRolesCommand, UserResponse>
{
    public async Task<UserResponse> Handle(SetUserRolesCommand request, CancellationToken cancellationToken)
    {
        var userId = new UserId(request.UserId);
        var user = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(UserErrors.UserNotFound, "The user was not found.");

        var roleIds = await UserQueries.ResolveRoleIdsAsync(db, request.RoleIds, localizer, cancellationToken);

        var administratorRoleId = await UserQueries.GetAdministratorRoleIdAsync(db, cancellationToken);
        if (administratorRoleId is { } adminId && user.IsActive && user.HasRole(adminId) && !roleIds.Contains(adminId))
        {
            await UserQueries.EnsureAnotherActiveAdministratorAsync(db, adminId, user.Id, cancellationToken);
        }

        var previous = user.RoleIds.Select(id => id.Value).ToList();
        user.SetRoles(roleIds);

        audit.Record(
            AuditActions.UserRolesChanged,
            AuditEntityTypes.User,
            user.Id.ToString(),
            oldValues: new { roleIds = previous },
            newValues: new { roleIds = roleIds.Select(id => id.Value) });
        await db.SaveChangesAsync(cancellationToken);

        return await UserQueries.GetResponseAsync(db, user.Id, time.GetUtcNow(), cancellationToken);
    }
}
