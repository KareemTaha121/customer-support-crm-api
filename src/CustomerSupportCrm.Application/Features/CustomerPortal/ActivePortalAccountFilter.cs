using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Application.Features.Authentication.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportCrm.Application.Features.CustomerPortal;

/// <summary>
/// Applied to the /portal group: a customer token is honoured only while its account is active and
/// the token was issued after the last password reset, so revoking portal access or resetting the
/// password signs the customer out at once instead of when the token expires.
/// One primary-key lookup per portal request.
/// </summary>
public sealed class ActivePortalAccountFilter : IEndpointFilter
{
    private const string IssuedAtClaim = "iat";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var services = context.HttpContext.RequestServices;
        var accountId = services.GetRequiredService<ICurrentCustomer>().AccountId;

        // JWT "iat" (seconds since the epoch); MapInboundClaims is off, so the claim keeps its name.
        var issuedAt = long.TryParse(context.HttpContext.User.FindFirst(IssuedAtClaim)?.Value, out var iat)
            ? DateTimeOffset.FromUnixTimeSeconds(iat)
            : DateTimeOffset.MinValue;

        var active = await services.GetRequiredService<IApplicationDbContext>().CustomerAccounts
            .AsNoTracking()
            .AnyAsync(a => a.Id == accountId && a.IsActive && (a.SessionsValidFrom == null || a.SessionsValidFrom <= issuedAt), context.HttpContext.RequestAborted);

        if (!active)
        {
            throw new UnauthorizedException(AuthenticationErrors.AccountDisabled, "The account is disabled.");
        }

        return await next(context);
    }
}
