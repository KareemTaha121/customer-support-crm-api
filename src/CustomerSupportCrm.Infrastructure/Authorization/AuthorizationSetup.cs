using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Authorization;
using CustomerSupportCrm.Domain.Roles;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportCrm.Infrastructure.Authorization;

internal static class AuthorizationSetup
{
    /// <summary>
    /// One policy per permission code (policy name = code), plus composite policies.
    /// Permissions come from the access token's "permission" claims.
    /// </summary>
    public static void AddPermissionPolicies(this IServiceCollection services)
    {
        var builder = services.AddAuthorizationBuilder();

        foreach (var permission in Permissions.All)
        {
            builder.AddPolicy(permission, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(CrmClaimTypes.Permission, permission));
        }

        builder.AddPolicy(PolicyNames.RolesRead, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(CrmClaimTypes.Permission, Permissions.RolesManage, Permissions.UsersManage));
    }
}
