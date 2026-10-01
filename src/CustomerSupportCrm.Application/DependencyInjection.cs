using System.Reflection;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Notifications;
using CustomerSupportCrm.Application.Behaviors;
using CustomerSupportCrm.Application.Common.Authorization;
using CustomerSupportCrm.Application.Features.Ai;
using CustomerSupportCrm.Application.Features.Attachments;
using CustomerSupportCrm.Application.Features.Authentication.Common;
using CustomerSupportCrm.Application.Features.Channels;
using CustomerSupportCrm.Application.Features.Customers.Common;
using CustomerSupportCrm.Application.Features.Integrations;
using CustomerSupportCrm.Application.Features.Notifications;
using CustomerSupportCrm.Application.Features.Reports;
using CustomerSupportCrm.Application.Features.Settings;
using CustomerSupportCrm.Application.Features.Sla;
using CustomerSupportCrm.Application.Features.Tickets;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomerSupportCrm.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssembly(assembly);
            config.AddOpenBehavior(typeof(ValidationBehavior<,>));
            config.AddOpenBehavior(typeof(FeatureToggleBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddEndpoints(assembly);
        services.AddLocalization();
        services.AddScoped<UserSessionService>();
        services.AddScoped<PasswordResetMailer>();
        services.AddScoped<IAccessScopeProvider, AccessScopeProvider>();
        services.AddScoped<NotificationSender>();
        services.AddScoped<AttachmentService>();
        services.AddScoped<CustomerTimeline>();
        services.AddScoped<TicketFactory>();
        services.AddScoped<TicketMessageWriter>();
        services.AddScoped<TicketHistoryRecorder>();
        services.AddScoped<SlaCalculator>();
        services.AddScoped<AssignmentEngine>();
        services.AddScoped<EscalationExecutor>();
        services.AddScoped<CustomerMessenger>();
        services.AddScoped<ReportFilterFactory>();
        services.AddScoped<AiAssistant>();
        services.AddScoped<SettingsReader>();
        services.AddScoped<WebhookPublisher>();
        services.AddScoped<CustomerResolver>();
        services.AddScoped<IChatAccessValidator, ChatAccessValidator>();

        return services;
    }

    public static IServiceCollection AddEndpoints(this IServiceCollection services, Assembly assembly)
    {
        Type[] groups = [typeof(IEndpoint), typeof(IPortalEndpoint), typeof(IPublicEndpoint), typeof(IExternalEndpoint)];

        var endpoints = assembly.DefinedTypes
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .SelectMany(type => groups.Where(type.IsAssignableTo).Select(group => ServiceDescriptor.Transient(group, type)));

        services.TryAddEnumerable(endpoints);
        return services;
    }
}
