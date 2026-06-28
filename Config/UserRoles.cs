namespace Rihla.Config;

// ─────────────────────────────────────────────────────────────────────────────
// User Types (Application-level classification)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Application-level user types. Derived from a combination of ERPNext roles
/// and the user's department. These are the only three operational roles
/// the chat & ticketing system cares about, plus Admin for full access.
/// </summary>
public enum AppUserType
{
    /// <summary>
    /// End-user / client.
    /// ERPNext role: "Customer" | User Type in ERPNext: "Website User"
    /// </summary>
    Customer,

    /// <summary>
    /// First-line support agent (handles chat escalations, creates tickets).
    /// ERPNext role: "Support Team" | Department: "Customer Service"
    /// </summary>
    CustomerCare,

    /// <summary>
    /// Field engineer / technical specialist (assigned to site visits).
    /// ERPNext roles: "Support Team" + "Specialist" | Department: "Technical support"
    /// </summary>
    Specialist,

    /// <summary>
    /// System administrator — full access to all modules.
    /// ERPNext role: "System Manager" or "Administrator"
    /// </summary>
    Admin
}

// ─────────────────────────────────────────────────────────────────────────────
// ERPNext Role Names
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Exact role names as they appear in ERPNext (case-sensitive match expected).
/// Add new roles here if ERPNext configuration changes.
/// </summary>
public static class ErpRoles
{
    // ── Operational roles ──────────────────────────────────────────────────────
    public const string Customer      = "Customer";
    public const string SupportTeam   = "Support Team";
    public const string Specialist    = "Specialist";
    public const string Agent         = "Agent";
    public const string ChatSupport   = "Chat Support";

    // ── Admin / system roles ───────────────────────────────────────────────────
    public const string SystemManager = "System Manager";
    public const string Administrator = "Administrator";

    // ── ERPNext built-in portal roles (treated as Customer) ───────────────────
    public const string PortalUser   = "Portal User";
    public const string WebsiteUser  = "Website User";

    // ── Standard employee roles (lower-priority — treated as generic employee) ─
    public const string Employee             = "Employee";
    public const string EmployeeSelfService  = "Employee Self Service";
}

// ─────────────────────────────────────────────────────────────────────────────
// ERPNext Department Names
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Exact department names as they appear in ERPNext Employee records.
/// The department is the primary differentiator between CustomerCare and Specialist
/// when both share the "Support Team" role.
/// Add new departments here if the organisational structure changes.
/// </summary>
public static class ErpDepartments
{
    /// <summary>Customer-care / first-line support agents.</summary>
    public const string CustomerCare = "Customer Service - SD";

    /// <summary>Field engineers / technical specialists.</summary>
    public const string Specialist   = "Technical Support";
}

// ─────────────────────────────────────────────────────────────────────────────
// PrimaryRole string values stored in AppUsers.PrimaryRole column
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// String values written to the <c>AppUsers.PrimaryRole</c> column and
/// embedded in the JWT "role" claim.  Always use these constants instead of
/// raw strings to avoid typos across the codebase.
/// </summary>
public static class AppRoles
{
    public const string Customer     = "Customer";
    public const string CustomerCare = "Customer Care";
    public const string Specialist   = "Specialist";
    public const string Admin        = "System Manager";
    public const string Employee     = "Employee";        // Generic fallback
}
