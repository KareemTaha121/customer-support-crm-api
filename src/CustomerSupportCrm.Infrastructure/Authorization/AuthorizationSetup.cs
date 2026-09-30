using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Authorization;
using CustomerSupportCrm.Domain.Roles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportCrm.Infrastructure.Authorization;

internal static class AuthorizationSetup
{
    /// <summary>
    /// Actor policies (staff / customer), one policy per permission code (policy name = code)
    /// and composite policies. Permissions come from the access token's "permission" claims;
    /// customer tokens never carry them.
    /// </summary>
    public static AuthorizationBuilder AddPermissionPolicies(this IServiceCollection services)
    {
        var builder = services.AddAuthorizationBuilder();

        builder.AddPolicy(PolicyNames.Staff, policy => policy.RequireStaff());
        builder.AddPolicy(PolicyNames.Customer, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(CrmClaimTypes.Actor, ActorTypes.Customer));

        foreach (var permission in Permissions.All)
        {
            builder.AddPolicy(permission, policy => policy.RequireStaff().RequireClaim(CrmClaimTypes.Permission, permission));
        }

        builder.AddPolicy(PolicyNames.RolesRead, policy => policy
            .RequireStaff()
            .RequireClaim(CrmClaimTypes.Permission, Permissions.RolesManage, Permissions.UsersManage));

        return builder;
    }

    private static AuthorizationPolicyBuilder RequireStaff(this AuthorizationPolicyBuilder policy) =>
        policy.RequireAuthenticatedUser().RequireClaim(CrmClaimTypes.Actor, ActorTypes.Staff);
}
