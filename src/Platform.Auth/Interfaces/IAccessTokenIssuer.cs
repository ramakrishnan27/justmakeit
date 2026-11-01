using Platform.Auth.Models;
using Platform.Helpers.Constants;

namespace Platform.Auth.Interfaces;

public interface IAccessTokenIssuer
{
    /// <summary>Signs a short-lived HS256 token carrying the user, agency and role claims.</summary>
    AccessToken Issue(Guid userId, Guid agencyId, OrganizationRole role);

    /// <summary>
    /// Signs a token for a site admin: the user id and the <see cref="RoleNames.SiteAdmin"/> role, and
    /// deliberately no agency id, so it cannot pass any agency policy or open a tenant connection.
    /// </summary>
    AccessToken IssueSiteAdmin(Guid userId);
}
