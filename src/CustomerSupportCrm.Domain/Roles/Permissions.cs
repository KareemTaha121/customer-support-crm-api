namespace CustomerSupportCrm.Domain.Roles;

/// <summary>
/// The permission catalog. Codes are stable identifiers shared with clients and stored
/// on roles; add new codes here, never rename existing ones.
/// </summary>
public static class Permissions
{
    public const string TicketsView = "tickets.view";
    public const string TicketsCreate = "tickets.create";
    public const string TicketsUpdate = "tickets.update";
    public const string TicketsAssign = "tickets.assign";
    public const string TicketsDelete = "tickets.delete";

    public const string CustomersView = "customers.view";
    public const string CustomersCreate = "customers.create";
    public const string CustomersUpdate = "customers.update";

    public const string ReportsView = "reports.view";

    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string SettingsManage = "settings.manage";
    public const string AuditView = "audit.view";

    public static IReadOnlyList<string> All { get; } =
    [
        TicketsView, TicketsCreate, TicketsUpdate, TicketsAssign, TicketsDelete,
        CustomersView, CustomersCreate, CustomersUpdate,
        ReportsView,
        UsersManage, RolesManage, SettingsManage, AuditView,
    ];

    private static readonly HashSet<string> Known = [.. All];

    public static bool IsKnown(string permission) => Known.Contains(permission);
}
