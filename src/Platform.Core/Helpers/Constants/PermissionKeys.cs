namespace Platform.Helpers.Constants;

/// <summary>
/// The permission catalog. Each key is a row in the <c>permissions</c> table (seeded by
/// <c>005_menus_permissions.sql</c> and later scripts), is granted to a business model and role in <c>role_permissions</c>,
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

    /// <summary>
    /// Invite people into the organization, change their roles and remove them: the Team page and <c>/v1/members</c>
    /// (seeded by <c>007_team_members.sql</c>). Whoever holds it can also make organization admins.
    /// </summary>
    public const string MembersManage = "members.manage";

    /// <summary>
    /// See the channels (platforms) and which are enabled for the organization: the Platform page and <c>/v1/platforms</c>
    /// (seeded by <c>012_member_platforms.sql</c>).
    /// </summary>
    public const string PlatformsView = "platforms.view";

    /// <summary>
    /// Open the Content Studio page (<c>/content-studio</c>). The four pages below are placeholders for features that are
    /// not built yet: the menu item and the permission are real, the page says it is coming soon (seeded by
    /// <c>013_member_studio_menu.sql</c>). The first segment of a key has no underscore, hence the one-word names.
    /// </summary>
    public const string ContentStudioView = "contentstudio.view";

    /// <summary>Open the Media Library page (<c>/media-library</c>).</summary>
    public const string MediaLibraryView = "medialibrary.view";

    /// <summary>Open the AI Studio page (<c>/ai-studio</c>).</summary>
    public const string AiStudioView = "aistudio.view";

    /// <summary>Open the Calendar page (<c>/calendar</c>).</summary>
    public const string CalendarView = "calendar.view";

    /// <summary>
    /// Create content in Content Studio: make, edit, duplicate and delete drafts, and save templates and tags
    /// (<c>/v1/content</c>, seeded by <c>015_content_studio_posts.sql</c>). Held by every role that changes data.
    /// </summary>
    public const string ContentStudioCreate = "contentstudio.create";

    /// <summary>
    /// Publish content in Content Studio: schedule, publish now, unschedule, archive and restore, and delete content that is no
    /// longer a draft (seeded by <c>015_content_studio_posts.sql</c>). Held by organization admins and marketing managers, and by
    /// the content writer of an individual creator, who works alone. A content writer in a company or an agency creates drafts for
    /// someone else to approve and publish.
    /// </summary>
    public const string ContentStudioPublish = "contentstudio.publish";

    /// <summary>
    /// Add to the Media Library: upload files, create folders and put files in them (<c>/v1/media</c>, seeded by
    /// <c>016_media_library.sql</c>). Held by every role that changes data. Opening the page and reading needs
    /// <see cref="MediaLibraryView"/>.
    /// </summary>
    public const string MediaLibraryCreate = "medialibrary.create";

    /// <summary>
    /// Delete files from the Media Library (seeded by <c>016_media_library.sql</c>). Held by organization admins and marketing
    /// managers, and by the content writer of an individual creator, who works alone. A content writer in a company or an agency adds
    /// files for others and does not take them away.
    /// </summary>
    public const string MediaLibraryDelete = "medialibrary.delete";

    /// <summary>Every key in the catalog. A test keeps this list, the constants above and the seeded table equal.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        DashboardView, WorkspacesView, WorkspacesCreate, DataRead, DataWrite, OrganizationManage, MembersManage, PlatformsView,
        ContentStudioView, MediaLibraryView, AiStudioView, CalendarView, ContentStudioCreate, ContentStudioPublish,
        MediaLibraryCreate, MediaLibraryDelete
    ];

    /// <summary>
    /// A protected permission is held by every role of every business model and the database refuses to
    /// revoke it. Mirrors <c>permissions.is_protected</c>.
    /// </summary>
    public static bool IsProtected(string key) => key == DashboardView;
}
