namespace Platform.Helpers.Constants;

/// <summary>
/// Authorization policy names. Platform.Auth registers them; any project can name them in
/// <c>[Authorize(Policy = PolicyNames.CanWrite)]</c> without referencing Platform.Auth.
/// </summary>
public static class PolicyNames
{
    /// <summary>Any member of the current agency: owner, admin, manager or viewer.</summary>
    public const string CanRead = "CanRead";

    /// <summary>Members who may change data: owner, admin or manager. Viewers are read-only.</summary>
    public const string CanWrite = "CanWrite";

    /// <summary>Members who manage the agency itself (members, workspaces): owner or admin.</summary>
    public const string CanManage = "CanManage";
}
