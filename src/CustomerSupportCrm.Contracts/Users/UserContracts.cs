namespace CustomerSupportCrm.Contracts.Users;

public sealed record CreateUserRequest(
    string Email,
    string DisplayName,
    string Password,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<UserScopeRequest>? Scopes = null);

public sealed record UpdateUserRequest(string DisplayName);

public sealed record ResetPasswordRequest(string NewPassword);

/// <param name="DepartmentId">Null grants the whole branch.</param>
public sealed record UserScopeRequest(Guid BranchId, Guid? DepartmentId);

public sealed record SetUserScopesRequest(IReadOnlyList<UserScopeRequest> Scopes);

public sealed record UserScopeResponse(Guid BranchId, string BranchName, Guid? DepartmentId, string? DepartmentName);

/// <summary>Minimal user data for pickers (assignee, mentions).</summary>
public sealed record UserLookupResponse(Guid Id, string DisplayName, string Email);

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
    IReadOnlyList<UserScopeResponse> Scopes,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
