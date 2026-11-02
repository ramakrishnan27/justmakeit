namespace Platform.Helpers.Constants;

/// <summary>
/// Authorization policy names. Platform.Auth registers them; any project can name them in
/// <c>[Authorize(Policy = PolicyNames.CanWrite)]</c> without referencing Platform.Auth.
/// </summary>
public static class PolicyNames
{
    /// <summary>Holds the <c>data.read</c> permission: by default every role of the organization.</summary>
    public const string CanRead = "CanRead";

    /// <summary>Holds the <c>data.write</c> permission: by default every role except the client manager.</summary>
    public const string CanWrite = "CanWrite";

    /// <summary>Holds the <c>organization.manage</c> permission: by default the organization admin only.</summary>
    public const string CanManage = "CanManage";

    /// <summary>A platform-wide site admin. Their token has no agency, so it passes no agency policy.</summary>
    public const string SiteAdmin = "SiteAdmin";

    /// <summary>
    /// Prefix of the policy that requires one named permission:
    /// <c>[Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.WorkspacesCreate)]</c>. It is a
    /// compile-time constant, so a channel project can name it without referencing Platform.Auth.
    /// </summary>
    public const string PermissionPrefix = "permission:";
}
