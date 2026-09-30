using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Authentication.Common;
using CustomerSupportCrm.Application.Resources;
using CustomerSupportCrm.Domain.Audit;
using CustomerSupportCrm.Domain.Users;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace CustomerSupportCrm.Application.Features.Authentication.ChangePassword;

/// <summary>Changes the caller's password and signs out every other session.</summary>
internal sealed class ChangePasswordHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IPasswordHasher passwordHasher,
    UserSessionService sessions,
    IAuditTrail audit,
    IStringLocalizer<Messages> localizer)
    : IRequestHandler<ChangePasswordCommand>
{
    public async Task Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new UnauthorizedException();

        if (passwordHasher.Verify(user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            throw new ValidationException(
            [
                new ValidationFailure(nameof(request.CurrentPassword), localizer[AuthenticationErrors.InvalidCurrentPassword])
                {
                    ErrorCode = AuthenticationErrors.InvalidCurrentPassword,
                },
            ]);
        }

        user.ChangePasswordHash(passwordHasher.Hash(request.NewPassword));

        var currentSession = currentUser.SessionId;
        await sessions.RevokeAsync(
            token => token.UserId == userId && token.SessionId != currentSession,
            RefreshTokenRevocationReason.PasswordChanged,
            cancellationToken);

        audit.Record(AuditActions.PasswordChanged, AuditEntityTypes.User, userId.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }
}
