using System.Globalization;
using System.Security.Claims;

namespace Platform.Auth.Controllers;

internal static class SessionClaims
{
    private const string ExpiresClaim = "exp";

    /// <summary>
    /// When the sign-in the request carries ends: the token's <c>exp</c> (seconds since 1970). Null when the claim is
    /// missing or not a number; claim names are kept as issued (<c>MapInboundClaims</c> is off), so it is called "exp".
    /// </summary>
    public static DateTimeOffset? SessionExpiresAt(this ClaimsPrincipal user) =>
        long.TryParse(user.FindFirstValue(ExpiresClaim), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
}
