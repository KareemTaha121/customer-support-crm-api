using System.Text.Json;
using CustomerSupportCrm.Application.Abstractions.Authentication;
using CustomerSupportCrm.Application.Abstractions.Persistence;
using CustomerSupportCrm.Application.Common.Authorization;
using CustomerSupportCrm.Application.Common.Exceptions;
using CustomerSupportCrm.Contracts.Customers;
using CustomerSupportCrm.Domain.Customers;
using CustomerSupportCrm.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCrm.Application.Features.Customers.Common;

public static class CustomerErrors
{
    public const string CustomerNotFound = "CUSTOMER_NOT_FOUND";
    public const string DuplicateCustomer = "DUPLICATE_CUSTOMER";
    public const string NoteNotFound = "NOTE_NOT_FOUND";
    public const string NoteEditForbidden = "NOTE_EDIT_FORBIDDEN";
    public const string HasOpenTickets = "CUSTOMER_HAS_OPEN_TICKETS";
}

internal static class CustomerQueries
{
    public static NotFoundException NotFound() => new(CustomerErrors.CustomerNotFound, "The customer was not found.");

    /// <summary>Loads a tracked customer the caller may access, with contacts.</summary>
    public static async Task<Customer> LoadAsync(IApplicationDbContext db, AccessScope scope, Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.Include(c => c.Contacts).SingleOrDefaultAsync(c => c.Id == customerId, cancellationToken)
            ?? throw NotFound();
        scope.EnsureAccess(customer, CustomerErrors.CustomerNotFound, "The customer was not found.");
        return customer;
    }

    /// <summary>Existence + scope check without loading the aggregate.</summary>
    public static async Task EnsureAccessibleAsync(IApplicationDbContext db, AccessScope scope, Guid customerId, CancellationToken cancellationToken)
    {
        var accessible = await db.Customers.AsNoTracking().Where(c => c.Id == customerId).WhereInScope(scope).AnyAsync(cancellationToken);
        if (!accessible)
        {
            throw NotFound();
        }
    }

    public static IQueryable<CustomerListItemResponse> ProjectListItems(this IQueryable<Customer> customers, IApplicationDbContext db) =>
        customers.Select(c => new CustomerListItemResponse(
            c.Id,
            c.Number,
            c.Type.ToString(),
            c.Name,
            c.CompanyName,
            c.PrimaryEmail,
            c.PrimaryPhone,
            c.Status.ToString(),
            c.BranchId,
            db.Branches.Where(b => b.Id == c.BranchId).Select(b => b.Name).FirstOrDefault() ?? string.Empty,
            c.DepartmentId,
            db.Departments.Where(d => d.Id == c.DepartmentId).Select(d => d.Name).FirstOrDefault(),
            c.Tags,
            db.Tickets.Count(t => t.CustomerId == c.Id && t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed),
            c.CreatedAt));

