using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Users;

namespace CustomerSupportCrm.Domain.Customers;

/// <summary>An internal note on a customer. Never exposed to the customer portal.</summary>
public sealed class CustomerNote : Entity<Guid>
{
    public const int BodyMaxLength = 10_000;
    public const string InvalidCode = "INVALID_NOTE";

    private CustomerNote()
    {
        Body = string.Empty;
    }

    private CustomerNote(Guid id)
        : base(id)
    {
        Body = string.Empty;
    }

    public Guid CustomerId { get; private set; }

    public string Body { get; private set; }

    public bool IsPinned { get; private set; }

    public UserId AuthorId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static CustomerNote Create(Guid customerId, UserId authorId, string body, bool isPinned, DateTimeOffset now)
    {
        var note = new CustomerNote(Guid.CreateVersion7()) { CustomerId = customerId, AuthorId = authorId, CreatedAt = now, IsPinned = isPinned };
        note.SetBody(body);
        return note;
    }

    public void Edit(string body, bool isPinned, DateTimeOffset now)
    {
        SetBody(body);
        IsPinned = isPinned;
        UpdatedAt = now;
    }

    private void SetBody(string body)
    {
        var trimmed = body?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > BodyMaxLength)
        {
            throw new DomainException(InvalidCode, "The note is empty or too long.");
        }

        Body = trimmed;
    }
}

/// <summary>
/// One entry of a customer's interaction timeline (tickets, messages, notes, files, profile
/// changes, portal activity). Append-only and denormalized for paged reads.
/// </summary>
public sealed class CustomerActivity : Entity<Guid>
{
    public const int TypeMaxLength = 64;
    public const int SummaryMaxLength = 500;

    private CustomerActivity()
    {
        Type = string.Empty;
        Summary = string.Empty;
    }

    private CustomerActivity(Guid id)
        : base(id)
    {
        Type = string.Empty;
        Summary = string.Empty;
    }

    public Guid CustomerId { get; private set; }

    public string Type { get; private set; }

    public string Summary { get; private set; }

    public UserId? ActorUserId { get; private set; }

    public Guid? TicketId { get; private set; }

    /// <summary>Type-specific JSON (e.g. ticket number, channel) for rendering.</summary>
    public string? Data { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public static CustomerActivity Record(Guid customerId, string type, string summary, UserId? actor, Guid? ticketId, string? data, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        var text = string.IsNullOrWhiteSpace(summary) ? type : summary.Trim();

        return new CustomerActivity(Guid.CreateVersion7())
        {
            CustomerId = customerId,
            Type = type,
            Summary = text.Length > SummaryMaxLength ? text[..SummaryMaxLength] : text,
            ActorUserId = actor,
            TicketId = ticketId,
            Data = data,
            OccurredAt = now,
        };
    }
}

public static class CustomerActivityTypes
{
    public const string CustomerCreated = "customer.created";
    public const string CustomerUpdated = "customer.updated";
    public const string NoteAdded = "note.added";
    public const string AttachmentAdded = "attachment.added";
    public const string TicketCreated = "ticket.created";
    public const string TicketStatusChanged = "ticket.status_changed";
    public const string TicketMessage = "ticket.message";
    public const string TicketFeedback = "ticket.feedback";
    public const string PortalSignIn = "portal.sign_in";
    public const string ChatStarted = "chat.started";
}
