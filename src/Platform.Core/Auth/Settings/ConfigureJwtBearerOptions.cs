using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Platform.Auth.Controllers;
using Platform.Helpers.Constants;

namespace Platform.Auth.Settings;

/// <summary>
/// Token validation rules for every protected endpoint, built from <see cref="JwtSettings"/> when the
/// scheme is first used so it sees the final configuration. The token comes from the <c>Authorization: Bearer</c>
/// header when there is one (tools and tests), and otherwise from the HttpOnly cookie the sign-in set (see
/// <see cref="AccessTokenCookie"/>), which is how the web pages carry it.
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
        options.Events = new JwtBearerEvents { OnMessageReceived = ReadTokenFromCookie };
    }

    // A request that names its own token in the Authorization header is judged by that alone. Otherwise the cookie for
    // the part of the API being called is used, so a member's cookie can never authenticate a Site admin route and the
    // other way round. No cookie means no token: the request is anonymous and the policy answers 401.
    private static Task ReadTokenFromCookie(MessageReceivedContext context)
    {
        if (string.IsNullOrEmpty(context.Request.Headers.Authorization))
        {
            var token = AccessTokenCookie.ForRequest(context.Request.Path).Read(context.Request);
            if (!string.IsNullOrEmpty(token))
            {
                context.Token = token;
            }
        }

        return Task.CompletedTask;
    }

    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
}
