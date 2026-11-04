using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// Mints access tokens the way the API does, so a test can act as any user, agency and role without
/// going through login. The role claim is the same lower-case name the real issuer writes, and the token
/// carries the permissions a role holds in a fresh installation unless the test names others.
/// </summary>
public static class TestAccessTokens
{
    /// <param name="permissions">The permission keys to put in the token; the role's defaults when null.</param>
    /// <param name="permissionVersion">The permission version the token claims to have been issued under.</param>
    public static string Create(
        string signingKey,
        Guid userId,
        Guid agencyId,
        OrganizationRole role,
        IEnumerable<string>? permissions = null,
        long permissionVersion = DefaultGrantsPermissionService.Version) =>
        Sign(signingKey, new Dictionary<string, object>
        {
            [ClaimNames.UserId] = userId.ToString(),
            [ClaimNames.AgencyId] = agencyId.ToString(),
            [ClaimNames.Role] = role.ToDb(),
            [ClaimNames.Permission] = (permissions ?? DefaultGrantsPermissionService.DefaultKeys(role)).ToArray(),
            [ClaimNames.PermissionVersion] = permissionVersion
        });

    /// <summary>A member token whose role claim is any text, for a role this build does not know.</summary>
    public static string CreateWithRoleName(
        string signingKey, Guid userId, Guid agencyId, string roleName, IEnumerable<string> permissions, long permissionVersion) =>
        Sign(signingKey, new Dictionary<string, object>
        {
            [ClaimNames.UserId] = userId.ToString(),
            [ClaimNames.AgencyId] = agencyId.ToString(),
            [ClaimNames.Role] = roleName,
            [ClaimNames.Permission] = permissions.ToArray(),
            [ClaimNames.PermissionVersion] = permissionVersion
        });

    /// <summary>
    /// A member token from before permissions travelled in the token: the user, agency and role, and
    /// neither permissions nor a version. The API must answer it 401 so it is refreshed.
    /// </summary>
    public static string CreateWithoutPermissions(string signingKey, Guid userId, Guid agencyId, OrganizationRole role) =>
        Sign(signingKey, new Dictionary<string, object>
        {
            [ClaimNames.UserId] = userId.ToString(),
            [ClaimNames.AgencyId] = agencyId.ToString(),
            [ClaimNames.Role] = role.ToDb()
        });

    /// <summary>A member token that does not say when it was issued, so it cannot be compared with an ended session.</summary>
    public static string CreateWithoutIssuedAt(string signingKey, Guid userId, Guid agencyId, OrganizationRole role, long permissionVersion)
    {
        // The token handler writes iat by itself whenever it builds a token from a descriptor, so this one signs a
        // payload written by hand.
        var now = DateTimeOffset.UtcNow;
        var payload = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["iss"] = "platform",
            ["aud"] = "platform-web",
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(15).ToUnixTimeSeconds(),
            [ClaimNames.UserId] = userId.ToString(),
            [ClaimNames.AgencyId] = agencyId.ToString(),
            [ClaimNames.Role] = role.ToDb(),
            [ClaimNames.Permission] = DefaultGrantsPermissionService.DefaultKeys(role).ToArray(),
            [ClaimNames.PermissionVersion] = permissionVersion
        });

        return new JsonWebTokenHandler().CreateToken(
            payload,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256));
    }

    /// <summary>A site admin token: the user and the siteadmin role, and no agency, like the real login issues.</summary>
    public static string CreateSiteAdmin(string signingKey, Guid userId) =>
        Sign(signingKey, new Dictionary<string, object>
        {
            [ClaimNames.UserId] = userId.ToString(),
            [ClaimNames.Role] = RoleNames.SiteAdmin
        });

    private static string Sign(string signingKey, Dictionary<string, object> claims)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "platform",
            Audience = "platform-web",
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(15),
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256)
        });
    }
}
