using Microsoft.OpenApi;

namespace CustomerSupportCrm.Api.OpenApi;

internal static class OpenApiExtensions
{
    private const string DocumentName = "v1";
    private const string BearerScheme = "Bearer";

    public static IServiceCollection AddApiOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(DocumentName, options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "Customer Support CRM API";
            document.Info.Version = DocumentName;

            // Swagger UI "Authorize": paste the accessToken returned by POST /api/v1/auth/login.
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Access token from POST /api/v1/auth/login.",
            };

            document.Security ??= [];
            document.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerScheme, document)] = [],
            });

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
