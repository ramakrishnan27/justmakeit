using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Platform.Auth.Services.Google;
using Platform.Auth.Tests.Support;

namespace Platform.Auth.Tests;

/// <summary>
/// The checks on Google's ID token, with tokens signed by a key of the test's own that stands in for Google's.
/// Nothing here reaches Google: whether the real round trip works is a manual check with real credentials.
/// </summary>
public sealed class GoogleIdTokenValidatorTests
{
    private const string ClientId = "client-123.apps.googleusercontent.com";
    private const string Nonce = "the-nonce-this-sign-in-started-with";

    private readonly FixedTimeProvider _time = FixedTimeProvider.Create();
    private readonly RsaSecurityKey _googleKey = new(RSA.Create(2048)) { KeyId = "google-key-1" };
    private readonly GoogleIdTokenValidator _validator;

    public GoogleIdTokenValidatorTests()
    {
        _validator = new GoogleIdTokenValidator(new FixedKeys([_googleKey]), _time);
    }

    private sealed class FixedKeys(IReadOnlyCollection<SecurityKey> keys) : IGoogleSigningKeys
    {
        public Task<IReadOnlyCollection<SecurityKey>> GetAsync(CancellationToken cancellationToken) => Task.FromResult(keys);
    }

    private string Token(
        Action<Dictionary<string, object>>? change = null,
        SecurityKey? signWith = null,
        string algorithm = SecurityAlgorithms.RsaSha256)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = "1234567890",
            ["email"] = "maya@gmail.com",
            ["email_verified"] = true,
            ["name"] = "Maya Rao",
            ["nonce"] = Nonce
        };
        change?.Invoke(claims);

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://accounts.google.com",
            Audience = ClientId,
            IssuedAt = _time.Now.UtcDateTime,
            NotBefore = _time.Now.UtcDateTime,
            Expires = _time.Now.AddHours(1).UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(signWith ?? _googleKey, algorithm)
        });
    }

    private Task<GoogleIdentity> ValidateAsync(string token, string clientId = ClientId, string nonce = Nonce) =>
        _validator.ValidateAsync(token, clientId, nonce, CancellationToken.None);

    // --- A good token ------------------------------------------------------------------------

    [Fact]
    public async Task AGoodToken_GivesTheSubjectEmailNameAndVerifiedFlag()
    {
        var identity = await ValidateAsync(Token());

        Assert.Equal(new GoogleIdentity("1234567890", "maya@gmail.com", true, "Maya Rao"), identity);
    }

    [Fact]
    public async Task TheNameIsOptional()
    {
        var identity = await ValidateAsync(Token(claims => claims.Remove("name")));

        Assert.Null(identity.FullName);
    }

    [Theory]
    [InlineData("https://accounts.google.com")]
    [InlineData("accounts.google.com")]
    public async Task BothSpellingsOfGoogleAsIssuer_AreAccepted(string issuer)
    {
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = ClientId,
            IssuedAt = _time.Now.UtcDateTime,
            NotBefore = _time.Now.UtcDateTime,
            Expires = _time.Now.AddHours(1).UtcDateTime,
            Claims = new Dictionary<string, object> { ["sub"] = "1", ["email"] = "a@b.co", ["nonce"] = Nonce },
            SigningCredentials = new SigningCredentials(_googleKey, SecurityAlgorithms.RsaSha256)
        });

        Assert.Equal("1", (await ValidateAsync(token)).Subject);
    }

    // --- email_verified ----------------------------------------------------------------------

    [Fact]
    public async Task AnUnverifiedEmail_IsReportedAsUnverified_NotRefusedHere()
    {
        var identity = await ValidateAsync(Token(claims => claims["email_verified"] = false));

        Assert.False(identity.EmailVerified);
    }

    [Fact]
    public async Task AMissingEmailVerifiedClaim_MeansNotVerified()
    {
        var identity = await ValidateAsync(Token(claims => claims.Remove("email_verified")));

        Assert.False(identity.EmailVerified);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    [InlineData("1", false)]
    public async Task EmailVerifiedWrittenAsAString_IsReadByItsWord(string text, bool expected)
    {
        var identity = await ValidateAsync(Token(claims => claims["email_verified"] = text));

        Assert.Equal(expected, identity.EmailVerified);
    }

    // --- Refused -----------------------------------------------------------------------------

    [Fact]
    public async Task ATokenSignedWithAnotherKey_IsRefused()
    {
        var stranger = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "google-key-1" };

        await Assert.ThrowsAsync<GoogleSignInException>(() => ValidateAsync(Token(signWith: stranger)));
    }

    [Fact]
    public async Task ATokenSignedWithTheSharedSecretTrick_IsRefused()
    {
        // The classic attack: sign with HMAC, using the public key as the secret. Only RS256 is allowed.
        var hmac = new SymmetricSecurityKey(new string('k', 64).Select(c => (byte)c).ToArray());

        await Assert.ThrowsAsync<GoogleSignInException>(() => ValidateAsync(Token(signWith: hmac, algorithm: SecurityAlgorithms.HmacSha256)));
    }

    [Fact]
    public async Task ATokenWithoutASignature_IsRefused()
    {
        var unsigned = new JsonWebTokenHandler().CreateToken(
            """{"iss":"https://accounts.google.com","aud":"client-123.apps.googleusercontent.com","sub":"1","email":"a@b.co","nonce":"the-nonce-this-sign-in-started-with","exp":4102444800}""");

        await Assert.ThrowsAsync<GoogleSignInException>(() => ValidateAsync(unsigned));
    }

    [Fact]
    public async Task ATokenForAnotherClient_IsRefused()
    {
        await Assert.ThrowsAsync<GoogleSignInException>(() => ValidateAsync(Token(), clientId: "someone-else.apps.googleusercontent.com"));
    }

    [Fact]
    public async Task ATokenFromAnotherIssuer_IsRefused()
    {
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://evil.example.com",
            Audience = ClientId,
            IssuedAt = _time.Now.UtcDateTime,
            NotBefore = _time.Now.UtcDateTime,
            Expires = _time.Now.AddHours(1).UtcDateTime,
            Claims = new Dictionary<string, object> { ["sub"] = "1", ["email"] = "a@b.co", ["nonce"] = Nonce },
            SigningCredentials = new SigningCredentials(_googleKey, SecurityAlgorithms.RsaSha256)
        });

        await Assert.ThrowsAsync<GoogleSignInException>(() => ValidateAsync(token));
    }

    [Fact]
    public async Task AnExpiredToken_IsRefused()
    {
        var token = Token();
        _time.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(3));

        await Assert.ThrowsAsync<GoogleSignInException>(() => ValidateAsync(token));
    }

    [Fact]
    public async Task ATokenThatJustExpired_IsStillAcceptedWithinTheClockSkew()
    {
        var token = Token();
        _time.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));

        Assert.Equal("1234567890", (await ValidateAsync(token)).Subject);
    }

    [Fact]
    public async Task ATokenWithTheWrongNonce_IsRefused_SoAnotherSignInsTokenCannotBeReplayed()
    {
        await Assert.ThrowsAsync<GoogleSignInException>(() => ValidateAsync(Token(), nonce: "some-other-nonce"));
    }

    [Fact]
    public async Task ATokenWithNoNonce_IsRefused()
    {
        await Assert.ThrowsAsync<GoogleSignInException>(() => ValidateAsync(Token(claims => claims.Remove("nonce"))));
    }

    [Theory]
    [InlineData("sub")]
    [InlineData("email")]
    public async Task ATokenThatDoesNotSayWhoThePersonIs_IsRefused(string missing)
    {
        await Assert.ThrowsAsync<GoogleSignInException>(() => ValidateAsync(Token(claims => claims.Remove(missing))));
    }

    [Fact]
    public async Task ASubjectLongerThanTheDatabaseKeeps_IsRefused()
    {
        await Assert.ThrowsAsync<GoogleSignInException>(() => ValidateAsync(Token(claims => claims["sub"] = new string('9', 256))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not.a.jwt")]
    [InlineData("garbage")]
    public async Task JunkInPlaceOfAToken_IsRefused_WithOurOwnException(string junk)
    {
        await Assert.ThrowsAsync<GoogleSignInException>(() => ValidateAsync(junk));
    }
}
