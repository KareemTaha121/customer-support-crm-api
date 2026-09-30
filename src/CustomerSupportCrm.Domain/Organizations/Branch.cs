using CustomerSupportCrm.Domain.Common;

namespace CustomerSupportCrm.Domain.Organizations;

/// <summary>A physical or logical branch of the organization. Deactivated, never deleted.</summary>
public sealed class Branch : Entity<Guid>, IAuditableEntity
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 200;
    public const string InvalidCode = "INVALID_BRANCH";

    private Branch()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    private Branch(Guid id)
        : base(id)
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? Address { get; private set; }

    public string? Phone { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public static Branch Create(string code, string name, string? address, string? phone)
    {
        var branch = new Branch(Guid.CreateVersion7()) { IsActive = true };
        branch.Update(code, name, address, phone);
        return branch;
    }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public void Update(string code, string name, string? address, string? phone)
    {
        var normalizedCode = NormalizeCode(code ?? string.Empty);
        var trimmedName = name?.Trim();
        if (normalizedCode.Length is 0 or > CodeMaxLength || string.IsNullOrEmpty(trimmedName) || trimmedName.Length > NameMaxLength)
        {
            throw new DomainException(InvalidCode, "The branch code or name is not valid.");
        }

        Code = normalizedCode;
        Name = trimmedName;
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
