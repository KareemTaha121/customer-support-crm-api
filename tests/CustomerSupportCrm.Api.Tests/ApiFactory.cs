using CustomerSupportCrm.Application;
using FluentValidation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportCrm.Api.Tests;

/// <summary>
/// Hosts the real API pipeline plus the test-only endpoints in this assembly.
/// The database points at an unreachable port: these tests never need PostgreSQL.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>High enough that ordinary tests never hit the auth rate limit.</summary>
    protected virtual int RateLimitPermits => 1_000;

    public const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=2";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:ConnectionString", UnreachableDatabase);
        builder.UseSetting("Database:InitializeOnStartup", "false");
        builder.UseSetting("BackgroundJobs:Enabled", "false");
        builder.UseSetting("RateLimiting:Authentication:PermitLimit", RateLimitPermits.ToString(System.Globalization.CultureInfo.InvariantCulture));

        builder.ConfigureTestServices(services =>
        {
            var assembly = typeof(ApiFactory).Assembly;
            services.AddEndpoints(assembly);
            services.AddMediatR(config => config.RegisterServicesFromAssembly(assembly));
            services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        });
    }
}
