using System.Net;
using CustomerSupportCrm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportCrm.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class DatabaseTests(PostgresApiFactory factory)
{
    [Fact]
    public async Task ReadinessIsHealthyWhenDatabaseIsReachable()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MigrationsAreFullyApplied()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task SeedsSystemAndDefaultRoles()
    {
        var api = new ApiTestClient(factory.CreateApiClient());
        var admin = await api.LoginAsAdminAsync();

        var roles = (await api.GetDataAsync("/api/v1/roles", admin.AccessToken)).EnumerateArray().ToList();
        var administrator = roles.Single(r => r.GetProperty("name").GetString() == "Administrator");

        Assert.True(administrator.GetProperty("isSystem").GetBoolean());
        Assert.Equal(Domain.Roles.Permissions.All.Count, administrator.GetProperty("permissions").GetArrayLength());
        Assert.Contains(roles, r => r.GetProperty("name").GetString() == "Manager");
        Assert.Contains(roles, r => r.GetProperty("name").GetString() == "Agent");
    }
}
