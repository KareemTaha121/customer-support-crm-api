using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Authentication.Common;
using CustomerSupportCrm.Domain.Audit;
using CustomerSupportCrm.Domain.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Authentication.Refresh;

/// <summary>
/// Rotates the refresh token and issues a fresh access token with current roles and
/// permissions. Presenting a spent token revokes the whole session (token theft signal).
/// </summary>
internal sealed class RefreshSessionHandler(
    IApplicationDbContext db,
    ITokenService tokens,
    UserSessionService sessions,
    IAuditTrail audit,
    TimeProvider time)
    : IRequestHandler<RefreshSessionCommand, AuthenticatedSession>
{
    public async Task<AuthenticatedSession> Handle(RefreshSessionCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            throw InvalidToken();
        }

        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(token => token.TokenHash == hash, cancellationToken)
            ?? throw InvalidToken();

        if (stored.IsSpent)
        {
            await sessions.RevokeAsync(token => token.SessionId == stored.SessionId, RefreshTokenRevocationReason.ReuseDetected, cancellationToken);
            audit.Record(AuditActions.RefreshTokenReuseDetected, AuditEntityTypes.User, stored.UserId.ToString(), newValues: new { stored.SessionId }, actorUserId: stored.UserId);
            await db.SaveChangesAsync(cancellationToken);
            throw InvalidToken();
        }

        if (!stored.IsActive(time.GetUtcNow()))
        {
            throw InvalidToken();
        }

        var user = await db.Users.SingleAsync(u => u.Id == stored.UserId, cancellationToken);
        if (!user.IsActive)
        {
            await sessions.RevokeAsync(token => token.UserId == user.Id, RefreshTokenRevocationReason.UserDisabled, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            throw InvalidToken();
        }

        var session = await sessions.RotateAsync(user, stored, cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent request rotated the same token first.
            throw InvalidToken();
        }

        return session;
    }

    private static UnauthorizedException InvalidToken() =>
        new(AuthenticationErrors.InvalidRefreshToken, "The session has expired. Please sign in again.");
}
