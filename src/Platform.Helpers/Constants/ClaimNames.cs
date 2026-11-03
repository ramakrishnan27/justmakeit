namespace Platform.Helpers.Constants;

public static class ClaimNames
{
    public const string UserId = "user_id";
    public const string AgencyId = "agency_id";
    public const string Role = "role";

    /// <summary>One claim per permission key the member's role holds (see <see cref="PermissionKeys"/>). Absent on a site admin token.</summary>
    public const string Permission = "permission";

    /// <summary>
    /// The permission version the token was issued under. When the Site admin changes any grant the
    /// version goes up and a token carrying an older one is answered 401 <c>permissions_changed</c>: the person signs in again.
    /// </summary>
    public const string PermissionVersion = "pv";
}
