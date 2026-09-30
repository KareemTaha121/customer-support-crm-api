using System.Security.Cryptography;
using System.Text;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Authorization;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Common.Validation;
using CustomerSupportCrm.Application.Features.Channels;
using CustomerSupportCrm.Application.Features.Customers.Common;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Contracts.Portal;
using CustomerSupportCrm.Domain.Customers;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Shared;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.CustomerPortal;

public static class PortalErrors
{
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string AccountLocked = "ACCOUNT_LOCKED";
    public const string NotVerified = "EMAIL_NOT_VERIFIED";
    public const string InvalidCode = "INVALID_VERIFICATION_CODE";
    public const string AccountExists = "PORTAL_ACCOUNT_EXISTS";
}

internal static class PortalSessions
{
    public static string HashCode(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim())));

    public static async Task<PortalProfileResponse> ProfileAsync(IApplicationDbContext db, Guid accountId, CancellationToken cancellationToken) =>
        await db.CustomerAccounts.AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new PortalProfileResponse(
                a.Id,
                a.CustomerId,
                db.Customers.Where(c => c.Id == a.CustomerId).Select(c => c.Number).First(),
                a.DisplayName,
                a.Email,
                db.Customers.Where(c => c.Id == a.CustomerId).Select(c => c.PreferredLanguage).First()))
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new UnauthorizedException();

    public static async Task<PortalSessionResponse> IssueAsync(IApplicationDbContext db, ITokenService tokens, CustomerAccount account, CancellationToken cancellationToken)
    {
        var profile = await ProfileAsync(db, account.Id, cancellationToken);
        var token = tokens.CreateCustomerAccessToken(account.Id, account.CustomerId, account.Email, account.DisplayName);
        return new PortalSessionResponse(token.Token, token.ExpiresAt, profile);
    }
}

// ---------- Registration & sign-in (anonymous) ----------

public sealed record PortalRegisterCommand(PortalRegisterRequest Request) : IRequest;

internal sealed class PortalRegisterValidator : AbstractValidator<PortalRegisterCommand>
{
    public PortalRegisterValidator()
    {
        RuleFor(c => c.Request.Name).NotEmpty().MaximumLength(Customer.NameMaxLength);
        RuleFor(c => c.Request.Email).NotEmpty().EmailAddress().MaximumLength(EmailAddress.MaxLength);
        RuleFor(c => c.Request.Password).ValidPassword();
        RuleFor(c => c.Request.Phone).MaximumLength(32);
        RuleFor(c => c.Request.Language).Must(l => l is null or "en" or "ar").WithErrorCode(ErrorCodes.InvalidValue);
    }
}

