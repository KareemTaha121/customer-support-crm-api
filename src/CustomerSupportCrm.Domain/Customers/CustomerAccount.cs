using System.Security.Cryptography;
using System.Text;
using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Shared;

namespace CustomerSupportCrm.Domain.Customers;

/// <summary>
/// A customer-portal login. Self-registered accounts stay inactive until the email address is
/// verified, so nobody can claim another person's customer record by email alone.
/// </summary>
public sealed class CustomerAccount : Entity<Guid>, IAuditableEntity
{
    public const int MaxFailedLoginAttempts = 5;
    public const string InvalidCode = "INVALID_ACCOUNT";

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan PasswordResetCooldown = TimeSpan.FromMinutes(1);

    private CustomerAccount()
    {
        Email = string.Empty;
        PasswordHash = string.Empty;
        DisplayName = string.Empty;
    }

    private CustomerAccount(Guid id)
        : base(id)
    {
        Email = string.Empty;
        PasswordHash = string.Empty;
        DisplayName = string.Empty;
    }

    public Guid CustomerId { get; private set; }

    public string Email { get; private set; }

    public string DisplayName { get; private set; }

    public string PasswordHash { get; private set; }

    public bool IsActive { get; private set; }

    public bool EmailVerified { get; private set; }

    /// <summary>SHA-256 of the emailed verification code.</summary>
    public string? VerificationCodeHash { get; private set; }

    public DateTimeOffset? VerificationExpiresAt { get; private set; }

    public int FailedLoginAttempts { get; private set; }

    public DateTimeOffset? LockoutEndsAt { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    /// <summary>SHA-256 of the emailed password reset token; null when no reset is pending.</summary>
    public string? PasswordResetTokenHash { get; private set; }

    public DateTimeOffset? PasswordResetExpiresAt { get; private set; }

    /// <summary>Portal tokens issued before this instant are rejected (see ActivePortalAccountFilter); set by a password reset.</summary>
    public DateTimeOffset? SessionsValidFrom { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    /// <summary>Created by staff for a known customer: active immediately.</summary>
    public static CustomerAccount CreateByStaff(Guid customerId, EmailAddress email, string displayName, string passwordHash)
    {
        var account = New(customerId, email, displayName, passwordHash);
        account.IsActive = true;
        account.EmailVerified = true;
        return account;
    }

    /// <summary>Self-registration: inactive until <see cref="Verify"/> succeeds.</summary>
    public static CustomerAccount Register(Guid customerId, EmailAddress email, string displayName, string passwordHash, string verificationCodeHash, DateTimeOffset now)
    {
        var account = New(customerId, email, displayName, passwordHash);
        account.VerificationCodeHash = verificationCodeHash;
        account.VerificationExpiresAt = now + VerificationLifetime;
        return account;
    }

    /// <summary>Unverified accounts only: new code (and optionally new credentials).</summary>
    public void RestartVerification(string verificationCodeHash, DateTimeOffset now, string? passwordHash = null, string? displayName = null)
    {
        if (EmailVerified)
        {
            throw new DomainException(InvalidCode, "The account is already verified.");
        }

        VerificationCodeHash = verificationCodeHash;
        VerificationExpiresAt = now + VerificationLifetime;
        if (passwordHash is not null)
        {
            PasswordHash = passwordHash;
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            DisplayName = displayName.Trim();
        }
    }

    public static string GenerateVerificationCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    public bool Verify(string codeHash, DateTimeOffset now)
    {
        if (EmailVerified)
        {
            return true;
        }

        if (VerificationCodeHash is null || VerificationExpiresAt < now
            || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(VerificationCodeHash), System.Text.Encoding.UTF8.GetBytes(codeHash)))
        {
            return false;
        }

        EmailVerified = true;
        IsActive = true;
        VerificationCodeHash = null;
        VerificationExpiresAt = null;
        return true;
    }

    public void ChangePasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    private bool CanSignIn => IsActive && EmailVerified;

    /// <summary>False when a reset token was issued less than <see cref="PasswordResetCooldown"/> ago (stops email flooding).</summary>
    public bool CanRequestPasswordReset(DateTimeOffset now) =>
        PasswordResetExpiresAt is not { } expires || expires - PasswordResetLifetime + PasswordResetCooldown <= now;

    /// <summary>Stores the hash of a new reset token; it replaces any pending one, so only the latest link works.</summary>
    public void StartPasswordReset(string tokenHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        PasswordResetTokenHash = tokenHash;
        PasswordResetExpiresAt = now + PasswordResetLifetime;
    }

    /// <summary>True when <paramref name="tokenHash"/> is the pending, unexpired reset token of an account that may sign in.</summary>
    public bool IsValidPasswordReset(string tokenHash, DateTimeOffset now) =>
        CanSignIn && PasswordResetTokenHash is not null && PasswordResetExpiresAt > now
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(PasswordResetTokenHash), Encoding.UTF8.GetBytes(tokenHash));

    /// <summary>
    /// Single use: sets the new password, clears the token and the lockout, and ends older portal
    /// sessions. JWT <c>iat</c> is in whole seconds, so the cut-off is truncated to the second.
    /// Check <see cref="IsValidPasswordReset"/> first.
    /// </summary>
    public void CompletePasswordReset(string passwordHash, DateTimeOffset now)
    {
        ChangePasswordHash(passwordHash);
        PasswordResetTokenHash = null;
        PasswordResetExpiresAt = null;
        FailedLoginAttempts = 0;
        LockoutEndsAt = null;
        SessionsValidFrom = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds());
    }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndsAt > now;

    public void RecordFailedLogin(DateTimeOffset now)
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= MaxFailedLoginAttempts)
        {
            LockoutEndsAt = now + LockoutDuration;
            FailedLoginAttempts = 0;
        }
    }

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginAttempts = 0;
        LockoutEndsAt = null;
        LastLoginAt = now;
    }

    public void SetActive(bool active) => IsActive = active && EmailVerified;

    /// <summary>
    /// Staff grant access again to a revoked account: like <see cref="CreateByStaff"/>, staff vouch
    /// for the email, so it becomes verified and active with the new password and no lockout.
    /// </summary>
    public void ReactivateByStaff(string passwordHash)
    {
        ChangePasswordHash(passwordHash);
        EmailVerified = true;
        IsActive = true;
        VerificationCodeHash = null;
        VerificationExpiresAt = null;
        FailedLoginAttempts = 0;
        LockoutEndsAt = null;
    }

    /// <summary>Keeps the portal name in step with the customer record (the profile and token read it).</summary>
    public void Rename(string displayName) => DisplayName = ValidName(displayName);

    private static CustomerAccount New(Guid customerId, EmailAddress email, string displayName, string passwordHash)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        return new CustomerAccount(Guid.CreateVersion7())
        {
            CustomerId = customerId,
            Email = email.Value,
            DisplayName = ValidName(displayName),
            PasswordHash = passwordHash,
        };
    }

    private static string ValidName(string displayName)
    {
        var name = displayName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > Customer.NameMaxLength)
        {
            throw new DomainException(InvalidCode, "The name is not valid.");
        }

        return name;
    }
}
