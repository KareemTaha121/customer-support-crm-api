using CustomerSupportCrm.Domain.Tickets;

namespace CustomerSupportCrm.Application.Abstractions.Channels;

public sealed record OutboundEnvelope(string To, string? Subject, string Body, string? HtmlBody);

/// <param name="Permanent">The provider rejected the message (bad address, unconfigured); do not retry.</param>
public sealed record SendResult(bool Succeeded, string? ProviderMessageId, string? Error, bool Permanent)
{
    public static SendResult Sent(string? providerMessageId) => new(true, providerMessageId, null, false);

    public static SendResult Failed(string error, bool permanent) => new(false, null, error, permanent);
}

/// <summary>Provider adapter for one outbound channel. Implementations live in Infrastructure.</summary>
public interface IMessageSender
{
    TicketChannel Channel { get; }

    bool IsConfigured { get; }

    Task<SendResult> SendAsync(OutboundEnvelope message, CancellationToken cancellationToken);
}

/// <summary>
/// A provider message normalized to one shape (email, WhatsApp, SMS all look alike to the
/// application). <see cref="From"/> is an email address or an E.164 phone.
/// </summary>
public sealed record InboundChannelMessage(
    TicketChannel Channel,
    string ExternalId,
    string From,
    string? FromName,
    string? To,
    string? Subject,
    string Body,
    DateTimeOffset ReceivedAt);

/// <summary>Links in customer-facing messages (portal URLs).</summary>
public sealed class CustomerPortalOptions
{
    public const string SectionName = "Portal";

    /// <summary>Absolute base URL of the customer portal, e.g. https://support.example.com/portal.</summary>
    public string BaseUrl { get; init; } = "http://localhost:4200/portal";
}
