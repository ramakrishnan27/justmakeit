namespace Platform.Helpers.Constants;

/// <summary>
/// A member's role inside one agency. The lower-cased name is what the database stores and what the
/// access token carries in its <see cref="ClaimNames.Role"/> claim. Owners and admins manage the
/// agency, managers work in it, viewers only read.
/// </summary>
public enum AgencyRole
{
    Owner,
    Admin,
    Manager,
    Viewer
}
