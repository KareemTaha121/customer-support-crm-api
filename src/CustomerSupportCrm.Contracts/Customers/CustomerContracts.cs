using System.Text.Json;
using CustomerSupportCrm.Contracts.Common;

namespace CustomerSupportCrm.Contracts.Customers;

/// <param name="Type">Email, Phone, WhatsApp, Address or Other. Phones must be E.164 (+9665...).</param>
public sealed record CustomerContactRequest(string Type, string Value, string? Label, bool IsPrimary);

/// <param name="Type">Individual or Company.</param>
/// <param name="IgnoreDuplicates">Create even when another customer has the same email/phone.</param>
public sealed record CreateCustomerRequest(
    string Type,
    string Name,
    string? CompanyName,
    string PreferredLanguage,
    Guid BranchId,
    Guid? DepartmentId,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<CustomerContactRequest>? Contacts,
    bool IgnoreDuplicates = false);

/// <param name="Status">Active or Inactive.</param>
public sealed record UpdateCustomerRequest(
    string Type,
    string Name,
    string? CompanyName,
    string PreferredLanguage,
    string Status,
    Guid BranchId,
    Guid? DepartmentId,
    IReadOnlyList<string>? Tags);

public sealed record CustomerContactResponse(Guid Id, string Type, string Value, string? Label, bool IsPrimary);

public sealed record CustomerListItemResponse(
    Guid Id,
    string Number,
    string Type,
    string Name,
    string? CompanyName,
    string? PrimaryEmail,
    string? PrimaryPhone,
    string Status,
    Guid BranchId,
    string BranchName,
    Guid? DepartmentId,
    string? DepartmentName,
    IReadOnlyList<string> Tags,
    int OpenTickets,
    DateTimeOffset CreatedAt);

public sealed record CustomerStatsResponse(int OpenTickets, int TotalTickets, DateTimeOffset? LastInteractionAt, decimal? AverageSatisfaction);

public sealed record CustomerResponse(
    Guid Id,
    string Number,
    string Type,
    string Name,
    string? CompanyName,
    string PreferredLanguage,
    string Status,
    Guid BranchId,
    string BranchName,
    Guid? DepartmentId,
    string? DepartmentName,
    IReadOnlyList<string> Tags,
    string? ExternalSystem,
    string? ExternalId,
    IReadOnlyList<CustomerContactResponse> Contacts,
    CustomerStatsResponse Stats,
    bool HasPortalAccount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <param name="MatchedOn">Email or Phone.</param>
public sealed record DuplicateCandidateResponse(Guid Id, string Number, string Name, string MatchedOn, string MatchedValue);

public sealed record CustomerNoteRequest(string Body, bool IsPinned = false);

public sealed record CustomerNoteResponse(
    Guid Id,
    string Body,
    bool IsPinned,
    Guid AuthorId,
    string AuthorName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    bool CanEdit);

public sealed record CustomerActivityResponse(
    Guid Id,
    string Type,
    string Summary,
    Guid? ActorUserId,
    string? ActorName,
    Guid? TicketId,
    JsonElement? Data,
    DateTimeOffset OccurredAt);

public sealed record CustomerAttachmentsResponse(IReadOnlyList<AttachmentResponse> Items);
