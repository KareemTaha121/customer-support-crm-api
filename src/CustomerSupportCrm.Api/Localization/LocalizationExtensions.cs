namespace CustomerSupportCrm.Api.Localization;

internal static class LocalizationExtensions
{
    public const string DefaultCulture = "en";

    public static readonly string[] SupportedCultures = ["en", "ar"];

    /// <summary>
    /// Negotiates the request culture (query string, cookie, then Accept-Language) and
    /// echoes it in the Content-Language response header.
    /// </summary>
    public static IServiceCollection AddApiLocalization(this IServiceCollection services) =>
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.SetDefaultCulture(DefaultCulture)
                .AddSupportedCultures(SupportedCultures)
                .AddSupportedUICultures(SupportedCultures);
            options.ApplyCurrentCultureToResponseHeaders = true;
        });
}
