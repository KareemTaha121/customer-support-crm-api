using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Shared;

namespace CustomerSupportCrm.Domain.Customers;

public enum CustomerType
{
    Individual,
    Company,
}

public enum CustomerStatus
{
    Active,
    Inactive,
}

public enum ContactType
{
    Email,
    Phone,
    WhatsApp,
    Address,
    Other,
}

/// <summary>
/// A customer (person or company) owned by a branch/department. Contact methods belong to the
/// aggregate; notes, attachments and the interaction timeline are separate records.
/// </summary>
public sealed class Customer : AggregateRoot<Guid>, IAuditableEntity, ISoftDeletable, IScopedEntity
{
    public const int NumberMaxLength = 20;
    public const int NameMaxLength = 200;
    public const int MaxTags = 20;
    public const int TagMaxLength = 50;

    public const string InvalidCode = "INVALID_CUSTOMER";
    public const string ContactNotFoundCode = "CONTACT_NOT_FOUND";
    public const string DuplicateContactCode = "DUPLICATE_CONTACT";

    private readonly List<CustomerContact> _contacts = [];

    private Customer()
    {
        Number = string.Empty;
        Name = string.Empty;
        PreferredLanguage = "en";
        Tags = [];
    }

    private Customer(Guid id, string number)
        : base(id)
    {
        Number = number;
        Name = string.Empty;
        PreferredLanguage = "en";
        Tags = [];
    }

    /// <summary>Human-friendly unique number, e.g. C-000042.</summary>
    public string Number { get; private set; }

    public CustomerType Type { get; private set; }

    public string Name { get; private set; }

    /// <summary>For individuals: the company they belong to, if any.</summary>
    public string? CompanyName { get; private set; }

    public string PreferredLanguage { get; private set; }

    public CustomerStatus Status { get; private set; }

    public List<string> Tags { get; private set; }

    /// <summary>Denormalized primary email, maintained from contacts for listing and matching.</summary>
    public string? PrimaryEmail { get; private set; }

    /// <summary>Denormalized primary phone (E.164), maintained from contacts.</summary>
    public string? PrimaryPhone { get; private set; }

    /// <summary>Source system for integrated customers (e.g. an ERP).</summary>
    public string? ExternalSystem { get; private set; }

    public string? ExternalId { get; private set; }

    public Guid BranchId { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public IReadOnlyCollection<CustomerContact> Contacts => _contacts;

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public static string FormatNumber(long sequence) => $"C-{sequence:D6}";

    public static Customer Create(
        string number,
        CustomerType type,
        string name,
        string? companyName,
        string preferredLanguage,
        IEnumerable<string> tags,
        Guid branchId,
        Guid? departmentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);

        var customer = new Customer(Guid.CreateVersion7(), number) { Status = CustomerStatus.Active };
        customer.UpdateProfile(type, name, companyName, preferredLanguage, tags);
        customer.MoveTo(branchId, departmentId);
        customer.Raise(new CustomerCreatedDomainEvent(customer.Id));
        return customer;
    }

