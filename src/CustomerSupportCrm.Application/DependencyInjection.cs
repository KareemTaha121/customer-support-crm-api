using System.Reflection;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Behaviors;
using CustomerSupportCrm.Application.Common.Authorization;
using CustomerSupportCrm.Application.Features.Authentication.Common;
using CustomerSupportCrm.Application.Features.Notifications;
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
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddEndpoints(assembly);
        services.AddLocalization();
        services.AddScoped<UserSessionService>();
        services.AddScoped<IAccessScopeProvider, AccessScopeProvider>();
        services.AddScoped<NotificationSender>();

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
