namespace CustomerSupportCrm.Contracts.Roles;

public sealed record CreateRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);

public sealed record UpdateRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);

public sealed record RoleResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<string> Permissions,
    int UserCount);

/// <param name="Code">Permission code, e.g. tickets.assign.</param>
/// <param name="Group">Feature group, e.g. tickets.</param>
public sealed record PermissionResponse(string Code, string Group);
