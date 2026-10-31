using Platform.Auth.Models;
using Platform.Helpers.Constants;

namespace Platform.Auth.Interfaces;

public interface IAccessTokenIssuer
{
    /// <summary>Signs a short-lived HS256 token carrying the user, agency and role claims.</summary>
    AccessToken Issue(Guid userId, Guid agencyId, AgencyRole role);
}
