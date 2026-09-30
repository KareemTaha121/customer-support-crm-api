namespace CustomerSupportCrm.Api.OpenApi;

internal static class OpenApiExtensions
{
    private const string DocumentName = "v1";

    public static IServiceCollection AddApiOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(DocumentName, options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "Customer Support CRM API";
            document.Info.Version = DocumentName;
            return Task.CompletedTask;
        }));

    /// <summary>Serves /openapi/v1.json and Swagger UI at /swagger. Development only.</summary>
    public static WebApplication MapApiOpenApi(this WebApplication app)
    {
        app.MapOpenApi();
        app.UseSwaggerUI(options => options.SwaggerEndpoint($"/openapi/{DocumentName}.json", "Customer Support CRM API v1"));
        return app;
    }
}
