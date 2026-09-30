namespace CustomerSupportCrm.Application.Abstractions.Authorization;

/// <summary>
/// Authorization policies beyond the one-policy-per-permission set (each permission code in
/// <see cref="Domain.Roles.Permissions"/> is also a policy name requiring that permission).
/// </summary>
public static class PolicyNames
{
    /// <summary>Read roles: needed both to manage roles and to assign them to users.</summary>
    public const string RolesRead = "policy:roles.read";
}
