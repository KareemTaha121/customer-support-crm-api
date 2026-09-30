using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Authentication.Common;
using CustomerSupportCrm.Contracts.Authentication;
using CustomerSupportCrm.Domain.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Authentication.GetCurrentUser;

/// <summary>Reads roles and permissions from the database, not the (possibly stale) token.</summary>
internal sealed class GetCurrentUserHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetCurrentUserQuery, CurrentUserResponse>
{
    public async Task<CurrentUserResponse> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.DisplayName, u.Status })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedException();

        // The access token outlives a disable by up to its lifetime; /me is what clients use to
        // restore a session, so refuse it as soon as the account is disabled.
        if (user.Status != UserStatus.Active)
        {
            throw new UnauthorizedException(AuthenticationErrors.AccountDisabled, "The account is disabled.");
        }

        var profile = await UserAccessProfile.LoadAsync(db, userId, cancellationToken);
        return new CurrentUserResponse(userId.Value, user.Email, user.DisplayName, profile.Roles, profile.Permissions);
    }
}
