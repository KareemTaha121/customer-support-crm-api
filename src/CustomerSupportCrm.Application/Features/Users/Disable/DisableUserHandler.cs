using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Authentication.Common;
using CustomerSupportCrm.Application.Features.Users.Common;
using CustomerSupportCrm.Contracts.Users;
using CustomerSupportCrm.Domain.Audit;
using CustomerSupportCrm.Domain.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Users.Disable;

/// <summary>
/// Disables the account and revokes its sessions. Existing access tokens stay valid until
/// they expire (minutes); refresh is refused immediately.
/// </summary>
internal sealed class DisableUserHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    UserSessionService sessions,
    IAuditTrail audit,
    TimeProvider time)
    : IRequestHandler<DisableUserCommand, UserResponse>
{
    public async Task<UserResponse> Handle(DisableUserCommand request, CancellationToken cancellationToken)
    {
        var userId = new UserId(request.UserId);
        if (userId == currentUser.UserId)
        {
            throw new ConflictException(UserErrors.CannotDisableSelf, "You cannot disable your own account.");
        }

        var user = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException(UserErrors.UserNotFound, "The user was not found.");

        if (user.IsActive)
        {
            var administratorRoleId = await UserQueries.GetAdministratorRoleIdAsync(db, cancellationToken);
            if (administratorRoleId is { } adminId && user.HasRole(adminId))
            {
                await UserQueries.EnsureAnotherActiveAdministratorAsync(db, adminId, user.Id, cancellationToken);
            }

            user.Disable();
            await sessions.RevokeAsync(token => token.UserId == userId, RefreshTokenRevocationReason.UserDisabled, cancellationToken);
            audit.Record(AuditActions.UserDisabled, AuditEntityTypes.User, user.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);
        }

        return await UserQueries.GetResponseAsync(db, user.Id, time.GetUtcNow(), cancellationToken);
    }
}
