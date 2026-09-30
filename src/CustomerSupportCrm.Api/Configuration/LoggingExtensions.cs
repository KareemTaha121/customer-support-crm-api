using System.Globalization;
using System.Security.Claims;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace CustomerSupportCrm.Api.Configuration;

internal static class LoggingExtensions
{
    private const string DevelopmentTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}  {Message:lj} {Properties:j}{NewLine}{Exception}";

    /// <summary>
    /// Levels and properties come from the "Serilog" configuration section. The console sink is
    /// readable text in Development and compact JSON everywhere else.
    /// </summary>
    public static IServiceCollection AddApiLogging(this IServiceCollection services) =>
        services.AddSerilog((serviceProvider, logger) =>
        {
            logger
                .ReadFrom.Configuration(serviceProvider.GetRequiredService<IConfiguration>())
                .ReadFrom.Services(serviceProvider)
                .Enrich.FromLogContext();

            if (serviceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
            {
                logger.WriteTo.Console(outputTemplate: DevelopmentTemplate, formatProvider: CultureInfo.InvariantCulture);
            }
            else
            {
                logger.WriteTo.Console(new RenderedCompactJsonFormatter());
            }
        });

    /// <summary>
    /// One structured event per request. Only method, path (no query string), route and status
    /// are recorded; headers and bodies are never logged.
    /// </summary>
    public static IApplicationBuilder UseApiRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
            options.GetLevel = (context, _, exception) =>
                exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError
                    ? LogEventLevel.Error
                    : context.Request.Path.StartsWithSegments("/health")
                        ? LogEventLevel.Verbose
                        : LogEventLevel.Information;
            options.EnrichDiagnosticContext = (diagnostics, context) =>
            {
                diagnostics.Set("RequestId", context.TraceIdentifier);

                if (context.GetEndpoint() is RouteEndpoint endpoint)
                {
                    diagnostics.Set("Route", endpoint.RoutePattern.RawText);
                }

                if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId)
                {
                    diagnostics.Set("UserId", userId);
                }
            };
        });
}