    public void UpdateProfile(CustomerType type, string name, string? companyName, string preferredLanguage, IEnumerable<string> tags)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > NameMaxLength)
        {
            throw new DomainException(InvalidCode, "The customer name is not valid.");
        }

        if (preferredLanguage is not ("en" or "ar"))
        {
            throw new DomainException(InvalidCode, "The preferred language is not supported.");
        }

        var cleanTags = tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (cleanTags.Count > MaxTags || cleanTags.Any(t => t.Length > TagMaxLength))
        {
            throw new DomainException(InvalidCode, "Too many or too long tags.");
        }

        Type = type;
        Name = trimmed;
        CompanyName = string.IsNullOrWhiteSpace(companyName) ? null : companyName.Trim();
        PreferredLanguage = preferredLanguage;
        Tags = cleanTags;
    }

    public void MoveTo(Guid branchId, Guid? departmentId)
    {
        if (branchId == Guid.Empty)
        {
            throw new DomainException(InvalidCode, "A customer must belong to a branch.");
        }

        BranchId = branchId;
        DepartmentId = departmentId;
    }

    public void SetStatus(CustomerStatus status) => Status = status;

    public void LinkExternal(string system, string externalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(system);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        ExternalSystem = system.Trim();
        ExternalId = externalId.Trim();
    }

    public CustomerContact AddContact(ContactType type, string value, string? label, bool isPrimary)
    {
        var normalized = CustomerContact.NormalizeValue(type, value);
        if (_contacts.Any(c => c.Type == type && c.Value == normalized))
        {
            throw new DomainException(DuplicateContactCode, "This contact already exists on the customer.");
        }

        var contact = new CustomerContact(Id, type, normalized, label);
        _contacts.Add(contact);

        // The first contact of a kind becomes primary automatically.
        if (isPrimary || !_contacts.Any(c => c.Type == type && c.IsPrimary))
        {
            SetPrimary(contact.Id);
        }

        return contact;
    }

    public void UpdateContact(Guid contactId, string value, string? label)
    {
        var contact = FindContact(contactId);
        var normalized = CustomerContact.NormalizeValue(contact.Type, value);
        if (_contacts.Any(c => c.Id != contactId && c.Type == contact.Type && c.Value == normalized))
        {
            throw new DomainException(DuplicateContactCode, "This contact already exists on the customer.");
        }

        contact.Update(normalized, label);
        RefreshPrimaries();
    }

    public void RemoveContact(Guid contactId)
    {
        var contact = FindContact(contactId);
        _contacts.Remove(contact);

        if (contact.IsPrimary && _contacts.FirstOrDefault(c => c.Type == contact.Type) is { } next)
        {
            next.SetPrimary(true);
        }

        RefreshPrimaries();
    }

    public void SetPrimary(Guid contactId)
    {
        var contact = FindContact(contactId);
        foreach (var other in _contacts.Where(c => c.Type == contact.Type))
        {
            other.SetPrimary(other.Id == contactId);
        }

        RefreshPrimaries();
    }

    private CustomerContact FindContact(Guid contactId) =>
        _contacts.SingleOrDefault(c => c.Id == contactId)
        ?? throw new DomainException(ContactNotFoundCode, "The contact was not found.");

    private void RefreshPrimaries()
    {
        PrimaryEmail = _contacts.FirstOrDefault(c => c.Type == ContactType.Email && c.IsPrimary)?.Value;
        PrimaryPhone = _contacts.FirstOrDefault(c => c.Type == ContactType.Phone && c.IsPrimary)?.Value
            ?? _contacts.FirstOrDefault(c => c.Type == ContactType.WhatsApp && c.IsPrimary)?.Value;
    }
}

public sealed class CustomerContact
{
    public const int ValueMaxLength = 500;
    public const int LabelMaxLength = 100;

    private CustomerContact()
    {
        Value = string.Empty;
    }

    internal CustomerContact(Guid customerId, ContactType type, string value, string? label)
    {
        Id = Guid.CreateVersion7();
        CustomerId = customerId;
        Type = type;
        Value = value;
        Label = NormalizeLabel(label);
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public ContactType Type { get; private set; }

    /// <summary>Normalized: lower-case email, E.164 phone, trimmed text otherwise.</summary>
    public string Value { get; private set; }

    public string? Label { get; private set; }

    public bool IsPrimary { get; private set; }

    /// <summary>Validates per type: email syntax, E.164 phone/WhatsApp, non-empty text.</summary>
    public static string NormalizeValue(ContactType type, string value) => type switch
    {
        ContactType.Email => EmailAddress.Create(value).Value,
        ContactType.Phone or ContactType.WhatsApp => PhoneNumber.Create(value).Value,
        _ => string.IsNullOrWhiteSpace(value) || value.Trim().Length > ValueMaxLength
            ? throw new DomainException(Customer.InvalidCode, "The contact value is not valid.")
            : value.Trim(),
    };

    internal void Update(string value, string? label)
    {
        Value = value;
        Label = NormalizeLabel(label);
    }

    internal void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;

    private static string? NormalizeLabel(string? label)
    {
        var trimmed = label?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed[..Math.Min(trimmed.Length, LabelMaxLength)];
    }
}

public sealed record CustomerCreatedDomainEvent(Guid CustomerId) : IDomainEvent;
