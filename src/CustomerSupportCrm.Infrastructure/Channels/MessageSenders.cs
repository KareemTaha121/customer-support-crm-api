using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using CustomerSupportCrm.Application.Abstractions.Channels;
using CustomerSupportCrm.Domain.Tickets;
using Microsoft.Extensions.Options;

namespace CustomerSupportCrm.Infrastructure.Channels;

/// <summary>
/// SMTP delivery via System.Net.Mail (sufficient for relay services such as SES, SendGrid or
/// Microsoft 365 SMTP). Sends multipart text + HTML.
/// </summary>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IMessageSender
{
    private readonly EmailOptions _options = options.Value;

    public TicketChannel Channel => TicketChannel.Email;

    public bool IsConfigured => _options.Enabled && !string.IsNullOrWhiteSpace(_options.Host) && !string.IsNullOrWhiteSpace(_options.FromAddress);

    public async Task<SendResult> SendAsync(OutboundEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        MailAddress to;
        try
        {
            to = new MailAddress(message.To);
        }
        catch (FormatException)
        {
            return SendResult.Failed("Invalid recipient address.", permanent: true);
        }

        using var mail = new MailMessage(new MailAddress(_options.FromAddress!, _options.FromName), to)
        {
            Subject = message.Subject ?? string.Empty,
            SubjectEncoding = Encoding.UTF8,
            Body = message.Body,
            BodyEncoding = Encoding.UTF8,
        };

        if (message.HtmlBody is not null)
        {
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, Encoding.UTF8, "text/html"));
        }

        using var client = new SmtpClient(_options.Host, _options.Port) { EnableSsl = _options.UseSsl };
        if (!string.IsNullOrEmpty(_options.Username))
        {
            client.Credentials = new NetworkCredential(_options.Username, _options.Password);
        }

        await client.SendMailAsync(mail, cancellationToken);
        return SendResult.Sent(null);
    }
}

/// <summary>WhatsApp Cloud API text messages (within the 24-hour customer service window).</summary>
internal sealed class WhatsAppSender(HttpClient http, IOptions<WhatsAppOptions> options) : IMessageSender
{
    private readonly WhatsAppOptions _options = options.Value;

    public TicketChannel Channel => TicketChannel.WhatsApp;

    public bool IsConfigured => _options.Enabled && !string.IsNullOrWhiteSpace(_options.PhoneNumberId) && !string.IsNullOrWhiteSpace(_options.AccessToken);

    public async Task<SendResult> SendAsync(OutboundEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{_options.ApiBaseUrl.TrimEnd('/')}/{_options.PhoneNumberId}/messages"))
        {
            Content = JsonContent.Create(new
            {
                messaging_product = "whatsapp",
                recipient_type = "individual",
                to = message.To.TrimStart('+'),
                type = "text",
                text = new { preview_url = false, body = message.Body },
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

        using var response = await http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // 4xx (bad number, expired window) will not succeed on retry; 5xx and 429 may.
            var permanent = (int)response.StatusCode is >= 400 and < 500 and not 429;
            return SendResult.Failed($"WhatsApp API {(int)response.StatusCode}: {Truncate(content)}", permanent);
        }

        using var json = JsonDocument.Parse(content);
        var id = json.RootElement.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0
            ? messages[0].GetProperty("id").GetString()
            : null;
        return SendResult.Sent(id);
    }

    private static string Truncate(string value) => value.Length > 500 ? value[..500] : value;
}

/// <summary>Twilio-compatible SMS (Messages resource, basic auth).</summary>
internal sealed class TwilioSmsSender(HttpClient http, IOptions<SmsOptions> options) : IMessageSender
{
    private readonly SmsOptions _options = options.Value;

    public TicketChannel Channel => TicketChannel.Sms;

    public bool IsConfigured => _options.Enabled && !string.IsNullOrWhiteSpace(_options.AccountSid) && !string.IsNullOrWhiteSpace(_options.AuthToken) && !string.IsNullOrWhiteSpace(_options.FromNumber);

    public async Task<SendResult> SendAsync(OutboundEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{_options.ApiBaseUrl.TrimEnd('/')}/2010-04-01/Accounts/{_options.AccountSid}/Messages.json"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = message.To,
                ["From"] = _options.FromNumber!,
                ["Body"] = message.Body.Length > 1600 ? message.Body[..1600] : message.Body,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}")));

        using var response = await http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var permanent = (int)response.StatusCode is >= 400 and < 500 and not 429;
            return SendResult.Failed($"SMS API {(int)response.StatusCode}: {(content.Length > 500 ? content[..500] : content)}", permanent);
        }

        using var json = JsonDocument.Parse(content);
        return SendResult.Sent(json.RootElement.TryGetProperty("sid", out var sid) ? sid.GetString() : null);
    }
}
