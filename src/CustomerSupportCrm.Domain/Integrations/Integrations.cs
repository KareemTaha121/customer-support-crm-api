using System.Security.Cryptography;
using CustomerSupportCrm.Domain.Common;

namespace CustomerSupportCrm.Domain.Integrations;

/// <summary>Scopes an API key can hold for the external API.</summary>
public static class ApiScopes
{
    public const string CustomersRead = "customers:read";
    public const string CustomersWrite = "customers:write";
    public const string TicketsRead = "tickets:read";
    public const string TicketsWrite = "tickets:write";

    public static IReadOnlyList<string> All { get; } = [CustomersRead, CustomersWrite, TicketsRead, TicketsWrite];
}

/// <summary>
/// Credential for an external system (ERP, website, automation). Only a SHA-256 hash of the
/// key is stored; the plaintext is shown once at creation.
/// </summary>
public sealed class ApiKey : Entity<Guid>, IAuditableEntity
{
    public const string KeyPrefix = "crm_";
    public const string InvalidCode = "INVALID_API_KEY";

    private ApiKey()
    {
        Name = string.Empty;
        DisplayPrefix = string.Empty;
        KeyHash = string.Empty;
        Scopes = [];
    }

    private ApiKey(Guid id)
        : base(id)
    {
        Name = string.Empty;
        DisplayPrefix = string.Empty;
        KeyHash = string.Empty;
        Scopes = [];
    }

    public string Name { get; private set; }

    /// <summary>First characters of the key, for recognizing it in lists.</summary>
    public string DisplayPrefix { get; private set; }

    public string KeyHash { get; private set; }

    public List<string> Scopes { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    /// <returns>The key and the plaintext secret (never stored).</returns>
    public static (ApiKey Key, string Plaintext) Create(string name, IEnumerable<string> scopes, DateTimeOffset? expiresAt)
    {
        var trimmed = name?.Trim();
        var scopeList = scopes.Distinct(StringComparer.Ordinal).ToList();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 150 || scopeList.Count == 0 || scopeList.Any(s => !ApiScopes.All.Contains(s)))
        {
            throw new DomainException(InvalidCode, "The API key needs a name and at least one valid scope.");
        }

        var plaintext = KeyPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        var key = new ApiKey(Guid.CreateVersion7())
        {
            Name = trimmed,
            DisplayPrefix = plaintext[..12],
            KeyHash = Hash(plaintext),
            Scopes = scopeList,
            ExpiresAt = expiresAt,
        };
        return (key, plaintext);
    }

