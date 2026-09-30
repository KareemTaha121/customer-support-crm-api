using System.Security.Cryptography;
using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Users;

namespace CustomerSupportCrm.Domain.Tickets;

public enum OutboundStatus
{
    Pending,
    Sent,
    Failed,
}

/// <summary>
/// Transactional outbox for external deliveries (email, WhatsApp, SMS). Written in the same
/// transaction as the change that caused it; a background dispatcher sends with retries.
/// </summary>
public sealed class OutboundMessage : Entity<Guid>
{
    public const int MaxAttempts = 6;

    private OutboundMessage()
    {
        To = string.Empty;
        Body = string.Empty;
    }

    private OutboundMessage(Guid id)
        : base(id)
    {
        To = string.Empty;
        Body = string.Empty;
    }

    public TicketChannel Channel { get; private set; }

    public string To { get; private set; }

    public string? Subject { get; private set; }

    public string Body { get; private set; }

    public string? HtmlBody { get; private set; }

    public Guid? TicketId { get; private set; }

    public Guid? TicketMessageId { get; private set; }

    public OutboundStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    public string? ProviderMessageId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public static OutboundMessage Queue(TicketChannel channel, string to, string? subject, string body, string? htmlBody, Guid? ticketId, Guid? ticketMessageId, DateTimeOffset now)
    {
        if (channel is not (TicketChannel.Email or TicketChannel.WhatsApp or TicketChannel.Sms))
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Only email, WhatsApp and SMS are delivered through the outbox.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return new OutboundMessage(Guid.CreateVersion7())
        {
            Channel = channel,
            To = to,
            Subject = subject,
            Body = body,
            HtmlBody = htmlBody,
            TicketId = ticketId,
            TicketMessageId = ticketMessageId,
            Status = OutboundStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now,
        };
    }

    public void MarkSent(string? providerMessageId, DateTimeOffset now)
    {
        Status = OutboundStatus.Sent;
        ProviderMessageId = providerMessageId;
        SentAt = now;
        Attempts++;
        LastError = null;
    }

    /// <summary>Exponential backoff (1, 2, 4, 8, 16 minutes); gives up after <see cref="MaxAttempts"/>.</summary>
    public void MarkFailed(string error, DateTimeOffset now, bool permanent)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
        if (permanent || Attempts >= MaxAttempts)
        {
            Status = OutboundStatus.Failed;
            return;
        }

        NextAttemptAt = now.AddMinutes(Math.Pow(2, Attempts - 1));
    }

    public void Retry(DateTimeOffset now)
    {
        Status = OutboundStatus.Pending;
        NextAttemptAt = now;
    }
}

/// <summary>Deduplicates inbound provider messages (webhooks are delivered at least once).</summary>
public sealed class InboundReceipt : Entity<Guid>
{
    private InboundReceipt()
    {
        ExternalId = string.Empty;
    }

    private InboundReceipt(Guid id)
        : base(id)
    {
        ExternalId = string.Empty;
    }

    public TicketChannel Channel { get; private set; }

    public string ExternalId { get; private set; }

    public Guid? TicketId { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public static InboundReceipt Record(TicketChannel channel, string externalId, Guid? ticketId, DateTimeOffset now) =>
        new(Guid.CreateVersion7()) { Channel = channel, ExternalId = externalId, TicketId = ticketId, ReceivedAt = now };
}

public enum ChatStatus
{
    Waiting,
    Active,
    Closed,
}

/// <summary>
/// A live-chat session. Each conversation is backed by a ticket (channel Chat) whose messages
/// hold the transcript; the visitor proves ownership with a per-conversation token.
/// </summary>
public sealed class ChatConversation : Entity<Guid>
{
    private ChatConversation()
    {
        VisitorName = string.Empty;
        AccessTokenHash = string.Empty;
    }

    private ChatConversation(Guid id)
        : base(id)
    {
        VisitorName = string.Empty;
        AccessTokenHash = string.Empty;
    }

    public Guid TicketId { get; private set; }

    public Guid CustomerId { get; private set; }

    public string VisitorName { get; private set; }

    public string AccessTokenHash { get; private set; }

    public ChatStatus Status { get; private set; }

    public UserId? AgentId { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public DateTimeOffset LastMessageAt { get; private set; }

    public static ChatConversation Start(Guid ticketId, Guid customerId, string visitorName, string accessTokenHash, DateTimeOffset now) =>
        new(Guid.CreateVersion7())
        {
            TicketId = ticketId,
            CustomerId = customerId,
            VisitorName = visitorName.Trim(),
            AccessTokenHash = accessTokenHash,
            Status = ChatStatus.Waiting,
            StartedAt = now,
            LastMessageAt = now,
        };

    public static string NewAccessToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    public void Accept(UserId agentId, DateTimeOffset now)
    {
        if (Status == ChatStatus.Closed)
        {
            throw new DomainException("CHAT_CLOSED", "The conversation is closed.");
        }

        AgentId = agentId;
        Status = ChatStatus.Active;
        AcceptedAt ??= now;
    }

    public void Touch(DateTimeOffset now)
    {
        if (Status == ChatStatus.Closed)
        {
            throw new DomainException("CHAT_CLOSED", "The conversation is closed.");
        }

        LastMessageAt = now;
    }

    public void Close(DateTimeOffset now)
    {
        Status = ChatStatus.Closed;
        ClosedAt ??= now;
    }
}