/// <summary>
/// Always answers the same way, whether or not the email is known, so registration cannot be
/// used to discover customers. The account activates only after the emailed code is entered.
/// </summary>
internal sealed class PortalRegisterHandler(
    IApplicationDbContext db,
    CustomerResolver customers,
    IPasswordHasher passwordHasher,
    CustomerMessenger messenger,
    TimeProvider time)
    : IRequestHandler<PortalRegisterCommand>
{
    public async Task Handle(PortalRegisterCommand request, CancellationToken cancellationToken)
    {
        var input = request.Request;
        var email = EmailAddress.Create(input.Email);
        var language = input.Language ?? "en";
        var code = CustomerAccount.GenerateVerificationCode();

        var existing = await db.CustomerAccounts.SingleOrDefaultAsync(a => a.Email == email.Value, cancellationToken);
        if (existing is not null)
        {
            if (!existing.EmailVerified)
            {
                // Re-registering an unverified account restarts verification with the new password.
                existing.RestartVerification(PortalSessions.HashCode(code), time.GetUtcNow(), passwordHasher.Hash(input.Password), input.Name);
                await messenger.QueueVerificationCodeAsync(email.Value, language, code, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        var customer = await customers.ResolveAsync(ContactType.Email, email.Value, input.Name, null, cancellationToken);
        if (!string.IsNullOrWhiteSpace(input.Phone) && PhoneNumber.TryCreate(input.Phone, out var phone)
            && !customer.Contacts.Any(c => c.Value == phone!.Value))
        {
            customer.AddContact(ContactType.Phone, phone!.Value, "portal", isPrimary: false);
        }

        db.CustomerAccounts.Add(CustomerAccount.Register(customer.Id, email, input.Name, passwordHasher.Hash(input.Password), PortalSessions.HashCode(code), time.GetUtcNow()));
        await messenger.QueueVerificationCodeAsync(email.Value, language, code, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record PortalVerifyCommand(string Email, string Code) : IRequest<PortalSessionResponse>;

internal sealed class PortalVerifyHandler(IApplicationDbContext db, ITokenService tokens, TimeProvider time) : IRequestHandler<PortalVerifyCommand, PortalSessionResponse>
{
    public async Task<PortalSessionResponse> Handle(PortalVerifyCommand request, CancellationToken cancellationToken)
    {
        var email = EmailAddress.Normalize(request.Email);
        var account = await db.CustomerAccounts.SingleOrDefaultAsync(a => a.Email == email, cancellationToken);
        if (account is null || !account.Verify(PortalSessions.HashCode(request.Code ?? string.Empty), time.GetUtcNow()))
        {
            throw new ValidationException([new ValidationFailure("Code", "The verification code is invalid or has expired.") { ErrorCode = PortalErrors.InvalidCode }]);
        }

        account.RecordSuccessfulLogin(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return await PortalSessions.IssueAsync(db, tokens, account, cancellationToken);
    }
}

public sealed record PortalResendVerificationCommand(string Email) : IRequest;

internal sealed class PortalResendVerificationHandler(IApplicationDbContext db, CustomerMessenger messenger, TimeProvider time)
    : IRequestHandler<PortalResendVerificationCommand>
{
    public async Task Handle(PortalResendVerificationCommand request, CancellationToken cancellationToken)
    {
        var email = EmailAddress.Normalize(request.Email);
        var account = await db.CustomerAccounts.SingleOrDefaultAsync(a => a.Email == email && !a.EmailVerified, cancellationToken);
        if (account is null)
        {
            return;
        }

        var code = CustomerAccount.GenerateVerificationCode();
        account.RestartVerification(PortalSessions.HashCode(code), time.GetUtcNow());
        var language = await db.Customers.Where(c => c.Id == account.CustomerId).Select(c => c.PreferredLanguage).FirstOrDefaultAsync(cancellationToken) ?? "en";
        await messenger.QueueVerificationCodeAsync(account.Email, language, code, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record PortalLoginCommand(string Email, string Password) : IRequest<PortalSessionResponse>;

internal sealed class PortalLoginValidator : AbstractValidator<PortalLoginCommand>
{
    public PortalLoginValidator()
    {
        RuleFor(c => c.Email).NotEmpty().MaximumLength(EmailAddress.MaxLength);
        RuleFor(c => c.Password).NotEmpty().MaximumLength(CommonRules.PasswordMaxLength);
    }
}

internal sealed class PortalLoginHandler(IApplicationDbContext db, IPasswordHasher passwordHasher, ITokenService tokens, CustomerTimeline timeline, TimeProvider time)
    : IRequestHandler<PortalLoginCommand, PortalSessionResponse>
{
    private static string? _timingEqualizerHash;

    public async Task<PortalSessionResponse> Handle(PortalLoginCommand request, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var email = EmailAddress.Normalize(request.Email);
        var account = await db.CustomerAccounts.SingleOrDefaultAsync(a => a.Email == email, cancellationToken);

        if (account is null)
        {
            passwordHasher.Verify(_timingEqualizerHash ??= passwordHasher.Hash(Guid.NewGuid().ToString()), request.Password);
            throw Invalid();
        }

        if (account.IsLockedOut(now))
        {
            throw new UnauthorizedException(PortalErrors.AccountLocked, "The account is temporarily locked. Try again later.");
        }

        var result = passwordHasher.Verify(account.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            account.RecordFailedLogin(now);
            await db.SaveChangesAsync(cancellationToken);
            throw Invalid();
        }

        if (!account.EmailVerified)
        {
            throw new UnauthorizedException(PortalErrors.NotVerified, "Please verify your email address first.");
        }

        if (!account.IsActive)
        {
            throw Invalid();
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.ChangePasswordHash(passwordHasher.Hash(request.Password));
        }

        account.RecordSuccessfulLogin(now);
        timeline.Record(account.CustomerId, CustomerActivityTypes.PortalSignIn, "Signed in to the customer portal");
        await db.SaveChangesAsync(cancellationToken);
        return await PortalSessions.IssueAsync(db, tokens, account, cancellationToken);
    }

    private static UnauthorizedException Invalid() => new(PortalErrors.InvalidCredentials, "The email or password is incorrect.");
}

internal sealed class PortalAuthEndpoints : IPublicEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/portal").WithTags("Portal");

        group.MapPost("/register", async (PortalRegisterRequest request, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new PortalRegisterCommand(request), ct);
                return ApiResults.Success("If the address can be registered, a verification code has been sent.");
            })
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .WithName("PortalRegister")
            .Produces<ApiResponse<object?>>();

        group.MapPost("/verify", async (PortalVerifyRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new PortalVerifyCommand(request.Email, request.Code), ct)))
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .WithName("PortalVerify")
            .Produces<ApiResponse<PortalSessionResponse>>();

        group.MapPost("/resend-verification", async (PortalEmailRequest request, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new PortalResendVerificationCommand(request.Email), ct);
                return ApiResults.Success();
            })
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .WithName("PortalResendVerification")
            .Produces<ApiResponse<object?>>();

        group.MapPost("/login", async (PortalLoginRequest request, ISender sender, CancellationToken ct) =>
                ApiResults.Ok(await sender.Send(new PortalLoginCommand(request.Email, request.Password), ct)))
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .WithName("PortalLogin")
            .Produces<ApiResponse<PortalSessionResponse>>();
    }
}

// ---------- Profile (signed-in customer) ----------

internal sealed class PortalProfileEndpoints : IPortalEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/me", async (IApplicationDbContext db, ICurrentCustomer customer, CancellationToken ct) =>
                ApiResults.Ok(await PortalSessions.ProfileAsync(db, customer.AccountId, ct)))
            .WithName("PortalGetProfile")
            .Produces<ApiResponse<PortalProfileResponse>>();

        app.MapPut("/me", async (PortalUpdateProfileRequest request, IApplicationDbContext db, ICurrentCustomer customer, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > Customer.NameMaxLength || request.Language is not ("en" or "ar"))
                {
                    throw new ValidationException([new ValidationFailure("Name", "The name or language is not valid.") { ErrorCode = ErrorCodes.Invalid }]);
                }

                var record = await db.Customers.SingleAsync(c => c.Id == customer.CustomerId, ct);
                record.UpdateProfile(record.Type, request.Name, record.CompanyName, request.Language, record.Tags);
                await db.SaveChangesAsync(ct);
                return ApiResults.Ok(await PortalSessions.ProfileAsync(db, customer.AccountId, ct));
            })
            .WithName("PortalUpdateProfile")
            .Produces<ApiResponse<PortalProfileResponse>>();

        app.MapPost("/me/change-password", async (PortalChangePasswordRequest request, IApplicationDbContext db, ICurrentCustomer customer, IPasswordHasher hasher, CancellationToken ct) =>
            {
                if (request.NewPassword is not { Length: >= CommonRules.PasswordMinLength and <= CommonRules.PasswordMaxLength })
                {
                    throw new ValidationException([new ValidationFailure("NewPassword", "The new password must be 12–128 characters.") { ErrorCode = ErrorCodes.InvalidLength }]);
                }

                var account = await db.CustomerAccounts.SingleAsync(a => a.Id == customer.AccountId, ct);
                if (hasher.Verify(account.PasswordHash, request.CurrentPassword ?? string.Empty) == PasswordVerificationResult.Failed)
                {
                    throw new ValidationException([new ValidationFailure("CurrentPassword", "The current password is incorrect.") { ErrorCode = "INVALID_CURRENT_PASSWORD" }]);
                }

                account.ChangePasswordHash(hasher.Hash(request.NewPassword));
                await db.SaveChangesAsync(ct);
                return ApiResults.Success();
            })
            .RequireRateLimiting(RateLimitPolicies.Authentication)
            .WithName("PortalChangePassword")
            .Produces<ApiResponse<object?>>();
    }
}

// ---------- Staff: grant/revoke portal access ----------

public sealed record GrantPortalAccessCommand(Guid CustomerId, string Email, string Password) : IRequest;

internal sealed class GrantPortalAccessValidator : AbstractValidator<GrantPortalAccessCommand>
{
    public GrantPortalAccessValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(EmailAddress.MaxLength);
        RuleFor(c => c.Password).ValidPassword();
    }
}

