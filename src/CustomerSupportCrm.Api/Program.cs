using System.Text.Json.Serialization;
using CustomerSupportCrm.Api.Configuration;
using CustomerSupportCrm.Api.Endpoints;
using CustomerSupportCrm.Api.Health;
using CustomerSupportCrm.Api.Localization;
using CustomerSupportCrm.Api.Middleware;
using CustomerSupportCrm.Api.OpenApi;
using CustomerSupportCrm.Application;
using CustomerSupportCrm.Infrastructure;
using CustomerSupportCrm.Infrastructure.Persistence;
using CustomerSupportCrm.Infrastructure.Persistence.Seed;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApiLogging()
    .AddApiLocalization()
    .AddApiOpenApi()
    .AddApiCors()
    .AddApiRateLimiting()
    .AddExceptionHandler<GlobalExceptionHandler>()
    .ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Deployment step: apply migrations and seed data, then exit.
if (args.Contains("--init-database"))
{
    await app.Services.InitializeDatabaseAsync();
    return;
}

if (app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.InitializeOnStartup)
{
    await app.Services.InitializeDatabaseAsync();
}

// Order matters: correlation and culture are ambient (async-local) state, so they must wrap
// the request logging and exception handling that read them.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseRequestLocalization();
app.UseApiRequestLogging();
app.UseExceptionHandler(_ => { });
app.UseStatusCodePages(ErrorResponseWriter.WriteStatusCodePageAsync);

if (app.Environment.IsDevelopment())
{
    app.MapApiOpenApi();
}

app.UseHttpsRedirection();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapApiHealthChecks();
app.MapApiEndpoints();

await app.RunAsync();

public partial class Program;
