namespace Platform.Helpers.Constants;

/// <summary>
/// Values of the access token's <see cref="ClaimNames.Role"/> claim that are not an <see cref="AgencyRole"/>.
/// An agency member's token carries an agency role plus an agency id; a site admin's token carries
/// <see cref="SiteAdmin"/> and no agency id, so neither kind of token works on the other's endpoints.
/// </summary>
public static class RoleNames
{
    /// <summary>A platform-wide administrator who sees every agency. Never an agency member role.</summary>
    public const string SiteAdmin = "siteadmin";
}
