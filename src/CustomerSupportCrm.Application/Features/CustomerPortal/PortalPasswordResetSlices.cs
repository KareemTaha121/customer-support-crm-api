using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Validation;
using CustomerSupportCrm.Application.Features.Authentication.Common;
using CustomerSupportCrm.Application.Features.Authentication.ForgotPassword;
using CustomerSupportCrm.Application.Features.Authentication.ResetPassword;
using CustomerSupportCrm.Application.Resources;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Portal;
using CustomerSupportCrm.Domain.Shared;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace CustomerSupportCrm.Application.Features.CustomerPortal;

// ---------- Portal password reset (anonymous) ----------

public sealed record PortalForgotPasswordCommand(string Email) : IRequest;

internal sealed class PortalForgotPasswordValidator : AbstractValidator<PortalForgotPasswordCommand>
{
    public PortalForgotPasswordValidator() => RuleFor(c => c.Email).NotEmpty().MaximumLength(EmailAddress.MaxLength);
}

/// <summary>Emails a reset link to an active, verified portal account; every other address gets the same response.</summary>
internal sealed class PortalForgotPasswordHandler(IApplicationDbContext db, ITokenService tokens, PasswordResetMailer mailer, IAuditTrail audit, TimeProvider time)
    : IRequestHandler<PortalForgotPasswordCommand>
{
    public async Task Handle(PortalForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var email = EmailAddress.Normalize(request.Email);
        var account = await db.CustomerAccounts.SingleOrDefaultAsync(a => a.Email == email, cancellationToken);
        if (account is not { IsActive: true, EmailVerified: true } || !account.CanRequestPasswordReset(now))
        {
            return;
        }

        var token = tokens.GenerateRefreshToken();
        account.StartPasswordReset(token.Hash, now);
        var language = await db.Customers.Where(c => c.Id == account.CustomerId).Select(c => c.PreferredLanguage).FirstOrDefaultAsync(cancellationToken) ?? "en";
        await mailer.QueuePortalAsync(account.Email, language, token.Token, cancellationToken);
        audit.Record("customers.portal_password_reset_requested", "Customer", account.CustomerId.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record PortalResetPasswordCommand(string Token, string NewPassword) : IRequest;

internal sealed class PortalResetPasswordValidator : AbstractValidator<PortalResetPasswordCommand>
{
    public PortalResetPasswordValidator()
    {
        RuleFor(c => c.Token).NotEmpty().MaximumLength(256);
        RuleFor(c => c.NewPassword).ValidPassword();
    }
}

/// <summary>Sets a new portal password; portal tokens issued before the reset stop working (ActivePortalAccountFilter).</summary>
internal sealed class PortalResetPasswordHandler(
    IApplicationDbContext db,
    ITokenService tokens,
    IPasswordHasher passwordHasher,
    IAuditTrail audit,
    IStringLocalizer<Messages> localizer,
    TimeProvider time)
    : IRequestHandler<PortalResetPasswordCommand>
{
    public async Task Handle(PortalResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var hash = tokens.HashRefreshToken(request.Token.Trim());
        var account = await db.CustomerAccounts.SingleOrDefaultAsync(a => a.PasswordResetTokenHash == hash, cancellationToken);
        if (account is null || !account.IsValidPasswordReset(hash, now))
        {
            throw ResetPasswordHandler.InvalidToken(localizer);
        }

        account.CompletePasswordReset(passwordHasher.Hash(request.NewPassword), now);
        audit.Record("customers.portal_password_reset", "Customer", account.CustomerId.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class PortalPasswordResetEndpoints : IPublicEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/portal").WithTags("Portal");

        group.MapPost("/forgot-password", async (PortalEmailRequest request, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new PortalForgotPasswordCommand(request.Email), ct);
                return ApiResults.Success(ForgotPasswordEndpoint.SentMessage);
            })
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .WithName("PortalForgotPassword")
            .Produces<ApiResponse<object?>>();

        group.MapPost("/reset-password", async (PortalResetPasswordRequest request, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new PortalResetPasswordCommand(request.Token, request.NewPassword), ct);
                return ApiResults.Success();
            })
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .WithName("PortalResetPassword")
            .Produces<ApiResponse<object?>>();
    }
}
