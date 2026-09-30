using CustomerSupportCrm.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace CustomerSupportCrm.Api.Health;

internal static class HealthCheckExtensions
{
    /// <summary>
    /// /health/live runs no checks (the process is up). /health/ready runs dependency checks
    /// tagged "ready". Both return only the overall status, never check details.
    /// </summary>
    public static WebApplication MapApiHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(DependencyInjection.ReadinessTag),
        });
        return app;
    }
}
