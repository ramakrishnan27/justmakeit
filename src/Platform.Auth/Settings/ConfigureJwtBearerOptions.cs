using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Platform.Helpers.Constants;

namespace Platform.Auth.Settings;

/// <summary>
/// Token validation rules for every protected endpoint, built from <see cref="JwtSettings"/> when the
/// scheme is first used so it sees the final configuration.
/// </summary>
public sealed class ConfigureJwtBearerOptions(IOptions<JwtSettings> jwtOptions) : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        var jwt = jwtOptions.Value;

        // Keep claim names exactly as issued (user_id, agency_id, role).
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(jwt.GetSigningKeyBytes()),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimNames.UserId,
            RoleClaimType = ClaimNames.Role
        };
    }

    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
}
