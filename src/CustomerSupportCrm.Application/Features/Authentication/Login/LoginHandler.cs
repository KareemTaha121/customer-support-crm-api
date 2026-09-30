using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Authentication.Common;
using CustomerSupportCrm.Domain.Audit;
using CustomerSupportCrm.Domain.Shared;
using CustomerSupportCrm.Domain.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Authentication.Login;

/// <summary>
/// Verifies credentials, enforces lockout, and starts a session. Unknown email and wrong
/// password produce the same error and similar timing. Failed attempts are persisted
/// before the error is raised.
/// </summary>
internal sealed class LoginHandler(
    IApplicationDbContext db,
    IPasswordHasher passwordHasher,
    UserSessionService sessions,
    IAuditTrail audit,
    TimeProvider time)
    : IRequestHandler<LoginCommand, AuthenticatedSession>
{
    private static string? _timingEqualizerHash;

    public async Task<AuthenticatedSession> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var email = EmailAddress.Normalize(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            passwordHasher.Verify(_timingEqualizerHash ??= passwordHasher.Hash(Guid.NewGuid().ToString()), request.Password);
            audit.Record(AuditActions.LoginFailed, AuditEntityTypes.User, entityId: null, newValues: new { email, reason = "unknown_email" });
            await db.SaveChangesAsync(cancellationToken);
            throw InvalidCredentials();
        }

        if (user.IsLockedOut(now))
        {
            audit.Record(AuditActions.LoginLockedOut, AuditEntityTypes.User, user.Id.ToString(), newValues: new { user.LockoutEndsAt }, actorUserId: user.Id);
            await db.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedException(AuthenticationErrors.AccountLocked, "The account is temporarily locked. Try again later.");
        }

        var verification = passwordHasher.Verify(user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.RecordFailedLogin(now);
            audit.Record(AuditActions.LoginFailed, AuditEntityTypes.User, user.Id.ToString(), newValues: new { reason = "invalid_password", lockedOut = user.IsLockedOut(now) }, actorUserId: user.Id);
            await db.SaveChangesAsync(cancellationToken);
            throw InvalidCredentials();
        }

        if (!user.IsActive)
        {
            audit.Record(AuditActions.LoginFailed, AuditEntityTypes.User, user.Id.ToString(), newValues: new { reason = "disabled" }, actorUserId: user.Id);
            await db.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedException(AuthenticationErrors.AccountDisabled, "The account is disabled.");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.ChangePasswordHash(passwordHasher.Hash(request.Password));
        }

        user.RecordSuccessfulLogin(now);
        var session = await sessions.StartAsync(user, cancellationToken);
        audit.Record(AuditActions.LoginSucceeded, AuditEntityTypes.User, user.Id.ToString(), actorUserId: user.Id);
        await db.SaveChangesAsync(cancellationToken);

        return session;
    }

    private static UnauthorizedException InvalidCredentials() =>
        new(AuthenticationErrors.InvalidCredentials, "The email or password is incorrect.");
}
