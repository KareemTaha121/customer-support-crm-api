using CustomerSupportCrm.Domain.Common;
using CustomerSupportCrm.Domain.Roles;
using CustomerSupportCrm.Domain.Shared;

namespace CustomerSupportCrm.Domain.Users;

public enum UserStatus
{
    Active,
    Disabled,
}

/// <summary>
/// A staff account. Owns its credentials, lockout state and role membership.
/// </summary>
public sealed class User : Entity<UserId>, IAuditableEntity
{
    public const int DisplayNameMaxLength = 200;
    public const int MaxFailedLoginAttempts = 5;

    public const string InvalidDisplayNameCode = "INVALID_DISPLAY_NAME";

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly List<UserRole> _roles = [];
    private readonly List<UserScope> _scopes = [];

    private User()
    {
    }

    private User(UserId id, EmailAddress email, string displayName, string passwordHash)
        : base(id)
    {
        Email = email.Value;
        Rename(displayName);
        ChangePasswordHash(passwordHash);
        Status = UserStatus.Active;
    }

    /// <summary>Normalized (lower-case) email; unique across users.</summary>
    public string Email { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public UserStatus Status { get; private set; }

    public int FailedLoginAttempts { get; private set; }

    public DateTimeOffset? LockoutEndsAt { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles;

    /// <summary>Branches/departments whose data the user may access (see <see cref="UserScope"/>).</summary>
    public IReadOnlyCollection<UserScope> Scopes => _scopes;

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public bool IsActive => Status == UserStatus.Active;

    public IReadOnlyList<RoleId> RoleIds => [.. _roles.Select(r => r.RoleId)];

    public static User Create(EmailAddress email, string displayName, string passwordHash, IEnumerable<RoleId> roleIds)
    {
        ArgumentNullException.ThrowIfNull(email);

        var user = new User(UserId.New(), email, displayName, passwordHash);
        user.SetRoles(roleIds);
        return user;
    }

    public void Rename(string displayName)
    {
        var trimmed = displayName?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > DisplayNameMaxLength)
        {
            throw new DomainException(InvalidDisplayNameCode, "The display name is not valid.");
        }

        DisplayName = trimmed;
    }

    public void ChangePasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    public void SetRoles(IEnumerable<RoleId> roleIds)
    {
        var requested = roleIds.Distinct().ToList();

        _roles.RemoveAll(existing => !requested.Contains(existing.RoleId));
        foreach (var roleId in requested.Where(id => _roles.All(r => r.RoleId != id)))
        {
            _roles.Add(new UserRole(Id, roleId));
        }
    }

    /// <summary>Replaces the user's data scope. A null department grants the whole branch.</summary>
    public void SetScopes(IEnumerable<(Guid BranchId, Guid? DepartmentId)> scopes)
    {
        var requested = scopes.Distinct().ToList();

        _scopes.RemoveAll(existing => !requested.Contains((existing.BranchId, existing.DepartmentId)));
        foreach (var (branchId, departmentId) in requested.Where(s => !_scopes.Any(e => e.BranchId == s.BranchId && e.DepartmentId == s.DepartmentId)))
        {
            _scopes.Add(new UserScope(Id, branchId, departmentId));
        }
    }

    public bool HasRole(RoleId roleId) => _roles.Any(r => r.RoleId == roleId);

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndsAt > now;

    /// <summary>Counts a failed sign-in; the account locks after <see cref="MaxFailedLoginAttempts"/>.</summary>
    public void RecordFailedLogin(DateTimeOffset now)
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= MaxFailedLoginAttempts)
        {
            LockoutEndsAt = now + LockoutDuration;
            FailedLoginAttempts = 0;
        }
    }

    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginAttempts = 0;
        LockoutEndsAt = null;
        LastLoginAt = now;
    }

    public void Disable() => Status = UserStatus.Disabled;

    public void Enable()
    {
        Status = UserStatus.Active;
        FailedLoginAttempts = 0;
        LockoutEndsAt = null;
    }
}

/// <summary>
/// Grants access to a branch (<see cref="DepartmentId"/> null) or to one department of it.
/// </summary>
public sealed class UserScope
{
    private UserScope()
    {
    }

    internal UserScope(UserId userId, Guid branchId, Guid? departmentId)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        BranchId = branchId;
        DepartmentId = departmentId;
    }

    public Guid Id { get; private set; }

    public UserId UserId { get; private set; }

    public Guid BranchId { get; private set; }

    public Guid? DepartmentId { get; private set; }
}

public sealed class UserRole
{
    private UserRole()
    {
    }

    internal UserRole(UserId userId, RoleId roleId)
    {
        UserId = userId;
        RoleId = roleId;
    }

    public UserId UserId { get; private set; }

    public RoleId RoleId { get; private set; }
}
