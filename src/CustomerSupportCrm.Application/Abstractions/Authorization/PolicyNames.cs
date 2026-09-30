namespace CustomerSupportCrm.Application.Abstractions.Authorization;

/// <summary>
/// Authorization policies beyond the one-policy-per-permission set (each permission code in
/// <see cref="Domain.Roles.Permissions"/> is also a policy name requiring that permission).
/// </summary>
public static class PolicyNames
{
    /// <summary>An authenticated staff user. Default for every /api/v1 endpoint.</summary>
    public const string Staff = "policy:staff";

    /// <summary>An authenticated customer-portal user. Default for /api/v1/portal.</summary>
    public const string Customer = "policy:customer";

    /// <summary>An external system calling with an API key. Default for /api/v1/external.</summary>
    public const string ApiClient = "policy:api-client";

    /// <summary>External API scope policy (API keys), e.g. policy:scope:customers:write.</summary>
    public static string ForScope(string scope) => $"policy:scope:{scope}";

    /// <summary>Read roles: needed both to manage roles and to assign them to users.</summary>
    public const string RolesRead = "policy:roles.read";
}
