using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using CustomerSupportCrm.Application.Abstractions.Http;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace CustomerSupportCrm.Api.Configuration;

public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    /// <summary>Exact frontend origins, e.g. https://crm.example.com. Never "*".</summary>
    public string[] AllowedOrigins { get; init; } = [];
}

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting:Authentication";

    [Range(1, 10_000)]
    public int PermitLimit { get; init; } = 10;

    [Range(1, 3_600)]
    public int WindowSeconds { get; init; } = 60;
}

public sealed class PublicRateLimitOptions
{
    public const string SectionName = "RateLimiting:Public";

    [Range(1, 10_000)]
    public int PermitLimit { get; init; } = 30;

    [Range(1, 3_600)]
    public int WindowSeconds { get; init; } = 60;
}

internal static class SecurityExtensions
{
    /// <summary>
    /// Credentialed CORS for the configured frontend origins only (the refresh cookie needs
    /// credentials, which the CORS spec forbids combining with a wildcard origin).
    /// </summary>
    public static IServiceCollection AddApiCors(this IServiceCollection services)
    {
        services.AddOptions<CorsSettings>().BindConfiguration(CorsSettings.SectionName);
        services.AddCors();
        services.AddOptions<CorsOptions>()
            .Configure<IOptions<CorsSettings>>((cors, settings) => cors.AddDefaultPolicy(policy => policy
                .WithOrigins(settings.Value.AllowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
                .WithExposedHeaders(CorrelationIdHttpContextExtensions.HeaderName, "Content-Language", "Retry-After")));
        return services;
    }

    /// <summary>
    /// Fixed-window limit per client IP for sign-in endpoints. Rejections return 429 with the
    /// standard envelope (via status-code pages) and Retry-After.
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitOptions>()
            .BindConfiguration(RateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<PublicRateLimitOptions>()
            .BindConfiguration(PublicRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

            options.AddPolicy(RateLimitPolicies.Public, context =>
            {
                var limits = context.RequestServices.GetRequiredService<IOptions<PublicRateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.PermitLimit,
                        Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                        QueueLimit = 0,
                    });
            });

            options.AddPolicy(RateLimitPolicies.Authentication, context =>
            {
                var limits = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.PermitLimit,
                        Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }
}
