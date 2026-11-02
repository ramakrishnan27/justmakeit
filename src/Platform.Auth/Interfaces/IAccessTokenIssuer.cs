using Platform.Auth.Models;
using Platform.Helpers.Constants;

namespace Platform.Auth.Interfaces;

public interface IAccessTokenIssuer
{
    /// <summary>
    /// Signs a short-lived HS256 token carrying the user, agency and role claims, the permission keys
    /// the role holds (one claim each) and the permission version they were read under.
    /// </summary>
    AccessToken Issue(Guid userId, Guid agencyId, OrganizationRole role, PermissionGrants grants);

    /// <summary>
    /// Signs a token for a site admin: the user id and the <see cref="RoleNames.SiteAdmin"/> role, and
    /// deliberately no agency id, so it cannot pass any agency policy or open a tenant connection. It
    /// carries no permissions either: a site admin's rights are the role itself.
    /// </summary>
    AccessToken IssueSiteAdmin(Guid userId);
}