internal sealed class GrantPortalAccessHandler(IApplicationDbContext db, IAccessScopeProvider scopes, IPasswordHasher hasher, Abstractions.Auditing.IAuditTrail audit)
    : IRequestHandler<GrantPortalAccessCommand>
{
    public async Task Handle(GrantPortalAccessCommand request, CancellationToken cancellationToken)
    {
        var customer = await CustomerQueries.LoadAsync(db, await scopes.GetAsync(cancellationToken), request.CustomerId, cancellationToken);
        var email = EmailAddress.Create(request.Email);
        if (await db.CustomerAccounts.AnyAsync(a => a.Email == email.Value, cancellationToken))
        {
            throw new ConflictException(PortalErrors.AccountExists, "A portal account with this email already exists.");
        }

        db.CustomerAccounts.Add(CustomerAccount.CreateByStaff(customer.Id, email, customer.Name, hasher.Hash(request.Password)));
        if (!customer.Contacts.Any(c => c.Type == ContactType.Email && c.Value == email.Value))
        {
            customer.AddContact(ContactType.Email, email.Value, "portal", isPrimary: false);
        }

        audit.Record("customers.portal_access_granted", "Customer", customer.Id.ToString(), newValues: new { email = email.Value });
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record RevokePortalAccessCommand(Guid CustomerId) : IRequest;

internal sealed class RevokePortalAccessHandler(IApplicationDbContext db, IAccessScopeProvider scopes, Abstractions.Auditing.IAuditTrail audit) : IRequestHandler<RevokePortalAccessCommand>
{
    public async Task Handle(RevokePortalAccessCommand request, CancellationToken cancellationToken)
    {
        await CustomerQueries.EnsureAccessibleAsync(db, await scopes.GetAsync(cancellationToken), request.CustomerId, cancellationToken);
        foreach (var account in await db.CustomerAccounts.Where(a => a.CustomerId == request.CustomerId).ToListAsync(cancellationToken))
        {
            account.SetActive(false);
        }

        audit.Record("customers.portal_access_revoked", "Customer", request.CustomerId.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class PortalAccessEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/customers/{id:guid}/portal-access", async (Guid id, GrantPortalAccessRequest request, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new GrantPortalAccessCommand(id, request.Email, request.Password), ct);
                return ApiResults.Success();
            })
            .RequireAuthorization(Permissions.CustomersUpdate)
            .WithTags("Customers")
            .WithName("GrantPortalAccess")
            .Produces<ApiResponse<object?>>();

        app.MapDelete("/customers/{id:guid}/portal-access", async (Guid id, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new RevokePortalAccessCommand(id), ct);
                return ApiResults.Success();
            })
            .RequireAuthorization(Permissions.CustomersUpdate)
            .WithTags("Customers")
            .WithName("RevokePortalAccess")
            .Produces<ApiResponse<object?>>();
    }
}
