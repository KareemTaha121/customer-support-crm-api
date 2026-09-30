using CustomerSupportCrm.Domain.Users;

namespace CustomerSupportCrm.Application.Abstractions.Authentication;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <param name="Token">The opaque value handed to the client. Never stored or logged.</param>
/// <param name="Hash">The value persisted server-side.</param>
public sealed record GeneratedRefreshToken(string Token, string Hash);

public interface ITokenService
{
    TimeSpan RefreshTokenLifetime { get; }

    AccessToken CreateAccessToken(User user, Guid sessionId, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions);

    AccessToken CreateCustomerAccessToken(Guid accountId, Guid customerId, string email, string name);

    GeneratedRefreshToken GenerateRefreshToken();

    string HashRefreshToken(string token);
}
