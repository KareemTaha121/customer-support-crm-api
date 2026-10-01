using CustomerSupportCrm.Contracts.Common;

namespace CustomerSupportCrm.Contracts.Tickets;

/// <param name="Priority">Low, Medium, High or Urgent.</param>
/// <param name="Channel">Defaults to Agent (created by staff). Phone for logged calls.</param>
/// <param name="BranchId">Defaults to the customer's branch.</param>
public sealed record CreateTicketRequest(
    Guid CustomerId,
    string Subject,
    string? Description,
    Guid? CategoryId,
    string Priority,
    string? Channel,
    Guid? BranchId,
    Guid? DepartmentId,
    IReadOnlyList<string>? Tags,
    Guid? AssignedAgentId);

public sealed record UpdateTicketRequest(string Subject, string? Description, Guid? CategoryId, string Priority, IReadOnlyList<string>? Tags);

/// <param name="AgentId">Null unassigns.</param>
public sealed record AssignTicketRequest(Guid? AgentId);

public sealed record ChangeTicketStatusRequest(string Status);

public sealed record EscalateTicketRequest(string Reason);

public sealed record TransferTicketRequest(Guid BranchId, Guid? DepartmentId);

/// <param name="AttachmentIds">Files uploaded to the ticket beforehand, sent with this message.</param>
public sealed record AddTicketMessageRequest(string Body, bool IsInternal, IReadOnlyList<Guid>? MentionedUserIds, IReadOnlyList<Guid>? AttachmentIds);

/// <param name="SlaState">none, ok, warning or breached.</param>
public sealed record TicketListItemResponse(
    Guid Id,
    string Number,
    string Subject,
    string Status,
    string Priority,
    string Channel,
    Guid CustomerId,
    string CustomerName,
    Guid? CategoryId,
    string? CategoryName,
    Guid? AssignedAgentId,
    string? AssignedAgentName,
    Guid BranchId,
    Guid? DepartmentId,
    string? DepartmentName,
    string SlaState,
    DateTimeOffset? FirstResponseDueAt,
    DateTimeOffset? ResolutionDueAt,
    int EscalationLevel,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? LastCustomerMessageAt);

public sealed record TicketSlaResponse(
    Guid? PolicyId,
    string? PolicyName,
    DateTimeOffset? FirstResponseDueAt,
    DateTimeOffset? FirstRespondedAt,
    bool FirstResponseBreached,
    DateTimeOffset? ResolutionDueAt,
    DateTimeOffset? ResolvedAt,
    bool ResolutionBreached,
    string State);

public sealed record TicketCustomerResponse(Guid Id, string Number, string Name, string? Email, string? Phone, string PreferredLanguage);

public sealed record TicketResponse(
    Guid Id,
    string Number,
    string Subject,
    string Description,
    string Status,
    IReadOnlyList<string> AllowedStatuses,
    string Priority,
    string Channel,
    string? ReplyAddress,
    TicketCustomerResponse Customer,
    Guid? CategoryId,
    string? CategoryName,
    Guid? AssignedAgentId,
    string? AssignedAgentName,
    Guid BranchId,
    string BranchName,
    Guid? DepartmentId,
    string? DepartmentName,
    IReadOnlyList<string> Tags,
    TicketSlaResponse Sla,
    int EscalationLevel,
    DateTimeOffset? EscalatedAt,
    int? SatisfactionRating,
    string? SatisfactionComment,
    DateTimeOffset? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record TicketMessageResponse(
    Guid Id,
    string AuthorType,
    Guid? AuthorUserId,
    Guid? AuthorCustomerId,
    string AuthorName,
    string Body,
    bool IsInternal,
    string Channel,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AttachmentResponse> Attachments,
    TicketMessageDeliveryResponse? Delivery = null);

/// <summary>
/// Delivery of an agent reply through the outbox (staff lists only). <paramref name="Channel"/> is the
/// delivery channel, which can differ from the message channel (a portal reply is emailed).
/// <paramref name="LastError"/> is filled only for <c>channels.manage</c>.
/// </summary>
public sealed record TicketMessageDeliveryResponse(
    Guid OutboundMessageId,
    string Channel,
    string Status,
    int Attempts,
    DateTimeOffset? SentAt,
    bool ChannelConfigured,
    string? LastError);

public sealed record TicketHistoryResponse(Guid Id, string Action, string? OldValue, string? NewValue, string? ActorName, DateTimeOffset OccurredAt);

public sealed record TicketCategoryRequest(string Name, string? NameAr, Guid? ParentId, Guid? DefaultDepartmentId, string? DefaultPriority, int SortOrder, bool IsActive = true);

public sealed record TicketCategoryResponse(
    Guid Id,
    string Name,
    string? NameAr,
    Guid? ParentId,
    Guid? DefaultDepartmentId,
    string? DefaultPriority,
    bool IsActive,
    int SortOrder);
