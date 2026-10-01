using System.Globalization;
using CustomerSupportCrm.Application.Abstractions.Ai;
using CustomerSupportCrm.Application.Abstractions.Auditing;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Channels;
using CustomerSupportCrm.Application.Abstractions.Http;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Contracts.Common;
using CustomerSupportCrm.Domain.Integrations;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Tickets;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Settings;

public sealed record SettingResponse(string Key, string Kind, string Value, string DefaultValue, bool IsPublic);

public sealed record UpdateSettingsRequest(IReadOnlyDictionary<string, string> Values);

public static class SettingErrors
{
    public const string UnknownSetting = "UNKNOWN_SETTING";
    public const string FeatureDisabled = "FEATURE_DISABLED";
}

/// <summary>Reads settings with catalog defaults; cached for the request.</summary>
public sealed class SettingsReader(IApplicationDbContext db)
{
    private Dictionary<string, string>? _values;

    public async Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken) =>
        bool.TryParse(await GetAsync(key, cancellationToken), out var value) && value;

    public async Task<int> GetIntAsync(string key, CancellationToken cancellationToken) =>
        int.TryParse(await GetAsync(key, cancellationToken), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    public async Task<string> GetAsync(string key, CancellationToken cancellationToken)
    {
        _values ??= await db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Value, cancellationToken);
        return _values.TryGetValue(key, out var value) ? value : SystemSettings.Catalog.First(c => c.Key == key).Default;
    }

    /// <summary>Throws 409 FEATURE_DISABLED when a toggle is off.</summary>
    public async Task EnsureEnabledAsync(string key, CancellationToken cancellationToken)
    {
        if (!await IsEnabledAsync(key, cancellationToken))
        {
            throw new ConflictException(SettingErrors.FeatureDisabled, "This feature is currently disabled.");
        }
    }

    public async Task<IReadOnlyList<SettingResponse>> ListAsync(bool publicOnly, CancellationToken cancellationToken)
    {
        var results = new List<SettingResponse>();
        foreach (var (key, kind, defaultValue, isPublic) in SystemSettings.Catalog.Where(c => !publicOnly || c.Public))
        {
            results.Add(new SettingResponse(key, kind.ToString(), await GetAsync(key, cancellationToken), defaultValue, isPublic));
        }

        return results;
    }
}

/// <summary>
/// Server-side prerequisites of some settings, for warnings on /admin/settings. Booleans only: no
/// keys, hosts or other configuration.
/// </summary>
public sealed record SettingsStatusResponse(bool AiProviderConfigured, bool EmailConfigured);

internal sealed class SettingsEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/settings").WithTags("Settings");

        group.MapGet("/", async (SettingsReader settings, CancellationToken ct) => ApiResults.Ok(await settings.ListAsync(false, ct)))
            .WithName("ListSettings")
            .Produces<ApiResponse<IReadOnlyList<SettingResponse>>>();

        group.MapGet("/status", (IAiCompletionClient ai, IEnumerable<IMessageSender> senders) =>
                ApiResults.Ok(new SettingsStatusResponse(ai.IsConfigured, senders.Any(s => s.Channel == TicketChannel.Email && s.IsConfigured))))
            .RequireAuthorization(Permissions.SettingsManage)
            .WithName("GetSettingsStatus")
            .Produces<ApiResponse<SettingsStatusResponse>>();

        group.MapPut("/", async (UpdateSettingsRequest request, IApplicationDbContext db, ICurrentUser user, IAuditTrail audit, TimeProvider time, SettingsReader settings, CancellationToken ct) =>
            {
                var now = time.GetUtcNow();
                foreach (var (key, value) in request.Values ?? new Dictionary<string, string>())
                {
                    var definition = SystemSettings.Catalog.FirstOrDefault(c => c.Key == key);
                    if (definition.Key is null)
                    {
                        throw new ValidationException([new ValidationFailure(key, $"Unknown setting '{key}'.") { ErrorCode = SettingErrors.UnknownSetting }]);
                    }

                    var valid = definition.Kind switch
                    {
                        SettingKind.Boolean => bool.TryParse(value, out _),
                        SettingKind.Number => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0 && n <= 3650,
                        _ => value.Length <= 2000,
                    };
                    if (!valid)
                    {
                        throw new ValidationException([new ValidationFailure(key, $"Invalid value for '{key}'.") { ErrorCode = ErrorCodes.InvalidValue }]);
                    }

                    var normalized = definition.Kind == SettingKind.Boolean ? (bool.Parse(value) ? "true" : "false") : value;
                    var existing = await db.SystemSettings.SingleOrDefaultAsync(s => s.Id == key, ct);
                    if (existing is null)
                    {
                        db.SystemSettings.Add(SystemSetting.Create(key, normalized, now, user.UserId.Value));
                    }
                    else
                    {
                        existing.Set(normalized, now, user.UserId.Value);
                    }

                    audit.Record("settings.updated", "Setting", key, newValues: new { value = normalized });
                }

                await db.SaveChangesAsync(ct);
                return ApiResults.Ok(await new SettingsReader(db).ListAsync(false, ct));
            })
            .RequireAuthorization(Permissions.SettingsManage)
            .WithName("UpdateSettings")
            .Produces<ApiResponse<IReadOnlyList<SettingResponse>>>();
    }
}

