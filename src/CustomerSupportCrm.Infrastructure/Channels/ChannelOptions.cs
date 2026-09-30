namespace CustomerSupportCrm.Infrastructure.Channels;

/// <summary>SMTP delivery and the shared secret for the inbound email webhook.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Channels:Email";

    public bool Enabled { get; init; }

    public string? Host { get; init; }

    public int Port { get; init; } = 587;

    public bool UseSsl { get; init; } = true;

    public string? Username { get; init; }

    public string? Password { get; init; }

    public string? FromAddress { get; init; }

    public string FromName { get; init; } = "Customer Support";

    /// <summary>Required in the X-Webhook-Secret header of inbound email posts.</summary>
    public string? InboundSecret { get; init; }
}

/// <summary>WhatsApp Business Cloud API (Meta Graph).</summary>
public sealed class WhatsAppOptions
{
    public const string SectionName = "Channels:WhatsApp";

    public bool Enabled { get; init; }

    public string? PhoneNumberId { get; init; }

    public string? AccessToken { get; init; }

    /// <summary>App secret for X-Hub-Signature-256 webhook signatures.</summary>
    public string? AppSecret { get; init; }

    /// <summary>Token echoed during webhook subscription.</summary>
    public string? VerifyToken { get; init; }

    public string ApiBaseUrl { get; init; } = "https://graph.facebook.com/v20.0";
}

/// <summary>Twilio-compatible SMS provider.</summary>
public sealed class SmsOptions
{
    public const string SectionName = "Channels:Sms";

    public bool Enabled { get; init; }

    public string? AccountSid { get; init; }

    public string? AuthToken { get; init; }

    public string? FromNumber { get; init; }

    public string ApiBaseUrl { get; init; } = "https://api.twilio.com";

    /// <summary>Public URL of the inbound webhook, exactly as configured at the provider (signature base).</summary>
    public string? InboundWebhookUrl { get; init; }
}
