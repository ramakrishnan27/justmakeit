namespace Platform.Helpers.Constants;

/// <summary>
/// The permission catalog. Each key is a row in the <c>permissions</c> table (seeded by
/// <c>005_menus_permissions.sql</c>), is granted to a business model and role in <c>role_permissions</c>,
/// travels in the access token as a <see cref="ClaimNames.Permission"/> claim and is named by an endpoint
/// through <c>PolicyNames.PermissionPrefix + PermissionKeys.X</c>. Keys are code-defined because endpoints
/// refer to them; the Site admin decides which role holds which, never which keys exist.
/// </summary>
public static class PermissionKeys
{
    /// <summary>Open the member dashboard. Protected: it can never be taken away, so a member always has a home page.</summary>
    public const string DashboardView = "dashboard.view";

    /// <summary>See the organization's workspaces: the Workspaces page and <c>GET /v1/workspaces</c>.</summary>
    public const string WorkspacesView = "workspaces.view";

    /// <summary>Add a workspace: <c>POST /v1/workspaces</c> and the add form.</summary>
    public const string WorkspacesCreate = "workspaces.create";

    /// <summary>Baseline behind the <c>CanRead</c> policy: read the organization's data.</summary>
    public const string DataRead = "data.read";

    /// <summary>Baseline behind the <c>CanWrite</c> policy: change the organization's data.</summary>
    public const string DataWrite = "data.write";

    /// <summary>Baseline behind the <c>CanManage</c> policy: manage the organization itself.</summary>
    public const string OrganizationManage = "organization.manage";

    /// <summary>Every key in the catalog. A test keeps this list, the constants above and the seeded table equal.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        DashboardView, WorkspacesView, WorkspacesCreate, DataRead, DataWrite, OrganizationManage
    ];

    /// <summary>
    /// A protected permission is held by every role of every business model and the database refuses to
    /// revoke it. Mirrors <c>permissions.is_protected</c>.
    /// </summary>
    public static bool IsProtected(string key) => key == DashboardView;
}