/// <summary>Public feature flags (portal registration, chat, web form, chatbot) for the web app.</summary>
internal sealed class PublicFeatureEndpoints : IPublicEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/features", async (SettingsReader settings, IAiCompletionClient ai, CancellationToken ct) =>
            {
                var features = (await settings.ListAsync(publicOnly: true, ct)).ToDictionary(s => s.Key, s => s.Value);

                // The chatbot needs an AI provider; without one the portal must not offer it.
                if (!ai.IsConfigured)
                {
                    features[SystemSettings.ChatbotEnabled] = "false";
                }

                return ApiResults.Ok(features);
            })
            .WithTags("Settings")
            .WithName("GetPublicFeatures")
            .Produces<ApiResponse<Dictionary<string, string>>>();
}

/// <summary>Toggle checks for anonymous features, run before their handlers.</summary>
internal sealed class FeatureToggleBehavior<TRequest, TResponse>(SettingsReader settings) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly Dictionary<string, string> Toggles = new(StringComparer.Ordinal)
    {
        ["PortalRegisterCommand"] = SystemSettings.PortalRegistrationEnabled,
        ["StartChatCommand"] = SystemSettings.LiveChatEnabled,
        ["SubmitWebFormCommand"] = SystemSettings.WebFormEnabled,
        ["ChatbotCommand"] = SystemSettings.ChatbotEnabled,
        ["SummarizeTicketCommand"] = SystemSettings.AiAgentAssistEnabled,
        ["SuggestReplyCommand"] = SystemSettings.AiAgentAssistEnabled,
        ["CategorizeTicketCommand"] = SystemSettings.AiAgentAssistEnabled,
        ["SuggestSolutionsCommand"] = SystemSettings.AiAgentAssistEnabled,
    };

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (Toggles.TryGetValue(typeof(TRequest).Name, out var key))
        {
            await settings.EnsureEnabledAsync(key, cancellationToken);
        }

        return await next(cancellationToken);
    }
}

/// <summary>Hourly: closes tickets resolved longer than the configured number of days (0 = never).</summary>
public sealed record AutoCloseResolvedTicketsCommand : IRequest;

internal sealed class AutoCloseResolvedTicketsHandler(IApplicationDbContext db, SettingsReader settings, TimeProvider time) : IRequestHandler<AutoCloseResolvedTicketsCommand>
{
    public async Task Handle(AutoCloseResolvedTicketsCommand request, CancellationToken cancellationToken)
    {
        var days = await settings.GetIntAsync(SystemSettings.AutoCloseResolvedDays, cancellationToken);
        if (days <= 0)
        {
            return;
        }

        var now = time.GetUtcNow();
        var cutoff = now.AddDays(-days);
        var tickets = await db.Tickets.Where(t => t.Status == TicketStatus.Resolved && t.ResolvedAt <= cutoff).Take(500).ToListAsync(cancellationToken);
        foreach (var ticket in tickets)
        {
            ticket.ChangeStatus(TicketStatus.Closed, now);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
