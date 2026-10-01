using CustomerSupportCrm.Application.Abstractions.Channels;
using CustomerSupportCrm.Domain.Tickets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CustomerSupportCrm.Infrastructure.Channels;

/// <summary>
/// Development-only email "delivery": writes the message (including portal verification codes and
/// reset links) to the log and reports it as sent. Selected with <c>Channels:Email:Provider = "Log"</c>.
/// Outside Development it reports "not configured", so messages fail exactly as with no SMTP host.
/// </summary>
internal sealed partial class LogEmailSender(IHostEnvironment environment, ILogger<LogEmailSender> logger) : IMessageSender
{
    public TicketChannel Channel => TicketChannel.Email;

    public bool IsConfigured => environment.IsDevelopment();

    public Task<SendResult> SendAsync(OutboundEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        LogEmail(logger, message.To, message.Subject ?? string.Empty, message.Body);
        return Task.FromResult(SendResult.Sent($"log-{Guid.CreateVersion7()}"));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email (log provider) to {To}: {Subject}\n{Body}")]
    private static partial void LogEmail(ILogger logger, string to, string subject, string body);
}
