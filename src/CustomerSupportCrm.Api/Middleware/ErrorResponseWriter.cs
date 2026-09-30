using System.Globalization;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Resources;
using CustomerSupportCrm.Contracts.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Localization;

namespace CustomerSupportCrm.Api.Middleware;

/// <summary>
/// The single place error response bodies are produced, always in the standard envelope.
/// </summary>
internal static class ErrorResponseWriter
{
    public static Task WriteAsync(HttpContext context, int statusCode, string categoryCode, IReadOnlyList<ApiError>? errors = null)
    {
        var message = Localize(context, categoryCode, categoryCode);
        var body = ApiResponse.Failure(message, errors ?? [new ApiError(categoryCode, message)]) with
        {
            CorrelationId = context.GetCorrelationId(),
        };

        context.Response.StatusCode = statusCode;
        // The exception handler clears headers set earlier by request localization.
        context.Response.Headers.ContentLanguage = CultureInfo.CurrentUICulture.Name;
        return context.Response.WriteAsJsonAsync(body, context.RequestAborted);
    }

    /// <summary>Envelope for framework responses without a body (unmatched route, 405, auth challenges).</summary>
    public static Task WriteStatusCodePageAsync(StatusCodeContext statusCodeContext)
    {
        var context = statusCodeContext.HttpContext;
        var statusCode = context.Response.StatusCode;
        return WriteAsync(context, statusCode, CategoryFor(statusCode));
    }

    public static string Localize(HttpContext context, string code, string fallback)
    {
        var localizer = context.RequestServices.GetRequiredService<IStringLocalizer<Messages>>();
        var localized = localizer[code];
        return localized.ResourceNotFound ? fallback : localized.Value;
    }

    public static string CategoryFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => ErrorCodes.BadRequest,
        StatusCodes.Status401Unauthorized => ErrorCodes.Unauthorized,
        StatusCodes.Status403Forbidden => ErrorCodes.Forbidden,
        StatusCodes.Status404NotFound => ErrorCodes.NotFound,
        StatusCodes.Status405MethodNotAllowed => ErrorCodes.MethodNotAllowed,
        StatusCodes.Status409Conflict => ErrorCodes.Conflict,
        StatusCodes.Status413PayloadTooLarge => ErrorCodes.PayloadTooLarge,
        StatusCodes.Status415UnsupportedMediaType => ErrorCodes.UnsupportedMediaType,
        StatusCodes.Status422UnprocessableEntity => ErrorCodes.BusinessRuleViolation,
        StatusCodes.Status429TooManyRequests => ErrorCodes.RateLimited,
        StatusCodes.Status503ServiceUnavailable => ErrorCodes.ServiceUnavailable,
        < 500 => ErrorCodes.BadRequest,
        _ => ErrorCodes.InternalError,
    };
}
