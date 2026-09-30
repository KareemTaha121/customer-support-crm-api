namespace CustomerSupportCrm.Contracts.Users;

public sealed record CreateUserRequest(string Email, string DisplayName, string Password, IReadOnlyList<Guid> RoleIds);

public sealed record SetUserRolesRequest(IReadOnlyList<Guid> RoleIds);

public sealed record RoleReference(Guid Id, string Name);

public sealed record UserListItemResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string Status,
    IReadOnlyList<string> Roles,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt);

public sealed record UserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string Status,
    bool IsLockedOut,
    IReadOnlyList<RoleReference> Roles,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
