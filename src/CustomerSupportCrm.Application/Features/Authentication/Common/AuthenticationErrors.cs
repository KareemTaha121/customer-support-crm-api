namespace CustomerSupportCrm.Application.Features.Authentication.Common;

public static class AuthenticationErrors
{
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string AccountLocked = "ACCOUNT_LOCKED";
    public const string AccountDisabled = "ACCOUNT_DISABLED";
    public const string InvalidRefreshToken = "INVALID_REFRESH_TOKEN";
    public const string CsrfValidationFailed = "CSRF_VALIDATION_FAILED";
    public const string InvalidCurrentPassword = "INVALID_CURRENT_PASSWORD";
    public const string InvalidResetToken = "INVALID_RESET_TOKEN";
}
