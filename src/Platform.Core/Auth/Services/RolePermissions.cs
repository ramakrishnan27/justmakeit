using System.Diagnostics.CodeAnalysis;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Services;

/// <summary>
/// What each role may do in a fresh installation: the factory defaults. <c>005_menus_permissions.sql</c>
/// (and <c>007_team_members.sql</c>, <c>012_member_platforms.sql</c> and <c>013_member_studio_menu.sql</c> for their keys) seeds exactly these as the grants of each business model and role, and a test compares the two. At run
/// time the Site admin's settings in the database decide (they reach the API as permissions in the access
/// token), so this class is the reference the seeds and the tests are checked against, not the rule that
/// is applied. <see cref="TryParse"/> is still how a role claim is read.
/// </summary>
public static class RolePermissions
{
    /// <summary>
    /// Everyone who is a member reads. A client manager reads but does not change data: their work
    /// (relationships, feedback, approvals) gets its own rule when those features exist.
    /// </summary>
    public static bool CanRead(this OrganizationRole role) => Enum.IsDefined(role);

    /// <summary>Organization admins, marketing managers and content writers change data.</summary>
    public static bool CanWrite(this OrganizationRole role) =>
        role is OrganizationRole.OrganizationAdmin or OrganizationRole.MarketingManager or OrganizationRole.ContentWriter;

    /// <summary>Only organization admins manage the organization: its members and its workspaces.</summary>
    public static bool CanManage(this OrganizationRole role) => role is OrganizationRole.OrganizationAdmin;

    /// <summary>
    /// Whether <paramref name="role"/> holds <paramref name="permissionKey"/> in a fresh installation.
    /// <c>005_menus_permissions.sql</c> seeds exactly this, and a test compares the two. At run time the
    /// Site admin's settings in the database decide, not this method; it is the factory default and
    /// the oracle for the tests. A key that is not in <see cref="PermissionKeys.All"/> throws, so a new
    /// permission cannot be added without saying who gets it.
    /// </summary>
    public static bool GrantedByDefault(this OrganizationRole role, string permissionKey) => permissionKey switch
    {
        // Every role opens these pages by default; the Site admin can take any of them away per role.
        PermissionKeys.DashboardView or PermissionKeys.WorkspacesView or PermissionKeys.DataRead or PermissionKeys.PlatformsView
            or PermissionKeys.ContentStudioView or PermissionKeys.MediaLibraryView or PermissionKeys.AiStudioView
            or PermissionKeys.CalendarView => role.CanRead(),
        PermissionKeys.DataWrite or PermissionKeys.ContentStudioCreate or PermissionKeys.MediaLibraryCreate => role.CanWrite(),
        // Publishing and deleting media are the defaults that depend on the business model (see the overload below). Here: held in
        // EVERY business model that has the role, so a content writer, who holds them only in a creator's organization, does not.
        PermissionKeys.ContentStudioPublish or PermissionKeys.MediaLibraryDelete =>
            role is OrganizationRole.OrganizationAdmin or OrganizationRole.MarketingManager,
        PermissionKeys.WorkspacesCreate or PermissionKeys.OrganizationManage or PermissionKeys.MembersManage => role.CanManage(),
        _ => throw new ArgumentOutOfRangeException(nameof(permissionKey), permissionKey, "Unknown permission.")
    };

    /// <summary>
    /// The factory default of one (business model, role) slot, which is what the database is seeded with and what the Site admin
    /// edits. Equal to <see cref="GrantedByDefault(OrganizationRole, string)"/> except where a default depends on the model:
    /// <c>contentstudio.publish</c> and <c>medialibrary.delete</c> are also held by the content writer of an individual creator, who
    /// works alone and so publishes their own content and manages their own files, while a content writer in a company or an agency
    /// submits content for others to publish and adds files for others to keep.
    /// </summary>
    public static bool GrantedByDefault(this OrganizationRole role, BusinessModel model, string permissionKey) =>
        permissionKey is PermissionKeys.ContentStudioPublish or PermissionKeys.MediaLibraryDelete
            && model == BusinessModel.Creator && role == OrganizationRole.ContentWriter
        || role.GrantedByDefault(permissionKey);

    /// <summary>
    /// Reads a role from its stored lower-case name, such as the token's role claim. Strict on purpose:
    /// anything but the exact name, including a number, is not a role.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? value, out OrganizationRole role)
    {
        foreach (var candidate in Enum.GetValues<OrganizationRole>())
        {
            if (value == candidate.ToDb())
            {
                role = candidate;
                return true;
            }
        }

        role = default;
        return false;
    }
}
