namespace CustomerSupportCrm.Api.Middleware;

/// <summary>
/// Adds browser hardening headers to every API response. The API only serves JSON and
/// file downloads, so the CSP forbids everything. The Swagger UI (Development only) needs
/// scripts and styles and is skipped.
/// </summary>
public sealed class SecureHeadersMiddleware(RequestDelegate next)
{
    private const string SwaggerPath = "/swagger";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var isSwagger = context.Request.Path.StartsWithSegments(SwaggerPath, StringComparison.OrdinalIgnoreCase);

        // OnStarting survives the exception handler clearing the response headers.
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            if (!isSwagger)
            {
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
