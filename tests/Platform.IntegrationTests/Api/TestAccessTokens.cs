using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// Mints access tokens the way the API does, so a test can act as any user, agency and role without
/// going through login. The role claim is the same lower-case name the real issuer writes.
/// </summary>
public static class TestAccessTokens
{
    public static string Create(string signingKey, Guid userId, Guid agencyId, AgencyRole role)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "platform",
            Audience = "platform-web",
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(15),
            Claims = new Dictionary<string, object>
            {
                [ClaimNames.UserId] = userId.ToString(),
                [ClaimNames.AgencyId] = agencyId.ToString(),
                [ClaimNames.Role] = role.ToDb()
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256)
        });
    }
}
