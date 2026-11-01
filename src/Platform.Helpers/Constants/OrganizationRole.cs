namespace Platform.Helpers.Constants;

/// <summary>
/// A member's role inside one organization. The lower-cased name is what the database stores and what
/// the access token carries in its <see cref="ClaimNames.Role"/> claim. Which roles an organization may
/// use depends on its <see cref="BusinessModel"/>: a client manager exists only in an agency, and an
/// individual creator is always a content writer. (In the code and the database an organization is
/// still called an agency; <see cref="BusinessModel.Agency"/> is one kind of organization.)
/// </summary>
public enum OrganizationRole
{
    /// <summary>Manages the organization: its members, workspaces, permissions and integrations.</summary>
    OrganizationAdmin,

    /// <summary>Agencies only: looks after one client's relationship, requirements, feedback and approvals.</summary>
    ClientManager,

    /// <summary>Plans campaigns, reviews content and oversees publishing.</summary>
    MarketingManager,

    /// <summary>Creates content and submits it for approval.</summary>
    ContentWriter
}
