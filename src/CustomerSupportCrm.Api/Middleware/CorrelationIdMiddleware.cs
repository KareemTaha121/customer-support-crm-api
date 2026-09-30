using System.Text.RegularExpressions;
using CustomerSupportCrm.Application.Abstractions.Http;
using Serilog.Context;

namespace CustomerSupportCrm.Api.Middleware;

/// <summary>
/// Accepts a well-formed incoming X-Correlation-Id or generates one, then exposes it to
/// the log context, the response header, and the response body.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = Resolve(context.Request.Headers[CorrelationIdHttpContextExtensions.HeaderName].ToString());
        context.SetCorrelationId(correlationId);

        // OnStarting survives the exception handler clearing the response headers.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationIdHttpContextExtensions.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static string Resolve(string incoming) =>
        AllowedFormat().IsMatch(incoming) ? incoming : Guid.NewGuid().ToString("N");

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex AllowedFormat();
}
