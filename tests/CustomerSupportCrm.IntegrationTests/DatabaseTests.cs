using System.Net;
using CustomerSupportCrm.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportCrm.IntegrationTests;

public sealed class DatabaseTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>
{
    [Fact]
    public async Task ReadinessIsHealthyWhenDatabaseIsReachable()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DbContextConnectsToPostgres()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.True(await dbContext.Database.CanConnectAsync());
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", dbContext.Database.ProviderName);
    }
}
