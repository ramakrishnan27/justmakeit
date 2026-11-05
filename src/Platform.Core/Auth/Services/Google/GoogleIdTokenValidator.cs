using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Platform.Auth.Services.Google;

/// <summary>Where Google's public signing keys come from. A seam, so tests can sign tokens with their own key.</summary>
public interface IGoogleSigningKeys
{
    Task<IReadOnlyCollection<SecurityKey>> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Google's keys, read from its OpenID Connect discovery document and cached (and refreshed when Google
/// rotates them) by <see cref="ConfigurationManager{T}"/>.
/// </summary>
public sealed class GoogleSigningKeys : IGoogleSigningKeys
{
    public const string DiscoveryAddress = "https://accounts.google.com/.well-known/openid-configuration";

    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configuration = new(
        DiscoveryAddress, new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever { RequireHttps = true });

    public async Task<IReadOnlyCollection<SecurityKey>> GetAsync(CancellationToken cancellationToken) =>
        (await _configuration.GetConfigurationAsync(cancellationToken)).SigningKeys.ToArray();
}

/// <summary>
/// Checks a Google ID token: the signature, the issuer, that it was issued for our client id, that it has not
/// expired, and that it carries the nonce this sign-in started with (which stops a token from another
/// sign-in being replayed into this one).
/// </summary>
public sealed class GoogleIdTokenValidator(IGoogleSigningKeys signingKeys, TimeProvider time)
{
    private static readonly string[] Issuers = ["https://accounts.google.com", "accounts.google.com"];
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);
    private const int MaxSubjectLength = 255;

    public async Task<GoogleIdentity> ValidateAsync(
        string idToken, string clientId, string expectedNonce, CancellationToken cancellationToken)
    {
        var parameters = new TokenValidationParameters
        {
            ValidIssuers = Issuers,
            ValidAudience = clientId,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = await signingKeys.GetAsync(cancellationToken),
            ClockSkew = ClockSkew,
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                var now = time.GetUtcNow().UtcDateTime;
                return expires is not null && expires.Value > now - ClockSkew && (notBefore is null || notBefore.Value < now + ClockSkew);
            }
        };

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, parameters);
        if (!result.IsValid)
        {
            throw new GoogleSignInException("The Google ID token is not valid.", result.Exception);
        }

        var claims = result.Claims;
        if (!(claims.TryGetValue("nonce", out var nonce) && nonce is string nonceText && SameText(nonceText, expectedNonce)))
        {
            throw new GoogleSignInException("The Google ID token does not belong to this sign-in.");
        }

        var subject = claims.TryGetValue("sub", out var sub) ? sub as string : null;
        var email = claims.TryGetValue("email", out var mail) ? mail as string : null;
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > MaxSubjectLength || string.IsNullOrWhiteSpace(email))
        {
            throw new GoogleSignInException("The Google ID token does not say who the person is.");
        }

        var name = claims.TryGetValue("name", out var fullName) ? fullName as string : null;
        return new GoogleIdentity(subject, email, IsTrue(claims, "email_verified"), name);
    }

    // Google writes email_verified as a JSON boolean; read a string "true" too, so a format change cannot
    // silently turn "verified" into "unverified" or the other way round.
    private static bool IsTrue(IDictionary<string, object> claims, string name) =>
        claims.TryGetValue(name, out var value)
        && (value is true || (value is string text && string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)));

    private static bool SameText(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}