    public static string Hash(string plaintext) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plaintext)));

    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);

    public void RecordUse(DateTimeOffset now)
    {
        // Throttle writes: at most one update per minute per key.
        if (LastUsedAt is null || now - LastUsedAt > TimeSpan.FromMinutes(1))
        {
            LastUsedAt = now;
        }
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

public static class WebhookEvents
{
    public const string TicketCreated = "ticket.created";
    public const string TicketStatusChanged = "ticket.status_changed";
    public const string TicketAssigned = "ticket.assigned";
    public const string TicketMessageAdded = "ticket.message_added";
    public const string TicketFeedback = "ticket.feedback";
    public const string CustomerCreated = "customer.created";

    public static IReadOnlyList<string> All { get; } = [TicketCreated, TicketStatusChanged, TicketAssigned, TicketMessageAdded, TicketFeedback, CustomerCreated];
}

/// <summary>An HTTPS endpoint notified of selected events, signed with a shared secret.</summary>
public sealed class WebhookSubscription : Entity<Guid>, IAuditableEntity
{
    public const string InvalidCode = "INVALID_WEBHOOK";

    private WebhookSubscription()
    {
        Name = string.Empty;
        Url = string.Empty;
        ProtectedSecret = string.Empty;
        Events = [];
    }

    private WebhookSubscription(Guid id)
        : base(id)
    {
        Name = string.Empty;
        Url = string.Empty;
        ProtectedSecret = string.Empty;
        Events = [];
    }

    public string Name { get; private set; }

    public string Url { get; private set; }

    public List<string> Events { get; private set; }

    /// <summary>Signing secret, encrypted at rest (it must be recoverable to sign payloads).</summary>
    public string ProtectedSecret { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static WebhookSubscription Create(string name, string url, IEnumerable<string> events, string protectedSecret)
    {
        var subscription = new WebhookSubscription(Guid.CreateVersion7()) { ProtectedSecret = protectedSecret, IsActive = true };
        subscription.Update(name, url, events, isActive: true);
        return subscription;
    }

    public static string NewSecret() => "whsec_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));

    public void Update(string name, string url, IEnumerable<string> events, bool isActive)
    {
        var eventList = events.Distinct(StringComparer.Ordinal).ToList();
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 150
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || url.Length > 500
            || eventList.Count == 0 || eventList.Any(e => !WebhookEvents.All.Contains(e)))
        {
            throw new DomainException(InvalidCode, "Webhooks need a name, an https URL and at least one known event.");
        }

        Name = name.Trim();
        Url = url;
        Events = eventList;
        IsActive = isActive;
    }

    public void RotateSecret(string protectedSecret) => ProtectedSecret = protectedSecret;
}

/// <summary>Outbox row for one webhook delivery, retried with backoff.</summary>
public sealed class WebhookDelivery : Entity<Guid>
{
    public const int MaxAttempts = 8;

    private WebhookDelivery()
    {
        EventType = string.Empty;
        Payload = string.Empty;
    }

    private WebhookDelivery(Guid id)
        : base(id)
    {
        EventType = string.Empty;
        Payload = string.Empty;
    }

    public Guid SubscriptionId { get; private set; }

    public string EventType { get; private set; }

    public string Payload { get; private set; }

    public bool Delivered { get; private set; }

    public bool Failed { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public int? LastStatusCode { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public static WebhookDelivery Queue(Guid subscriptionId, string eventType, string payload, DateTimeOffset now) =>
        new(Guid.CreateVersion7()) { SubscriptionId = subscriptionId, EventType = eventType, Payload = payload, NextAttemptAt = now, CreatedAt = now };

    public void MarkDelivered(int statusCode, DateTimeOffset now)
    {
        Attempts++;
        Delivered = true;
        LastStatusCode = statusCode;
        LastError = null;
        DeliveredAt = now;
    }

    public void MarkAttemptFailed(int? statusCode, string error, DateTimeOffset now)
    {
        Attempts++;
        LastStatusCode = statusCode;
        LastError = error.Length > 1000 ? error[..1000] : error;
        if (Attempts >= MaxAttempts)
        {
            Failed = true;
            return;
        }

        NextAttemptAt = now.AddMinutes(Math.Pow(2, Attempts - 1));
    }

    public void Retry(DateTimeOffset now)
    {
        Failed = false;
        Delivered = false;
        NextAttemptAt = now;
    }
}

public enum SettingKind
{
    Boolean,
    Number,
    Text,
}

/// <summary>A system setting (feature toggle or tunable) from the code-defined catalog.</summary>
public sealed class SystemSetting : Entity<string>
{
    private SystemSetting()
    {
        Value = string.Empty;
    }

    private SystemSetting(string key)
        : base(key)
    {
        Value = string.Empty;
    }

    public string Value { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static SystemSetting Create(string key, string value, DateTimeOffset now, Guid? by) => new(key) { Value = value, UpdatedAt = now, UpdatedBy = by };

    public void Set(string value, DateTimeOffset now, Guid? by)
    {
        Value = value;
        UpdatedAt = now;
        UpdatedBy = by;
    }
}

/// <summary>The settings catalog: key, kind, default and whether anonymous clients may read it.</summary>
public static class SystemSettings
{
    public const string PortalRegistrationEnabled = "portal.registration_enabled";
    public const string LiveChatEnabled = "chat.enabled";
    public const string WebFormEnabled = "webform.enabled";
    public const string ChatbotEnabled = "chatbot.enabled";
    public const string AiAgentAssistEnabled = "ai.agent_assist_enabled";
    public const string AutoCloseResolvedDays = "tickets.auto_close_resolved_days";

    public static IReadOnlyList<(string Key, SettingKind Kind, string Default, bool Public)> Catalog { get; } =
    [
        (PortalRegistrationEnabled, SettingKind.Boolean, "true", true),
        (LiveChatEnabled, SettingKind.Boolean, "true", true),
        (WebFormEnabled, SettingKind.Boolean, "true", true),
        (ChatbotEnabled, SettingKind.Boolean, "true", true),
        (AiAgentAssistEnabled, SettingKind.Boolean, "true", false),
        (AutoCloseResolvedDays, SettingKind.Number, "7", false),
    ];
}
