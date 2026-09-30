using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Features.Authentication.Common;
using CustomerSupportCrm.Domain.Audit;
using CustomerSupportCrm.Domain.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Authentication.Logout;

/// <summary>Ends the session the refresh cookie belongs to. Idempotent.</summary>
internal sealed class LogoutHandler(
    IApplicationDbContext db,
    ITokenService tokens,
    UserSessionService sessions,
    IAuditTrail audit)
    : IRequestHandler<LogoutCommand>
{
    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            return;
        }

        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var stored = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);
        if (stored is null)
        {
            return;
        }

        await sessions.RevokeAsync(token => token.SessionId == stored.SessionId, RefreshTokenRevocationReason.Logout, cancellationToken);
        audit.Record(AuditActions.Logout, AuditEntityTypes.User, stored.UserId.ToString(), newValues: new { stored.SessionId }, actorUserId: stored.UserId);
        await db.SaveChangesAsync(cancellationToken);
    }
}
