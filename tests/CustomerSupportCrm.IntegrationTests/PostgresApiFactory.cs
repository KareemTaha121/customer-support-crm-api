using System.Security.Cryptography;
using CustomerSupportCrm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace CustomerSupportCrm.IntegrationTests;

/// <summary>
/// Runs the real API against a disposable PostgreSQL database, migrated and seeded at startup.
/// By default a Testcontainers PostgreSQL is started (requires Docker). Set CRM_TEST_POSTGRES to a
/// connection string for an existing server to use a throwaway database on it instead.
/// </summary>
public sealed class PostgresApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@it.test";
    public const string AdminPassword = "integration-admin-password";

    private const string ExternalServerVariable = "CRM_TEST_POSTGRES";

    private PostgreSqlContainer? _container;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var externalServer = Environment.GetEnvironmentVariable(ExternalServerVariable);
        if (!string.IsNullOrWhiteSpace(externalServer))
        {
            // Migrate creates the database; it is dropped on dispose.
            _connectionString = new NpgsqlConnectionStringBuilder(externalServer)
            {
                Database = $"crm_it_{Guid.NewGuid():N}",
            }.ConnectionString;
        }
        else
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await _container.StartAsync();
            _connectionString = _container.GetConnectionString();
        }

        // Starting the host applies migrations and seeds the administrator.
        _ = Services;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_container is null && _connectionString.Length > 0)
        {
            await using var scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync();
        }

        await DisposeAsync();

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.UseSetting("Database:ConnectionString", _connectionString);
        builder.UseSetting("Database:InitializeOnStartup", "true");
        builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        builder.UseSetting("Bootstrap:AdminEmail", AdminEmail);
        builder.UseSetting("Bootstrap:AdminPassword", AdminPassword);
        builder.UseSetting("RateLimiting:Authentication:PermitLimit", "10000");
        builder.UseSetting("BackgroundJobs:Enabled", "false");
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresApiFactory>
{
    public const string Name = "postgres";
}
