using CustomerSupportCrm.Application.Common.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace CustomerSupportCrm.Application.Features.Authentication.Common;

/// <summary>
/// HTTP details shared by the auth endpoints: the refresh-token cookie and the CSRF header
/// required on the endpoints that the cookie authenticates.
/// </summary>
public static class AuthenticationHttp
{
    public const string RoutePrefix = "/auth";
    public const string RefreshCookieName = "crm_refresh";
    public const string CsrfHeaderName = "X-CSRF-Protection";

    /// <summary>Scoped to the auth routes so the cookie is never sent to other endpoints.</summary>
    public const string RefreshCookiePath = "/api/v1/auth";

    public static void AppendRefreshCookie(HttpResponse response, AuthenticatedSession session)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(session);

        response.Cookies.Append(RefreshCookieName, session.RefreshToken, CreateCookieOptions(session.RefreshTokenExpiresAt));
    }

    public static void DeleteRefreshCookie(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Delete(RefreshCookieName, CreateCookieOptions(expires: null));
    }

    public static string? ReadRefreshCookie(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Cookies.TryGetValue(RefreshCookieName, out var value) ? value : null;
    }

    /// <summary>
    /// Cookie-authenticated endpoints require a custom header. Cross-site forms cannot set one,
    /// and cross-origin scripts need a CORS preflight that only allowed origins pass.
    /// </summary>
    public static RouteHandlerBuilder RequireCsrfHeader(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter((context, next) =>
        {
            if (!context.HttpContext.Request.Headers.ContainsKey(CsrfHeaderName))
            {
                throw new ForbiddenException(AuthenticationErrors.CsrfValidationFailed, "The CSRF protection header is missing.");
            }

            return next(context);
        });

    private static CookieOptions CreateCookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = RefreshCookiePath,
        Expires = expires,
        IsEssential = true,
    };
}
