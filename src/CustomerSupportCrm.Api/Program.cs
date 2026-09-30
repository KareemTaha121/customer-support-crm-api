using System.Text.Json.Serialization;
using CustomerSupportCrm.Api.Configuration;
using CustomerSupportCrm.Api.Endpoints;
using CustomerSupportCrm.Api.Health;
using CustomerSupportCrm.Api.Localization;
using CustomerSupportCrm.Api.Middleware;
using CustomerSupportCrm.Api.OpenApi;
using CustomerSupportCrm.Application;
using CustomerSupportCrm.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApiLogging()
    .AddApiLocalization()
    .AddApiOpenApi()
    .AddExceptionHandler<GlobalExceptionHandler>()
    .ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

var app = builder.Build();

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

app.MapApiHealthChecks();
app.MapApiEndpoints();

app.Run();

public partial class Program;
