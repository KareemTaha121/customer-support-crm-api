using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using CustomerSupportCrm.Application.Abstractions.Http;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
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

public sealed class GlobalRateLimitOptions
{
    public const string SectionName = "RateLimiting:Global";

    [Range(1, 100_000)]
    public int PermitLimit { get; init; } = 300;

    [Range(1, 3_600)]
    public int WindowSeconds { get; init; } = 60;
}

public sealed class RequestLimitOptions
{
    public const string SectionName = "RequestLimits";

    /// <summary>Kestrel body limit. Must stay above the 20 MB attachment limit plus multipart overhead.</summary>
    [Range(1_048_576, 1_073_741_824)]
    public long MaxRequestBodyBytes { get; init; } = 25 * 1024 * 1024;
}

internal static class SecurityExtensions
{
    /// <summary>
    /// Credentialed CORS for the configured frontend origins only (the refresh cookie needs
    /// credentials, which the CORS spec forbids combining with a wildcard origin).
    /// </summary>
    public static IServiceCollection AddApiCors(this IServiceCollection services, IHostEnvironment environment)
    {
        // Development and the integration-test host may run without origins; a deployed API may not.
        var originsRequired = !environment.IsDevelopment() && !environment.IsEnvironment("Test");
        services.AddOptions<CorsSettings>()
            .BindConfiguration(CorsSettings.SectionName)
            .Validate(
                settings => !originsRequired || settings.AllowedOrigins.Length > 0,
                $"{CorsSettings.SectionName}:AllowedOrigins must list at least one origin outside Development.")
            .ValidateOnStart();
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
        services.AddOptions<GlobalRateLimitOptions>()
            .BindConfiguration(GlobalRateLimitOptions.SectionName)
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

            // Every request except health probes: per signed-in user, falling back to the client IP.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
                {
                    return RateLimitPartition.GetNoLimiter("health");
                }

                var limits = context.RequestServices.GetRequiredService<IOptions<GlobalRateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.PermitLimit,
                        Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                        QueueLimit = 0,
                    });
            });

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

            // AI: per signed-in user (or IP for the anonymous chatbot), 20 requests per minute.
            options.AddPolicy(RateLimitPolicies.Ai, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

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

    /// <summary>
    /// Kestrel hardening: configurable request body limit (oversized bodies become 413
    /// PAYLOAD_TOO_LARGE) and no Server header.
    /// </summary>
    public static IServiceCollection AddApiRequestLimits(this IServiceCollection services)
    {
        services.AddOptions<RequestLimitOptions>()
            .BindConfiguration(RequestLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<KestrelServerOptions>()
            .Configure<IOptions<RequestLimitOptions>>((kestrel, limits) =>
            {
                kestrel.AddServerHeader = false;
                kestrel.Limits.MaxRequestBodySize = limits.Value.MaxRequestBodyBytes;
            });
        return services;
    }
}
