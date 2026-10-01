namespace CustomerSupportCrm.Domain.Audit;

/// <summary>Stable audit action names. Never rename an existing value.</summary>
public static class AuditActions
{
    public const string LoginSucceeded = "auth.login.succeeded";
    public const string LoginFailed = "auth.login.failed";
    public const string LoginLockedOut = "auth.login.locked_out";
    public const string Logout = "auth.logout";
    public const string RefreshTokenReuseDetected = "auth.refresh.reuse_detected";
    public const string PasswordChanged = "auth.password.changed";
    public const string PasswordResetRequested = "auth.password.reset_requested";
    public const string PasswordReset = "auth.password.reset";

    public const string UserCreated = "users.created";
    public const string UserRolesChanged = "users.roles_changed";
    public const string UserDisabled = "users.disabled";
    public const string UserEnabled = "users.enabled";

    public const string RoleCreated = "roles.created";
    public const string RoleUpdated = "roles.updated";
    public const string RoleDeleted = "roles.deleted";
}

public static class AuditEntityTypes
{
    public const string User = "User";
    public const string Role = "Role";
}
