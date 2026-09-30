namespace CustomerSupportCrm.Contracts.Common;

/// <summary>
/// Stable error codes shared with API clients. Never change an existing value.
/// Feature-specific codes (e.g. TICKET_NOT_FOUND) live with their feature.
/// </summary>
public static class ErrorCodes
{
    // Response-level categories.
    public const string BadRequest = "BAD_REQUEST";
    public const string ValidationError = "VALIDATION_ERROR";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string Conflict = "CONFLICT";
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";
    public const string BusinessRuleViolation = "BUSINESS_RULE_VIOLATION";
    public const string RateLimited = "RATE_LIMITED";
    public const string InternalError = "INTERNAL_ERROR";
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";

    // Field-level validation codes.
    public const string Required = "REQUIRED";
    public const string InvalidLength = "INVALID_LENGTH";
    public const string InvalidFormat = "INVALID_FORMAT";
    public const string InvalidEmail = "INVALID_EMAIL";
    public const string OutOfRange = "OUT_OF_RANGE";
    public const string InvalidValue = "INVALID_VALUE";
    public const string Invalid = "INVALID";
}
