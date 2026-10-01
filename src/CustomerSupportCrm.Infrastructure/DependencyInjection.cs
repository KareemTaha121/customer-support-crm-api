using CustomerSupportCrm.Application.Abstractions.Ai;
using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Channels;
using CustomerSupportCrm.Application.Abstractions.Files;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Integrations;
using CustomerSupportCrm.Application.Abstractions.Notifications;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Abstractions.Reporting;
using CustomerSupportCrm.Application.Abstractions.Search;
using CustomerSupportCrm.Application.Features.Channels;
using CustomerSupportCrm.Application.Features.Dashboard;
using CustomerSupportCrm.Application.Features.Integrations;
using CustomerSupportCrm.Application.Features.Settings;
using CustomerSupportCrm.Application.Features.Sla;
using CustomerSupportCrm.Infrastructure.Ai;
using CustomerSupportCrm.Infrastructure.Auditing;
using CustomerSupportCrm.Infrastructure.Authentication;
using CustomerSupportCrm.Infrastructure.Authorization;
using CustomerSupportCrm.Infrastructure.BackgroundJobs;
using CustomerSupportCrm.Infrastructure.Channels;
using CustomerSupportCrm.Infrastructure.Files;
using CustomerSupportCrm.Infrastructure.Integrations;
using CustomerSupportCrm.Infrastructure.Persistence;
using CustomerSupportCrm.Infrastructure.Persistence.Interceptors;
using CustomerSupportCrm.Infrastructure.Persistence.Seed;
using CustomerSupportCrm.Infrastructure.Realtime;
using CustomerSupportCrm.Infrastructure.Reporting;
using CustomerSupportCrm.Infrastructure.Search;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CustomerSupportCrm.Infrastructure;

public static class DependencyInjection
{
    public const string ReadinessTag = "ready";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();

        services.AddPersistence();
        services.AddIdentityServices();
        services.AddPlatformServices();
        services.AddChannels();

        return services;
    }

    private static void AddPersistence(this IServiceCollection services)
    {
        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<AuditableEntityInterceptor>();

        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
        {
            var database = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            options
                .UseNpgsql(database.ConnectionString, npgsql => npgsql.CommandTimeout(database.CommandTimeoutSeconds))
                .UseSnakeCaseNamingConvention()
                .EnableSensitiveDataLogging(database.EnableSensitiveDataLogging)
                .AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>());
        });
        services.AddScoped<IApplicationDbContext>(serviceProvider => serviceProvider.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<SqlRunner>();
        services.AddScoped<ISequenceGenerator, PostgresSequenceGenerator>();
        services.AddScoped<IReportingQueries, PostgresReportingQueries>();
        services.AddScoped<IKnowledgeSearch, PostgresKnowledgeSearch>();

        services.AddOptions<BootstrapOptions>().BindConfiguration(BootstrapOptions.SectionName);
        services.AddScoped<DatabaseInitializer>();

        services.AddHealthChecks()
            .AddDbContextCheck<ApplicationDbContext>("database", tags: [ReadinessTag]);
    }

    private static void AddPlatformServices(this IServiceCollection services)
    {
        services.AddOptions<StorageOptions>().BindConfiguration(StorageOptions.SectionName);
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        services.AddSignalR();
        services.AddSingleton<IUserIdProvider, SubjectUserIdProvider>();
        services.AddSingleton<IRealtimeNotifier, SignalRRealtimeNotifier>();

        services.AddOptions<BackgroundJobOptions>().BindConfiguration(BackgroundJobOptions.SectionName);
        services.AddRecurringRequest<EvaluateSlaCommand>(TimeSpan.FromMinutes(1));
        services.AddRecurringRequest<SendTaskRemindersCommand>(TimeSpan.FromMinutes(1));
    }

    private static void AddChannels(this IServiceCollection services)
    {
        services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.SectionName);
        services.AddOptions<WhatsAppOptions>().BindConfiguration(WhatsAppOptions.SectionName);
        services.AddOptions<SmsOptions>().BindConfiguration(SmsOptions.SectionName);
        services.AddOptions<CustomerPortalOptions>().BindConfiguration(CustomerPortalOptions.SectionName);

        // One Email sender either way, so the dispatcher and GET /channels/status see a single Email channel.
        services.AddSingleton<IMessageSender>(sp =>
            string.Equals(sp.GetRequiredService<IOptions<EmailOptions>>().Value.Provider, EmailOptions.LogProvider, StringComparison.OrdinalIgnoreCase)
                ? ActivatorUtilities.CreateInstance<LogEmailSender>(sp)
                : ActivatorUtilities.CreateInstance<SmtpEmailSender>(sp));
        services.AddHttpClient<IMessageSender, WhatsAppSender>(client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddHttpClient<IMessageSender, TwilioSmsSender>(client => client.Timeout = TimeSpan.FromSeconds(20));

        services.AddSingleton<IChannelWebhookAdapter, EmailWebhookAdapter>();
        services.AddSingleton<IChannelWebhookAdapter, WhatsAppWebhookAdapter>();
        services.AddSingleton<IChannelWebhookAdapter, SmsWebhookAdapter>();

        services.AddRecurringRequest<DispatchOutboxCommand>(TimeSpan.FromSeconds(15));

        // Keys must survive restarts (they decrypt webhook secrets): persist them with the file storage.
        services.AddDataProtection()
            .SetApplicationName("CustomerSupportCrm")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "App_Data", "keys")));
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<ICurrentApiClient, HttpCurrentApiClient>();
        services.AddHttpClient<IWebhookSender, SafeWebhookSender>(client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(SafeWebhookSender.CreateHandler);
        services.AddRecurringRequest<DispatchWebhooksCommand>(TimeSpan.FromSeconds(15));
        services.AddRecurringRequest<AutoCloseResolvedTicketsCommand>(TimeSpan.FromHours(1));

        services.AddOptions<AiOptions>().BindConfiguration(AiOptions.SectionName);
        services.AddSingleton<IAiCompletionClient, AnthropicAiClient>();
    }

    private static void AddIdentityServices(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;

                // Keep claim names as issued ("sub", "role", "permission").
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = TokenService.CreateSigningKey(jwt),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = CrmClaimTypes.Name,
                    RoleClaimType = CrmClaimTypes.Role,
                };

                // Browsers cannot set headers on WebSocket requests; SignalR sends the token in the query.
                bearer.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddPermissionPolicies();

        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<ICurrentCustomer, HttpCurrentCustomer>();
        services.AddScoped<IRequestContext, HttpRequestContext>();
        services.AddScoped<IAuditTrail, AuditTrail>();
    }
}
