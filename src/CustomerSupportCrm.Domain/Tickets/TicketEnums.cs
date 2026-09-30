namespace CustomerSupportCrm.Domain.Tickets;

public enum TicketStatus
{
    New,
    Open,
    PendingCustomer,
    PendingInternal,
    Escalated,
    Resolved,
    Closed,
}

public enum TicketPriority
{
    Low,
    Medium,
    High,
    Urgent,
}

public enum TicketChannel
{
    Agent,
    Portal,
    WebForm,
    Email,
    WhatsApp,
    Sms,
    Chat,
    Phone,
    Api,
}

public enum MessageAuthorType
{
    Agent,
    Customer,
    System,
}

public enum SlaTarget
{
    FirstResponse,
    Resolution,
}

public static class TicketStatusExtensions
{
    public static bool IsActive(this TicketStatus status) => status is not (TicketStatus.Resolved or TicketStatus.Closed);
}
