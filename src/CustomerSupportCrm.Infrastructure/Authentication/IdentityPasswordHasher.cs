using CustomerSupportCrm.Application.Abstractions.Authentication;
using Microsoft.AspNetCore.Identity;
using IdentityResult = Microsoft.AspNetCore.Identity.PasswordVerificationResult;
using PasswordVerificationResult = CustomerSupportCrm.Application.Abstractions.Authentication.PasswordVerificationResult;

namespace CustomerSupportCrm.Infrastructure.Authentication;

/// <summary>
/// ASP.NET Core Identity's password hasher (PBKDF2-HMAC-SHA512, salted, versioned format).
/// Only the hasher is used, not the Identity user store.
/// </summary>
internal sealed class IdentityPasswordHasher : IPasswordHasher
{
    private static readonly object Unused = new();

    private readonly PasswordHasher<object> _inner = new();

    public string Hash(string password) => _inner.HashPassword(Unused, password);

    public PasswordVerificationResult Verify(string passwordHash, string providedPassword) =>
        _inner.VerifyHashedPassword(Unused, passwordHash, providedPassword) switch
        {
            IdentityResult.Success => PasswordVerificationResult.Success,
            IdentityResult.SuccessRehashNeeded => PasswordVerificationResult.SuccessRehashNeeded,
            _ => PasswordVerificationResult.Failed,
        };
}
