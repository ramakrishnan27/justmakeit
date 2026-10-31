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
/// carrying only the user, agency and role. The validation side is <see cref="ConfigureJwtBearerOptions"/>.
/// </summary>
public sealed class JwtAccessTokenIssuer(IOptions<JwtSettings> options, TimeProvider time) : IAccessTokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Issue(Guid userId, Guid agencyId, AgencyRole role)
    {
        var jwt = options.Value;
        var now = time.GetUtcNow();
        var expires = now.AddMinutes(jwt.AccessTokenMinutes);

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [ClaimNames.UserId] = userId.ToString(),
                [ClaimNames.AgencyId] = agencyId.ToString(),
                [ClaimNames.Role] = role.ToDb(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N")
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(jwt.GetSigningKeyBytes()), SecurityAlgorithms.HmacSha256)
        });

        return new AccessToken(token, expires);
    }
}
