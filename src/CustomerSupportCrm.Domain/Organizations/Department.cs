using CustomerSupportCrm.Domain.Common;

namespace CustomerSupportCrm.Domain.Organizations;

/// <summary>A team inside a branch (e.g. Billing, Technical Support). Deactivated, never deleted.</summary>
public sealed class Department : Entity<Guid>, IAuditableEntity
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 200;
    public const string InvalidCode = "INVALID_DEPARTMENT";

    private Department()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    private Department(Guid id)
        : base(id)
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public Guid BranchId { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    /// <summary>Inbound address routed to this department by the email channel.</summary>
    public string? Email { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static Department Create(Guid branchId, string code, string name, string? email)
    {
        var department = new Department(Guid.CreateVersion7()) { BranchId = branchId, IsActive = true };
        department.Update(code, name, email);
        return department;
    }

    public void Update(string code, string name, string? email)
    {
        var normalizedCode = Branch.NormalizeCode(code ?? string.Empty);
        var trimmedName = name?.Trim();
        if (normalizedCode.Length is 0 or > CodeMaxLength || string.IsNullOrEmpty(trimmedName) || trimmedName.Length > NameMaxLength)
        {
            throw new DomainException(InvalidCode, "The department code or name is not valid.");
        }

        Code = normalizedCode;
        Name = trimmedName;
        Email = string.IsNullOrWhiteSpace(email) ? null : Shared.EmailAddress.Create(email).Value;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
