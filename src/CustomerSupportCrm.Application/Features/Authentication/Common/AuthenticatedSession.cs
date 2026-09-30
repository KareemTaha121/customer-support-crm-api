using CustomerSupportCrm.Contracts.Authentication;

namespace CustomerSupportCrm.Application.Features.Authentication.Common;

/// <summary>
/// Result of login/refresh. The endpoint returns <see cref="Response"/> in the body and puts
/// <see cref="RefreshToken"/> in an HttpOnly cookie.
/// </summary>
public sealed record AuthenticatedSession(AccessTokenResponse Response, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);