    public static async Task<CustomerResponse> GetResponseAsync(IApplicationDbContext db, AccessScope scope, Guid customerId, CancellationToken cancellationToken)
    {
        var row = await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .WhereInScope(scope)
            .Select(c => new
            {
                c.Id,
                c.Number,
                c.Type,
                c.Name,
                c.CompanyName,
                c.PreferredLanguage,
                c.Status,
                c.BranchId,
                BranchName = db.Branches.Where(b => b.Id == c.BranchId).Select(b => b.Name).FirstOrDefault(),
                c.DepartmentId,
                DepartmentName = db.Departments.Where(d => d.Id == c.DepartmentId).Select(d => d.Name).FirstOrDefault(),
                c.Tags,
                c.ExternalSystem,
                c.ExternalId,
                Contacts = c.Contacts.OrderBy(x => x.Type).ThenByDescending(x => x.IsPrimary).Select(x => new { x.Id, x.Type, x.Value, x.Label, x.IsPrimary }).ToList(),
                OpenTickets = db.Tickets.Count(t => t.CustomerId == c.Id && t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed),
                TotalTickets = db.Tickets.Count(t => t.CustomerId == c.Id),
                LastInteractionAt = db.CustomerActivities.Where(a => a.CustomerId == c.Id).Max(a => (DateTimeOffset?)a.OccurredAt),
                AverageSatisfaction = db.Tickets.Where(t => t.CustomerId == c.Id && t.SatisfactionRating != null).Average(t => (decimal?)t.SatisfactionRating),
                HasPortalAccount = db.CustomerAccounts.Any(a => a.CustomerId == c.Id && a.IsActive),
                c.CreatedAt,
                c.UpdatedAt,
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound();

        return new CustomerResponse(
            row.Id,
            row.Number,
            row.Type.ToString(),
            row.Name,
            row.CompanyName,
            row.PreferredLanguage,
            row.Status.ToString(),
            row.BranchId,
            row.BranchName ?? string.Empty,
            row.DepartmentId,
            row.DepartmentName,
            row.Tags,
            row.ExternalSystem,
            row.ExternalId,
            [.. row.Contacts.Select(x => new CustomerContactResponse(x.Id, x.Type.ToString(), x.Value, x.Label, x.IsPrimary))],
            new CustomerStatsResponse(row.OpenTickets, row.TotalTickets, row.LastInteractionAt, row.AverageSatisfaction is { } avg ? Math.Round(avg, 2) : null),
            row.HasPortalAccount,
            row.CreatedAt,
            row.UpdatedAt);
    }

    /// <summary>
    /// Other customers (organization-wide, regardless of scope) sharing any of the given
    /// email/phone values. Duplicates are detected across branches on purpose.
    /// </summary>
    public static async Task<IReadOnlyList<DuplicateCandidateResponse>> FindDuplicatesAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<string> emails,
        IReadOnlyCollection<string> phones,
        Guid? excludeCustomerId,
        CancellationToken cancellationToken)
    {
        if (emails.Count == 0 && phones.Count == 0)
        {
            return [];
        }

        var emailList = emails.ToList();
        var phoneList = phones.ToList();
        var matches = await db.Customers.AsNoTracking()
            .Where(c => c.Id != excludeCustomerId)
            .SelectMany(c => c.Contacts
                .Where(x => (x.Type == ContactType.Email && emailList.Contains(x.Value))
                    || ((x.Type == ContactType.Phone || x.Type == ContactType.WhatsApp) && phoneList.Contains(x.Value)))
                .Select(x => new { c.Id, c.Number, c.Name, x.Type, x.Value }))
            .Take(20)
            .ToListAsync(cancellationToken);

        return
        [
            .. matches.DistinctBy(m => (m.Id, m.Value)).Select(m => new DuplicateCandidateResponse(
                m.Id,
                m.Number,
                m.Name,
                m.Type == ContactType.Email ? "Email" : "Phone",
                m.Value)),
        ];
    }

    public static (List<string> Emails, List<string> Phones) MatchableValues(IEnumerable<(ContactType Type, string Value)> contacts)
    {
        var list = contacts.ToList();
        return (
            [.. list.Where(c => c.Type == ContactType.Email).Select(c => c.Value).Distinct()],
            [.. list.Where(c => c.Type is ContactType.Phone or ContactType.WhatsApp).Select(c => c.Value).Distinct()]);
    }

    public static JsonElement? ParseJson(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

/// <summary>Writes customer timeline entries in the current unit of work.</summary>
public sealed class CustomerTimeline(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Record(Guid customerId, string type, string summary, Guid? ticketId = null, object? data = null) =>
        db.CustomerActivities.Add(CustomerActivity.Record(
            customerId,
            type,
            summary,
            currentUser.IsAuthenticated ? currentUser.UserId : null,
            ticketId,
            data is null ? null : JsonSerializer.Serialize(data, JsonOptions),
            time.GetUtcNow()));
}
