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
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=2";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:ConnectionString", UnreachableDatabase);

        builder.ConfigureTestServices(services =>
        {
            var assembly = typeof(ApiFactory).Assembly;
            services.AddEndpoints(assembly);
            services.AddMediatR(config => config.RegisterServicesFromAssembly(assembly));
            services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        });
    }
}
