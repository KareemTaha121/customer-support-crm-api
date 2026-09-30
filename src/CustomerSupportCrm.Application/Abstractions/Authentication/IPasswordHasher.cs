namespace CustomerSupportCrm.Application.Abstractions.Authentication;

public enum PasswordVerificationResult
{
    Failed,
    Success,

    /// <summary>Correct, but hashed with outdated parameters; the caller should rehash.</summary>
    SuccessRehashNeeded,
}

public interface IPasswordHasher
{
    string Hash(string password);

    PasswordVerificationResult Verify(string passwordHash, string providedPassword);
}
