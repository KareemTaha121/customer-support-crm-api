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
    public const string TicketsEscalate = "tickets.escalate";
    public const string TicketsDelete = "tickets.delete";
    public const string TicketCategoriesManage = "tickets.categories_manage";

    public const string CustomersView = "customers.view";
    public const string CustomersCreate = "customers.create";
    public const string CustomersUpdate = "customers.update";
    public const string CustomersDelete = "customers.delete";
    public const string CustomerNotesManage = "customers.notes_manage";
    public const string CustomerAttachmentsManage = "customers.attachments_manage";

    public const string KnowledgeView = "kb.view";
    public const string KnowledgeManage = "kb.manage";
    public const string KnowledgePublish = "kb.publish";

    public const string SlaManage = "sla.manage";
    public const string AutomationManage = "automation.manage";

    public const string ChatHandle = "chat.handle";
    public const string ChannelsManage = "channels.manage";
    public const string QuickRepliesManage = "quickreplies.manage";

    public const string ReportsView = "reports.view";
    public const string ReportsExport = "reports.export";

    public const string AiUse = "ai.use";

    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string AuditView = "audit.view";
    public const string AuditExport = "audit.export";
    public const string OrganizationManage = "organization.manage";
    public const string SettingsManage = "settings.manage";
    public const string IntegrationsManage = "integrations.manage";

    /// <summary>See and act on data of every branch and department, regardless of scope.</summary>
    public const string DataAllBranches = "data.all_branches";

    public static IReadOnlyList<string> All { get; } =
    [
        TicketsView, TicketsCreate, TicketsUpdate, TicketsAssign, TicketsEscalate, TicketsDelete, TicketCategoriesManage,
        CustomersView, CustomersCreate, CustomersUpdate, CustomersDelete, CustomerNotesManage, CustomerAttachmentsManage,
        KnowledgeView, KnowledgeManage, KnowledgePublish,
        SlaManage, AutomationManage,
        ChatHandle, ChannelsManage, QuickRepliesManage,
        ReportsView, ReportsExport,
        AiUse,
        UsersManage, RolesManage, AuditView, AuditExport, OrganizationManage, SettingsManage, IntegrationsManage,
        DataAllBranches,
    ];

    /// <summary>Front-line agent defaults.</summary>
    public static IReadOnlyList<string> AgentDefaults { get; } =
    [
        TicketsView, TicketsCreate, TicketsUpdate,
        CustomersView, CustomersCreate, CustomersUpdate, CustomerNotesManage, CustomerAttachmentsManage,
        KnowledgeView, ChatHandle, AiUse,
    ];

    /// <summary>Supervisor/manager defaults: agents plus assignment, escalation, content and reports.</summary>
    public static IReadOnlyList<string> ManagerDefaults { get; } =
    [
        .. AgentDefaults,
        TicketsAssign, TicketsEscalate, TicketsDelete, TicketCategoriesManage,
        CustomersDelete,
        KnowledgeManage, KnowledgePublish,
        SlaManage, AutomationManage, QuickRepliesManage,
        ReportsView, ReportsExport,
    ];

    private static readonly HashSet<string> Known = [.. All];

    public static bool IsKnown(string permission) => Known.Contains(permission);
}
