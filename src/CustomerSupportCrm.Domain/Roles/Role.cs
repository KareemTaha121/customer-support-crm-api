using CustomerSupportCrm.Domain.Common;

namespace CustomerSupportCrm.Domain.Roles;

/// <summary>
/// A named set of permissions. System roles are managed by the platform and cannot be
/// changed or deleted through the API.
/// </summary>
public sealed class Role : Entity<RoleId>, IAuditableEntity
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    public const string AdministratorName = "Administrator";

    public const string SystemRoleCode = "ROLE_IS_SYSTEM";
    public const string UnknownPermissionCode = "UNKNOWN_PERMISSION";
    public const string InvalidNameCode = "INVALID_ROLE_NAME";

    private readonly List<RolePermission> _permissions = [];

    private Role()
    {
    }

    private Role(RoleId id, string name, string? description, bool isSystem)
        : base(id)
    {
        Rename(name);
        Description = NormalizeDescription(description);
        IsSystem = isSystem;
    }

    public string Name { get; private set; } = string.Empty;

    public string NormalizedName { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsSystem { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public IReadOnlyList<string> PermissionCodes => [.. _permissions.Select(p => p.Permission).Order(StringComparer.Ordinal)];

    public static Role Create(string name, string? description, IEnumerable<string> permissions)
    {
        var role = new Role(RoleId.New(), name, description, isSystem: false);
        role.ReplacePermissions(permissions);
        return role;
    }

    /// <summary>The built-in Administrator role, which always holds every permission.</summary>
    public static Role CreateAdministrator()
    {
        var role = new Role(RoleId.New(), AdministratorName, "Full access to every feature.", isSystem: true);
        role.GrantAllPermissions();
        return role;
    }

    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Trim().ToUpperInvariant();
    }

    public void Update(string name, string? description, IEnumerable<string> permissions)
    {
        EnsureNotSystem();
        Rename(name);
        Description = NormalizeDescription(description);
        ReplacePermissions(permissions);
    }

    public void EnsureCanBeDeleted() => EnsureNotSystem();

    /// <summary>Keeps a system role in sync with the permission catalog as it grows.</summary>
    public void GrantAllPermissions()
    {
        if (!IsSystem)
        {
            throw new InvalidOperationException("Only system roles are granted every permission.");
        }

        ReplacePermissions(Roles.Permissions.All);
    }

    public bool HasPermission(string permission) => _permissions.Any(p => p.Permission == permission);

    private void Rename(string name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > NameMaxLength)
        {
            throw new DomainException(InvalidNameCode, "The role name is not valid.");
        }

        Name = trimmed;
        NormalizedName = Normalize(trimmed);
    }

    private void ReplacePermissions(IEnumerable<string> permissions)
    {
        var requested = permissions.Distinct(StringComparer.Ordinal).ToList();

        var unknown = requested.FirstOrDefault(p => !Roles.Permissions.IsKnown(p));
        if (unknown is not null)
        {
            throw new DomainException(UnknownPermissionCode, $"Unknown permission '{unknown}'.");
        }

        _permissions.RemoveAll(existing => !requested.Contains(existing.Permission));
        foreach (var permission in requested.Where(p => !HasPermission(p)))
        {
            _permissions.Add(new RolePermission(Id, permission));
        }
    }

    private void EnsureNotSystem()
    {
        if (IsSystem)
        {
            throw new DomainException(SystemRoleCode, "System roles cannot be modified or deleted.");
        }
    }

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();
        if (trimmed?.Length > DescriptionMaxLength)
        {
            throw new DomainException(InvalidNameCode, "The role description is too long.");
        }

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

public sealed class RolePermission
{
    private RolePermission()
    {
        Permission = string.Empty;
    }

    internal RolePermission(RoleId roleId, string permission)
    {
        RoleId = roleId;
        Permission = permission;
    }

    public RoleId RoleId { get; private set; }

    public string Permission { get; private set; }
}
