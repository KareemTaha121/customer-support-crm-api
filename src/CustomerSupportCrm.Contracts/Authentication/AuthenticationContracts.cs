namespace CustomerSupportCrm.Contracts.Authentication;

public sealed record LoginRequest(string Email, string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>
/// Returned by login and refresh. The refresh token is never in the body; it is set as an
/// HttpOnly cookie scoped to the auth endpoints.
/// </summary>
public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAt, CurrentUserResponse User);

public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);
