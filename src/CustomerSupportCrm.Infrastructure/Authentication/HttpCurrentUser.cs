using System.Globalization;
using System.Security.Claims;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Domain.Users;
using Microsoft.AspNetCore.Http;

namespace CustomerSupportCrm.Infrastructure.Authentication;

/// <summary>Adapts the validated JWT principal of the current request to <see cref="ICurrentUser"/>.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated =>
        Principal?.Identity?.IsAuthenticated == true && Principal.HasClaim(CrmClaimTypes.Actor, ActorTypes.Staff);

    public UserId UserId =>
        IsAuthenticated && Guid.TryParse(Principal?.FindFirstValue(CrmClaimTypes.Subject), out var id)
            ? new UserId(id)
            : throw new UnauthorizedException();

    public Guid? SessionId =>
        Guid.TryParse(Principal?.FindFirstValue(CrmClaimTypes.SessionId), out var id) ? id : null;

    public IReadOnlyCollection<string> Roles => ValuesOf(CrmClaimTypes.Role);

    public IReadOnlyCollection<string> Permissions => ValuesOf(CrmClaimTypes.Permission);

    public CultureInfo Culture => CultureInfo.CurrentUICulture;

    public bool HasPermission(string permission) => Principal?.HasClaim(CrmClaimTypes.Permission, permission) == true;

    private string[] ValuesOf(string claimType) =>
        Principal is null ? [] : [.. Principal.FindAll(claimType).Select(claim => claim.Value)];
}
