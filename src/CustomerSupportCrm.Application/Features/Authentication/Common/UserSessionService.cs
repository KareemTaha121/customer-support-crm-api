using System.Linq.Expressions;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Contracts.Authentication;
using CustomerSupportCrm.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Authentication.Common;

/// <summary>
/// Issues, rotates and revokes refresh-token sessions. Shared by the login, refresh, logout,
/// password and user-management slices. Changes are saved by the calling handler.
/// </summary>
public sealed class UserSessionService(
    IApplicationDbContext db,
    ITokenService tokens,
    IRequestContext request,
    TimeProvider time)
{
    public Task<AuthenticatedSession> StartAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var refresh = tokens.GenerateRefreshToken();
        var stored = RefreshToken.Issue(user.Id, refresh.Hash, time.GetUtcNow(), tokens.RefreshTokenLifetime, request.IpAddress, request.UserAgent);
        db.RefreshTokens.Add(stored);

        return BuildAsync(user, stored, refresh.Token, cancellationToken);
    }

    public Task<AuthenticatedSession> RotateAsync(User user, RefreshToken current, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(current);

        var refresh = tokens.GenerateRefreshToken();
        var next = current.Rotate(refresh.Hash, time.GetUtcNow(), tokens.RefreshTokenLifetime, request.IpAddress, request.UserAgent);
        db.RefreshTokens.Add(next);

        return BuildAsync(user, next, refresh.Token, cancellationToken);
    }

    /// <summary>
    /// Revokes the active tokens matching <paramref name="predicate"/>. A session has at most one
    /// active token; spent tokens are already unusable.
    /// </summary>
    public async Task RevokeAsync(Expression<Func<RefreshToken, bool>> predicate, RefreshTokenRevocationReason reason, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var active = await db.RefreshTokens
            .Where(predicate)
            .Where(token => token.UsedAt == null && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in active)
        {
            token.Revoke(now, reason);
        }
    }

    private async Task<AuthenticatedSession> BuildAsync(User user, RefreshToken stored, string refreshToken, CancellationToken cancellationToken)
    {
        var profile = await UserAccessProfile.LoadAsync(db, user.Id, cancellationToken);
        var access = tokens.CreateAccessToken(user, stored.SessionId, profile.Roles, profile.Permissions);

        var response = new AccessTokenResponse(
            access.Token,
            access.ExpiresAt,
            new CurrentUserResponse(user.Id.Value, user.Email, user.DisplayName, profile.Roles, profile.Permissions, profile.HasDataAccess));

        return new AuthenticatedSession(response, refreshToken, stored.ExpiresAt);
    }
}
