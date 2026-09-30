using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustomerSupportCrm.Application.Abstractions.Channels;
using CustomerSupportCrm.Application.Features.Channels;
using CustomerSupportCrm.Domain.Tickets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CustomerSupportCrm.Infrastructure.Channels;

/// <summary>
/// Generic inbound email webhook (JSON). Point an inbound-parse service (SendGrid, Mailgun,
/// Postmark, or a mail-to-webhook bridge) at /api/v1/public/channels/email/webhook with the
/// X-Webhook-Secret header. Body: { messageId, from, fromName, to, subject, text }.
/// </summary>
internal sealed class EmailWebhookAdapter(IOptions<EmailOptions> options, TimeProvider time) : IChannelWebhookAdapter
{
    public const string SecretHeader = "X-Webhook-Secret";

    public TicketChannel Channel => TicketChannel.Email;

    public string? Handshake(HttpRequest request) => null;

    public async Task<IReadOnlyList<InboundChannelMessage>?> ParseAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var secret = options.Value.InboundSecret;
        if (string.IsNullOrEmpty(secret) || !FixedEquals(request.Headers[SecretHeader].ToString(), secret))
        {
            return null;
        }

        var payload = await JsonSerializer.DeserializeAsync<InboundEmail>(request.Body, JsonSerializerOptions.Web, cancellationToken);
        if (payload?.From is null || payload.MessageId is null)
        {
            return [];
        }

        return [new InboundChannelMessage(TicketChannel.Email, payload.MessageId, ExtractAddress(payload.From), payload.FromName, payload.To is null ? null : ExtractAddress(payload.To), payload.Subject, payload.Text ?? string.Empty, time.GetUtcNow())];
    }

    /// <summary>"Jane Doe &lt;jane@example.com&gt;" → jane@example.com.</summary>
    private static string ExtractAddress(string value)
    {
        var start = value.LastIndexOf('<');
        var end = value.LastIndexOf('>');
        return start >= 0 && end > start ? value[(start + 1)..end].Trim() : value.Trim();
    }

    internal static bool FixedEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private sealed record InboundEmail(string? MessageId, string? From, string? FromName, string? To, string? Subject, string? Text);
}

/// <summary>WhatsApp Cloud API webhook: GET subscription handshake, POST signed notifications.</summary>
internal sealed class WhatsAppWebhookAdapter(IOptions<WhatsAppOptions> options, TimeProvider time) : IChannelWebhookAdapter
{
    public TicketChannel Channel => TicketChannel.WhatsApp;

    public string? Handshake(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var verifyToken = options.Value.VerifyToken;
        return request.Query["hub.mode"] == "subscribe"
            && !string.IsNullOrEmpty(verifyToken)
            && EmailWebhookAdapter.FixedEquals(request.Query["hub.verify_token"].ToString(), verifyToken)
                ? request.Query["hub.challenge"].ToString()
                : null;
    }

    public async Task<IReadOnlyList<InboundChannelMessage>?> ParseAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var secret = options.Value.AppSecret;
        if (string.IsNullOrEmpty(secret))
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        var body = buffer.ToArray();

        var expected = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));
        if (!EmailWebhookAdapter.FixedEquals(request.Headers["X-Hub-Signature-256"].ToString(), expected))
        {
            return null;
        }

        using var json = JsonDocument.Parse(body);
        var result = new List<InboundChannelMessage>();
        foreach (var entry in Array(json.RootElement, "entry"))
        {
            foreach (var change in Array(entry, "changes"))
            {
                if (!change.TryGetProperty("value", out var value))
                {
                    continue;
                }

                var names = Array(value, "contacts").ToDictionary(
                    c => c.GetProperty("wa_id").GetString() ?? string.Empty,
                    c => c.TryGetProperty("profile", out var p) && p.TryGetProperty("name", out var n) ? n.GetString() : null);

                foreach (var message in Array(value, "messages"))
                {
                    var from = message.GetProperty("from").GetString() ?? string.Empty;
                    var text = message.TryGetProperty("text", out var t) && t.TryGetProperty("body", out var b)
                        ? b.GetString() ?? string.Empty
                        : $"[{message.GetProperty("type").GetString()} message]";
                    var timestamp = message.TryGetProperty("timestamp", out var ts) && long.TryParse(ts.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                        ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                        : time.GetUtcNow();

                    result.Add(new InboundChannelMessage(TicketChannel.WhatsApp, message.GetProperty("id").GetString()!, "+" + from, names.GetValueOrDefault(from), null, null, text, timestamp));
                }
            }
        }

        return result;
    }

    private static List<JsonElement> Array(JsonElement element, string property) =>
        element.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array ? [.. array.EnumerateArray()] : [];
}

/// <summary>Twilio-compatible inbound SMS (form post, X-Twilio-Signature HMAC-SHA1).</summary>
internal sealed class SmsWebhookAdapter(IOptions<SmsOptions> options, TimeProvider time) : IChannelWebhookAdapter
{
    public TicketChannel Channel => TicketChannel.Sms;

    public string? Handshake(HttpRequest request) => null;

    public async Task<IReadOnlyList<InboundChannelMessage>?> ParseAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = options.Value;
        if (string.IsNullOrEmpty(settings.AuthToken) || string.IsNullOrEmpty(settings.InboundWebhookUrl) || !request.HasFormContentType)
        {
            return null;
        }

        var form = await request.ReadFormAsync(cancellationToken);

        // Signature base: full URL followed by every POST parameter (key + value) sorted by key.
        var data = new StringBuilder(settings.InboundWebhookUrl);
        foreach (var key in form.Keys.Order(StringComparer.Ordinal))
        {
            data.Append(key).Append(form[key].ToString());
        }

#pragma warning disable CA5350 // Twilio's request-signing scheme is defined as HMAC-SHA1.
        var expected = Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(settings.AuthToken), Encoding.UTF8.GetBytes(data.ToString())));
#pragma warning restore CA5350
        if (!EmailWebhookAdapter.FixedEquals(request.Headers["X-Twilio-Signature"].ToString(), expected))
        {
            return null;
        }

        var sid = form["MessageSid"].ToString();
        var from = form["From"].ToString();
        if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(from))
        {
            return [];
        }

        return [new InboundChannelMessage(TicketChannel.Sms, sid, from, null, form["To"].ToString(), null, form["Body"].ToString(), time.GetUtcNow())];
    }
}
