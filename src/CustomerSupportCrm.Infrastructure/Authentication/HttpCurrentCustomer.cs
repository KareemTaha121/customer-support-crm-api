using System.Security.Claims;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Common.Exceptions;
using Microsoft.AspNetCore.Http;

namespace CustomerSupportCrm.Infrastructure.Authentication;

internal sealed class HttpCurrentCustomer(IHttpContextAccessor accessor) : ICurrentCustomer
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated =>
        Principal?.Identity?.IsAuthenticated == true && Principal.HasClaim(CrmClaimTypes.Actor, ActorTypes.Customer);

    public Guid AccountId => Read(CrmClaimTypes.Subject);

    public Guid CustomerId => Read(CrmClaimTypes.CustomerId);

    private Guid Read(string claimType) =>
        IsAuthenticated && Guid.TryParse(Principal!.FindFirstValue(claimType), out var id)
            ? id
            : throw new UnauthorizedException();
}
