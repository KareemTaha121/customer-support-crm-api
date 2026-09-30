using CustomerSupportCrm.Application.Abstractions.Http;
using Microsoft.AspNetCore.Http;

namespace CustomerSupportCrm.Infrastructure.Authentication;

/// <summary>
/// Request transport details. Behind a reverse proxy, configure forwarded headers so
/// <see cref="IpAddress"/> is the client address rather than the proxy's.
/// </summary>
internal sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    public string? CorrelationId => accessor.HttpContext?.GetCorrelationId();

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent =>
        accessor.HttpContext?.Request.Headers.UserAgent is { Count: > 0 } userAgent ? userAgent.ToString() : null;
}
