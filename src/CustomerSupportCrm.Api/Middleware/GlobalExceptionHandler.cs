using System.Text.Json;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Domain.Common;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CustomerSupportCrm.Api.Middleware;

/// <summary>
/// Maps exceptions to stable error codes. Exception details are logged, never returned.
/// </summary>
internal sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            LogRequestAborted(logger);
            return true;
        }

        var (statusCode, category, errors) = exception switch
        {
            ValidationException validation =>
                (StatusCodes.Status400BadRequest, ErrorCodes.ValidationError, validation.Errors.Select(ToApiError).ToList()),
            AppException app =>
                (StatusFor(app), ErrorResponseWriter.CategoryFor(StatusFor(app)), Single(httpContext, app.Code, app.Message)),
            DomainException domain =>
                (StatusCodes.Status422UnprocessableEntity, ErrorCodes.BusinessRuleViolation, Single(httpContext, domain.Code, domain.Message)),
            // Concurrent edit, or a unique index beaten by a parallel request after the handler's check.
            DbUpdateConcurrencyException or DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                (StatusCodes.Status409Conflict, ErrorCodes.Conflict, null),
            BadHttpRequestException badRequest =>
                (badRequest.StatusCode, ErrorResponseWriter.CategoryFor(badRequest.StatusCode), null),
            _ =>
                (StatusCodes.Status500InternalServerError, ErrorCodes.InternalError, (List<ApiError>?)null),
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandledException(logger, exception);
        }
        else
        {
            LogHandledException(logger, category);
        }

        await ErrorResponseWriter.WriteAsync(httpContext, statusCode, category, errors);
        return true;
    }

    private static List<ApiError> Single(HttpContext context, string code, string fallbackMessage) =>
        [new ApiError(code, ErrorResponseWriter.Localize(context, code, fallbackMessage))];

    private static int StatusFor(AppException exception) => exception switch
    {
        UnauthorizedException => StatusCodes.Status401Unauthorized,
        NotFoundException => StatusCodes.Status404NotFound,
        ConflictException => StatusCodes.Status409Conflict,
        ForbiddenException => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status400BadRequest,
    };

    private static ApiError ToApiError(ValidationFailure failure) =>
        new(ToValidationCode(failure.ErrorCode), failure.ErrorMessage, ToFieldPath(failure.PropertyName));

    private static string ToValidationCode(string? fluentValidationCode) => fluentValidationCode switch
    {
        "NotEmptyValidator" or "NotNullValidator" => ErrorCodes.Required,
        "MaximumLengthValidator" or "MinimumLengthValidator" or "LengthValidator" or "ExactLengthValidator" => ErrorCodes.InvalidLength,
        "EmailValidator" => ErrorCodes.InvalidEmail,
        "RegularExpressionValidator" => ErrorCodes.InvalidFormat,
        "GreaterThanValidator" or "GreaterThanOrEqualValidator" or "LessThanValidator" or "LessThanOrEqualValidator"
            or "InclusiveBetweenValidator" or "ExclusiveBetweenValidator" => ErrorCodes.OutOfRange,
        "EnumValidator" or "StringEnumValidator" => ErrorCodes.InvalidValue,
        // Validators may set a stable UPPER_SNAKE_CASE code via WithErrorCode(...).
        { } code when IsStableCode(code) => code,
        _ => ErrorCodes.Invalid,
    };

    private static bool IsStableCode(string code) =>
        code.Length > 0 && code.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_');

    private static string? ToFieldPath(string? propertyName) =>
        string.IsNullOrEmpty(propertyName)
            ? null
            : string.Join('.', propertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));

    [LoggerMessage(Level = LogLevel.Information, Message = "Request aborted by the client")]
    private static partial void LogRequestAborted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Request failed with {ErrorCode}")]
    private static partial void LogHandledException(ILogger logger, string errorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception);
}
