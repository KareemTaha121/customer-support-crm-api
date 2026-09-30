using CustomerSupportCrm.Contracts.Common;

namespace CustomerSupportCrm.Contracts.Portal;

public sealed record PortalRegisterRequest(string Name, string Email, string Password, string? Phone, string? Language);

public sealed record PortalVerifyRequest(string Email, string Code);

public sealed record PortalEmailRequest(string Email);

public sealed record PortalLoginRequest(string Email, string Password);

public sealed record PortalProfileResponse(Guid AccountId, Guid CustomerId, string CustomerNumber, string Name, string Email, string Language);

/// <summary>Portal tokens are not refreshed; sign in again after <see cref="ExpiresAt"/>.</summary>
public sealed record PortalSessionResponse(string AccessToken, DateTimeOffset ExpiresAt, PortalProfileResponse Profile);

public sealed record PortalUpdateProfileRequest(string Name, string Language);

public sealed record PortalChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record PortalCreateTicketRequest(string Subject, string Message, Guid? CategoryId);

public sealed record PortalMessageRequest(string Body, IReadOnlyList<Guid>? AttachmentIds);

public sealed record PortalFeedbackRequest(int Rating, string? Comment);

/// <summary>Customer view of a ticket: no internal notes, SLA or routing details.</summary>
public sealed record PortalTicketResponse(
    Guid Id,
    string Number,
    string Subject,
    string Description,
    string Status,
    string? CategoryName,
    string? AgentName,
    bool CanReply,
    bool CanGiveFeedback,
    int? SatisfactionRating,
    string? SatisfactionComment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record PortalTicketListItemResponse(Guid Id, string Number, string Subject, string Status, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, DateTimeOffset? LastAgentReplyAt);

public sealed record PortalMessageResponse(Guid Id, string AuthorType, string AuthorName, string Body, DateTimeOffset CreatedAt, IReadOnlyList<AttachmentResponse> Attachments);

public sealed record PortalCategoryResponse(Guid Id, string Name, string? NameAr);

public sealed record PortalHistoryItemResponse(Guid Id, string Type, string Summary, Guid? TicketId, DateTimeOffset OccurredAt);

/// <summary>Staff grants portal access to a known customer.</summary>
public sealed record GrantPortalAccessRequest(string Email, string Password);
