using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Settings;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Services;

/// <summary>
/// Issues the access token: HS256, signed with <c>Jwt:SigningKey</c>, valid for <c>Jwt:AccessTokenMinutes</c>,
/// carrying only the user, the role and, for agency members, the agency, the permissions the role holds and
/// the version they were read under. The validation side is
/// <see cref="ConfigureJwtBearerOptions"/>.
/// </summary>
public sealed class JwtAccessTokenIssuer(IOptions<JwtSettings> options, TimeProvider time) : IAccessTokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Issue(Guid userId, Guid agencyId, OrganizationRole role, PermissionGrants grants) =>
        Sign(new Dictionary<string, object>
        {
            [ClaimNames.UserId] = userId.ToString(),
            [ClaimNames.AgencyId] = agencyId.ToString(),
            [ClaimNames.Role] = role.ToDb(),
            // A JSON array, which the validating side reads back as one claim per key.
            [ClaimNames.Permission] = grants.Keys.ToArray(),
            [ClaimNames.PermissionVersion] = grants.Version
        });

    public AccessToken IssueSiteAdmin(Guid userId) =>
        Sign(new Dictionary<string, object>
        {
            [ClaimNames.UserId] = userId.ToString(),
            [ClaimNames.Role] = RoleNames.SiteAdmin
        });

    private AccessToken Sign(Dictionary<string, object> claims)
    {
        var jwt = options.Value;
        var now = time.GetUtcNow();
        var expires = now.AddMinutes(jwt.AccessTokenMinutes);

        claims[JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N");

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(jwt.GetSigningKeyBytes()), SecurityAlgorithms.HmacSha256)
        });

        return new AccessToken(token, expires);
    }
}
