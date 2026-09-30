using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Users;

namespace CustomerSupportCrm.Domain.Tickets;

public sealed record TicketCreatedDomainEvent(Guid TicketId, Guid CustomerId, TicketChannel Channel) : IDomainEvent;

public sealed record TicketAssignedDomainEvent(Guid TicketId, UserId? PreviousAgentId, UserId? AgentId) : IDomainEvent;

public sealed record TicketStatusChangedDomainEvent(Guid TicketId, TicketStatus PreviousStatus, TicketStatus Status) : IDomainEvent;

public sealed record TicketPriorityChangedDomainEvent(Guid TicketId, TicketPriority PreviousPriority, TicketPriority Priority) : IDomainEvent;

public sealed record TicketCategoryChangedDomainEvent(Guid TicketId, Guid? PreviousCategoryId, Guid? CategoryId) : IDomainEvent;

public sealed record TicketTransferredDomainEvent(Guid TicketId, Guid? PreviousDepartmentId, Guid? DepartmentId) : IDomainEvent;

public sealed record TicketEscalatedDomainEvent(Guid TicketId, int Level, string Reason, bool Automatic) : IDomainEvent;

public sealed record TicketMessageAddedDomainEvent(Guid TicketId, Guid MessageId, MessageAuthorType AuthorType, bool IsInternal) : IDomainEvent;

public sealed record TicketSlaWarningDomainEvent(Guid TicketId, SlaTarget Target, DateTimeOffset DueAt) : IDomainEvent;

public sealed record TicketSlaBreachedDomainEvent(Guid TicketId, SlaTarget Target) : IDomainEvent;

public sealed record TicketFeedbackSubmittedDomainEvent(Guid TicketId, int Rating) : IDomainEvent;
