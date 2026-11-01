using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Platform.Auth.Services.Google;
using Platform.Auth.Settings;
using Platform.Auth.Tests.Support;

namespace Platform.Auth.Tests;

/// <summary>The request the server sends to Google, and what it does with each kind of answer. Google itself is replaced by a stub handler.</summary>
public sealed class GoogleOAuthClientTests
{
    private const string ClientId = "client-123.apps.googleusercontent.com";
    private const string ClientSecret = "the-client-secret";
    private const string Nonce = "nonce-1";

    private readonly FixedTimeProvider _time = FixedTimeProvider.Create();
    private readonly RsaSecurityKey _googleKey = new(RSA.Create(2048)) { KeyId = "k1" };
    private readonly StubHandler _handler = new();

    private sealed class FixedKeys(IReadOnlyCollection<SecurityKey> keys) : IGoogleSigningKeys
    {
        public Task<IReadOnlyCollection<SecurityKey>> GetAsync(CancellationToken cancellationToken) => Task.FromResult(keys);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        public Func<HttpResponseMessage> Respond { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return Respond();
        }
    }

    private GoogleOAuthClient Create(GoogleSettings? settings = null) => new(
        new HttpClient(_handler),
        Options.Create(settings ?? new GoogleSettings { ClientId = ClientId, ClientSecret = ClientSecret }),
        new GoogleIdTokenValidator(new FixedKeys([_googleKey]), _time));

    private string IdToken() => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = "https://accounts.google.com",
        Audience = ClientId,
        IssuedAt = _time.Now.UtcDateTime,
        NotBefore = _time.Now.UtcDateTime,
        Expires = _time.Now.AddHours(1).UtcDateTime,
        Claims = new Dictionary<string, object>
        {
            ["sub"] = "1234567890", ["email"] = "maya@gmail.com", ["email_verified"] = true, ["name"] = "Maya", ["nonce"] = Nonce
        },
        SigningCredentials = new SigningCredentials(_googleKey, SecurityAlgorithms.RsaSha256)
    });

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private Task<GoogleIdentity> ExchangeAsync(GoogleOAuthClient client) =>
        client.ExchangeCodeAsync("the-code", "the-verifier", "http://localhost:5080/v1/auth/google/callback", Nonce, CancellationToken.None);

    [Fact]
    public async Task TheCodeGoesToGoogleOverTheBackChannel_WithTheSecretAndTheVerifier()
    {
        _handler.Respond = () => Json(HttpStatusCode.OK, $$"""{"id_token":"{{IdToken()}}","access_token":"ignored"}""");

        await ExchangeAsync(Create());

        Assert.Equal(HttpMethod.Post, _handler.Request!.Method);
        Assert.Equal("https://oauth2.googleapis.com/token", _handler.Request.RequestUri!.ToString());
        var form = System.Web.HttpUtility.ParseQueryString(_handler.Body!);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("the-code", form["code"]);
        Assert.Equal(ClientId, form["client_id"]);
        Assert.Equal(ClientSecret, form["client_secret"]);
        Assert.Equal("the-verifier", form["code_verifier"]);
        Assert.Equal("http://localhost:5080/v1/auth/google/callback", form["redirect_uri"]);
    }

    [Fact]
    public async Task TheSecret_TravelsInTheBodyOnly_NeverInTheAddress()
    {
        _handler.Respond = () => Json(HttpStatusCode.OK, $$"""{"id_token":"{{IdToken()}}"}""");

        await ExchangeAsync(Create());

        Assert.DoesNotContain(ClientSecret, _handler.Request!.RequestUri!.ToString());
        Assert.DoesNotContain("the-code", _handler.Request.RequestUri.ToString());
    }

    [Fact]
    public async Task AGoodAnswer_GivesTheCheckedIdentity()
    {
        _handler.Respond = () => Json(HttpStatusCode.OK, $$"""{"id_token":"{{IdToken()}}"}""");

        var identity = await ExchangeAsync(Create());

        Assert.Equal(new GoogleIdentity("1234567890", "maya@gmail.com", true, "Maya"), identity);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AnErrorFromGoogle_IsOurExceptionWithNothingSensitiveInIt(HttpStatusCode status)
    {
        _handler.Respond = () => Json(status, """{"error":"invalid_grant","error_description":"Bad Request"}""");

        var exception = await Assert.ThrowsAsync<GoogleSignInException>(() => ExchangeAsync(Create()));

        Assert.Contains(((int)status).ToString(), exception.Message);
        Assert.DoesNotContain("the-code", exception.Message);
        Assert.DoesNotContain(ClientSecret, exception.Message);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"id_token":""}""")]
    [InlineData("""{"id_token":null}""")]
    [InlineData("""{"access_token":"only"}""")]
    public async Task AnAnswerWithoutAnIdToken_IsRefused(string json)
    {
        _handler.Respond = () => Json(HttpStatusCode.OK, json);

        await Assert.ThrowsAsync<GoogleSignInException>(() => ExchangeAsync(Create()));
    }

    [Fact]
    public async Task AnAnswerThatIsNotJson_IsRefused()
    {
        _handler.Respond = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>oops</html>", Encoding.UTF8, "text/html") };

        await Assert.ThrowsAsync<GoogleSignInException>(() => ExchangeAsync(Create()));
    }

    [Fact]
    public async Task AnIdTokenForTheWrongNonce_IsRefused()
    {
        _handler.Respond = () => Json(HttpStatusCode.OK, $$"""{"id_token":"{{IdToken()}}"}""");

        await Assert.ThrowsAsync<GoogleSignInException>(() =>
            Create().ExchangeCodeAsync("c", "v", "http://localhost/cb", "another-nonce", CancellationToken.None));
    }

    [Fact]
    public async Task WhenGoogleCannotBeReached_ItIsOurExceptionToo()
    {
        _handler.Respond = () => throw new HttpRequestException("no route to host");

        await Assert.ThrowsAsync<GoogleSignInException>(() => ExchangeAsync(Create()));
    }

    [Fact]
    public async Task WithGoogleNotConfigured_NothingIsSent()
    {
        await Assert.ThrowsAsync<GoogleSignInException>(() => ExchangeAsync(Create(new GoogleSettings())));

        Assert.Null(_handler.Request);
    }
}
