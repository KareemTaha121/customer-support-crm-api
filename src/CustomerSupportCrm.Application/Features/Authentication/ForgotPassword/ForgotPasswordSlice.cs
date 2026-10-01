using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Features.Authentication.Common;
using CustomerSupportCrm.Contracts.Authentication;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Domain.Audit;
using CustomerSupportCrm.Domain.Shared;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Authentication.ForgotPassword;

public sealed record ForgotPasswordCommand(string Email) : IRequest;

internal sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordValidator() => RuleFor(c => c.Email).NotEmpty().MaximumLength(EmailAddress.MaxLength);
}

/// <summary>
/// Emails a single-use reset link to an active staff user. Unknown, disabled and rate-limited
/// addresses get the same response, so the endpoint cannot be used to discover accounts.
/// </summary>
internal sealed class ForgotPasswordHandler(IApplicationDbContext db, ITokenService tokens, PasswordResetMailer mailer, IAuditTrail audit, TimeProvider time)
    : IRequestHandler<ForgotPasswordCommand>
{
    public async Task Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var email = EmailAddress.Normalize(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);
        if (user is null || !user.IsActive || !user.CanRequestPasswordReset(now))
        {
            return;
        }

        var token = tokens.GenerateRefreshToken();
        user.StartPasswordReset(token.Hash, now);
        await mailer.QueueStaffAsync(user.Email, token.Token, cancellationToken);
        audit.Record(AuditActions.PasswordResetRequested, AuditEntityTypes.User, user.Id.ToString(), actorUserId: user.Id);
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class ForgotPasswordEndpoint : IEndpoint
{
    public const string SentMessage = "If the address belongs to an account, a password reset link has been sent.";

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost($"{AuthenticationHttp.RoutePrefix}/forgot-password", async (ForgotPasswordRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                await sender.Send(new ForgotPasswordCommand(request.Email), cancellationToken);
                return ApiResults.Success(SentMessage);
            })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .WithName("ForgotPassword")
            .WithTags("Authentication")
            .Produces<ApiResponse<object?>>();
}
