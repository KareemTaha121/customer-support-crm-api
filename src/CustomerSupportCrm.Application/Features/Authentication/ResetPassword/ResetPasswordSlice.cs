using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Validation;
using CustomerSupportCrm.Application.Features.Authentication.Common;
using CustomerSupportCrm.Application.Resources;
using CustomerSupportCrm.Contracts.Authentication;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Domain.Audit;
using CustomerSupportCrm.Domain.Users;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace CustomerSupportCrm.Application.Features.Authentication.ResetPassword;

public sealed record ResetPasswordCommand(string Token, string NewPassword) : IRequest;

internal sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(c => c.Token).NotEmpty().MaximumLength(256);
        RuleFor(c => c.NewPassword).ValidPassword();
    }
}

/// <summary>
/// Sets a new password from an emailed reset token, clears the lockout and ends every session.
/// It does not sign the user in.
/// </summary>
internal sealed class ResetPasswordHandler(
    IApplicationDbContext db,
    ITokenService tokens,
    IPasswordHasher passwordHasher,
    UserSessionService sessions,
    IAuditTrail audit,
    IStringLocalizer<Messages> localizer,
    TimeProvider time)
    : IRequestHandler<ResetPasswordCommand>
{
    public async Task Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var hash = tokens.HashRefreshToken(request.Token.Trim());
        var user = await db.Users.SingleOrDefaultAsync(u => u.PasswordResetTokenHash == hash, cancellationToken);
        if (user is null || !user.IsValidPasswordReset(hash, time.GetUtcNow()))
        {
            throw InvalidToken(localizer);
        }

        user.CompletePasswordReset(passwordHasher.Hash(request.NewPassword));

        var userId = user.Id;
        await sessions.RevokeAsync(t => t.UserId == userId, RefreshTokenRevocationReason.PasswordChanged, cancellationToken);
        audit.Record(AuditActions.PasswordReset, AuditEntityTypes.User, userId.ToString(), actorUserId: userId);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Used, expired, replaced and unknown tokens all get the same field error.</summary>
    internal static ValidationException InvalidToken(IStringLocalizer<Messages> localizer) =>
        new([new ValidationFailure(nameof(ResetPasswordCommand.Token), localizer[AuthenticationErrors.InvalidResetToken]) { ErrorCode = AuthenticationErrors.InvalidResetToken }]);
}

internal sealed class ResetPasswordEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost($"{AuthenticationHttp.RoutePrefix}/reset-password", async (CompletePasswordResetRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                await sender.Send(new ResetPasswordCommand(request.Token, request.NewPassword), cancellationToken);
                return ApiResults.Success();
            })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .WithName("ResetPassword")
            .WithTags("Authentication")
            .Produces<ApiResponse<object?>>();
}
