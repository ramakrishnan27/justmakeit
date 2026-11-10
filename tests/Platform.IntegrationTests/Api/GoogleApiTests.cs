using System.Net;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Platform.Auth.Services.Google;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// Continue with Google, end to end through the real pipeline and a real database. Only Google itself is
/// replaced: its code exchange is a stand-in that answers with the identity a test chooses. The checks on
/// the real ID token are covered by the unit tests; whether Google accepts our requests is a manual check
/// with real credentials.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Database")]
public sealed class GoogleApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string ClientId = "client-123.apps.googleusercontent.com";
    private const string ClientSecret = "the-client-secret-that-must-never-leave-the-server";
    private const string StateCookie = "google_oauth";

    private readonly FakeGoogle _google = new();
    private ApiTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await ApiTestHost.StartWithDatabaseAsync(
            postgres,
            (settings, _) =>
            {
                settings["Google:ClientId"] = ClientId;
                settings["Google:ClientSecret"] = ClientSecret;
            },
            services => services.AddSingleton<IGoogleOAuthClient>(_google));
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private sealed class FakeGoogle : IGoogleOAuthClient
    {
        public GoogleIdentity Identity { get; set; } = new("sub-maya", "maya.rao@gmail.com", true, "Maya Rao");
        public List<(string Code, string Verifier, string RedirectUri, string Nonce)> Calls { get; } = [];

        public Task<GoogleIdentity> ExchangeCodeAsync(
            string code, string codeVerifier, string redirectUri, string expectedNonce, CancellationToken cancellationToken)
        {
            Calls.Add((code, codeVerifier, redirectUri, expectedNonce));
            return Task.FromResult(Identity);
        }
    }

    // --- Helpers -----------------------------------------------------------------------------

    private async Task<long> CountAsync(string table)
    {
        await using var owner = await _host.Database.OpenOwnerAsync();
        return await DbTestSupport.CountAsync(owner, table);
    }

    private async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var owner = await _host.Database.OpenOwnerAsync();
        return await DbTestSupport.ScalarAsync<T>(owner, sql);
    }

    private static string Query(string intent, string? model = null, string? name = null)
    {
        var parts = new List<string> { $"intent={intent}" };
        if (model is not null)
        {
            parts.Add($"businessModel={Uri.EscapeDataString(model)}");
        }

        if (name is not null)
        {
            parts.Add($"organizationName={Uri.EscapeDataString(name)}");
        }

        return string.Join('&', parts);
    }

    /// <summary>The browser's first step: ask the server where Google is, and keep the state cookie.</summary>
    private async Task<(string Cookie, string State, HttpResponseMessage Response)> StartAsync(string query)
    {
        var response = await _host.GetAsync($"/v1/auth/google/start?{query}");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("accounts.google.com", location.Host);
        var state = HttpUtility.ParseQueryString(location.Query)["state"]!;
        return (ApiTestHost.CookieValue(response, StateCookie)!, state, response);
    }

    /// <summary>The browser's last step: Google sends it to the callback with the code, the state and the cookie it holds.</summary>
    private async Task<HttpResponseMessage> CallbackAsync(string? cookie, string? state, string code = "the-one-time-code", string? error = null)
    {
        var query = error is null
            ? $"code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state ?? "")}"
            : $"error={error}&state={Uri.EscapeDataString(state ?? "")}";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/auth/google/callback?{query}");
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{StateCookie}={cookie}");
        }

        return await _host.Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> RegisterThroughGoogleAsync(string model = "agency", string name = "Bright Ideas")
    {
        var (cookie, state, _) = await StartAsync(Query("register", model, name));
        return await CallbackAsync(cookie, state);
    }

    private static string RedirectedTo(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        return response.Headers.Location!.OriginalString;
    }

    // --- Providers ---------------------------------------------------------------------------

    [Fact]
    public async Task Providers_SaysGoogleIsOn_WhenItIsConfigured_AndNeedsNoSignIn()
    {
        var response = await _host.GetAsync("/v1/auth/providers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await ApiTestHost.ReadJsonAsync(response)).GetProperty("google").GetBoolean());
    }

    [Fact]
    public async Task Providers_SaysGoogleIsOff_WhenItIsNotConfigured_AndNeverShowsAnyValue()
    {
        await using var plain = ApiTestHost.StartWithoutDatabase();

        var response = await plain.GetAsync("/v1/auth/providers");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("""{"google":false}""", text);
    }

    [Fact]
    public async Task Providers_NeverRevealsTheClientIdOrTheSecret()
    {
        var text = await (await _host.GetAsync("/v1/auth/providers")).Content.ReadAsStringAsync();

        Assert.DoesNotContain(ClientId, text);
        Assert.DoesNotContain(ClientSecret, text);
    }

    // --- Start -------------------------------------------------------------------------------

    [Fact]
    public async Task Start_RedirectsToGoogle_WithTheStateCookieAndNothingSecretInTheAddress()
    {
        var (cookie, state, response) = await StartAsync(Query("register", "company", "Northwind Foods"));

        var location = response.Headers.Location!;
        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.Equal(ClientId, query["client_id"]);
        Assert.StartsWith("http", query["redirect_uri"]);
        Assert.EndsWith("/v1/auth/google/callback", query["redirect_uri"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(state, query["state"]);
        Assert.DoesNotContain(ClientSecret, location.OriginalString);
        Assert.DoesNotContain("Northwind", location.OriginalString);
        Assert.False(string.IsNullOrEmpty(cookie));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Start_TheStateCookie_IsHttpOnly_Secure_Lax_ScopedToTheGooglePaths_AndShortLived()
    {
        var (_, _, response) = await StartAsync(Query("login"));

        var header = ApiTestHost.CookieHeader(response, StateCookie)!;
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/v1/auth/google", header, StringComparison.OrdinalIgnoreCase);
        var expires = DateTimeOffset.Parse(System.Text.RegularExpressions.Regex.Match(header, "expires=([^;]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange((expires - DateTimeOffset.UtcNow).TotalMinutes, 8, 11);
    }

    [Fact]
    public async Task Start_TheStateCookie_DoesNotShowWhatIsInsideIt()
    {
        var (cookie, _, _) = await StartAsync(Query("register", "creator", "Maya Makes Videos"));

        Assert.DoesNotContain("Maya", cookie);
        Assert.DoesNotContain("creator", cookie);
    }

    [Theory]
    [InlineData("intent=register&organizationName=Acme", "/register?error=google_invalid_request")]
    [InlineData("intent=register&businessModel=company", "/register?error=google_invalid_request")]
    [InlineData("intent=register&businessModel=individual&organizationName=Acme", "/register?error=google_invalid_request")]
    [InlineData("intent=nonsense", "/login?error=google_invalid_request")]
    [InlineData("", "/login?error=google_invalid_request")]
    public async Task Start_WithAnIncompleteRequest_SendsThePersonBackToTheFormWithAnErrorCode(string query, string expected)
    {
        var response = await _host.GetAsync($"/v1/auth/google/start?{query}");

        Assert.Equal(expected, RedirectedTo(response));
        Assert.Null(ApiTestHost.CookieValue(response, StateCookie));
    }

    [Fact]
    public async Task Start_WithGoogleNotConfigured_SendsThePersonBackWithAnErrorCode_NotToGoogle()
    {
        await using var plain = ApiTestHost.StartWithoutDatabase();

        var response = await plain.GetAsync($"/v1/auth/google/start?{Query("login")}");

        Assert.Equal("/login?error=google_unavailable", RedirectedTo(response));
    }

    // --- Callback: registering ---------------------------------------------------------------

    [Theory]
    [InlineData("company", "organizationadmin")]
    [InlineData("agency", "organizationadmin")]
    [InlineData("creator", "contentwriter")]
    public async Task Callback_RegistersTheOrganizationTheyChose_WithNoPassword_AndSignsThemIn(string model, string role)
    {
        var response = await RegisterThroughGoogleAsync(model, "Bright Ideas");

        Assert.Equal("/dashboard", RedirectedTo(response));
        Assert.Equal(1, await CountAsync("users"));
        Assert.Null(await ScalarAsync<string>("SELECT password_hash FROM users"));
        Assert.Equal("maya.rao@gmail.com", await ScalarAsync<string>("SELECT email FROM users"));
        Assert.Equal("Maya Rao", await ScalarAsync<string>("SELECT full_name FROM users"));
        Assert.Equal((model, "Bright Ideas", "trial"), (
            await ScalarAsync<string>("SELECT business_model FROM agencies"),
            await ScalarAsync<string>("SELECT name FROM agencies"),
            await ScalarAsync<string>("SELECT plan FROM agencies")));
        Assert.Equal(role, await ScalarAsync<string>("SELECT role FROM agency_members"));
        Assert.Equal(model, await ScalarAsync<string>("SELECT business_model FROM agency_members"));
        Assert.Equal(1, await CountAsync("workspaces"));
        Assert.Equal(("google", "sub-maya", "maya.rao@gmail.com"), (
            await ScalarAsync<string>("SELECT provider FROM user_identities"),
            await ScalarAsync<string>("SELECT subject FROM user_identities"),
            await ScalarAsync<string>("SELECT email FROM user_identities")));
    }

    [Fact]
    public async Task Callback_SetsTheMemberTokenCookie_SoThePersonIsSignedInAfterTheRedirect()
    {
        var response = await RegisterThroughGoogleAsync("creator", "Maya Makes Videos");

        var token = ApiTestHost.MemberCookieValue(response);
        Assert.NotNull(token);
        var header = ApiTestHost.MemberCookieHeader(response)!;
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/v1;", header + ";", StringComparison.OrdinalIgnoreCase);
        var expires = ApiTestHost.CookieExpires(response, ApiTestHost.MemberCookieName)!.Value;
        Assert.InRange(expires, DateTimeOffset.UtcNow.AddDays(30).AddMinutes(-2), DateTimeOffset.UtcNow.AddDays(30).AddMinutes(2));

        // The browser sends that cookie to the app's next requests, and the app knows who is signed in.
        var me = await ApiTestHost.ReadJsonAsync(await _host.GetAsync("/v1/auth/me", tokenCookie: token));
        Assert.Equal("creator", me.GetProperty("agency").GetProperty("businessModel").GetString());
        Assert.Equal("contentwriter", me.GetProperty("role").GetString());
        Assert.Equal("maya.rao@gmail.com", me.GetProperty("user").GetProperty("email").GetString());
    }

    [Fact]
    public async Task Callback_NeverPutsATokenInTheAddressOrTheBody()
    {
        var response = await RegisterThroughGoogleAsync();

        var token = ApiTestHost.MemberCookieValue(response)!;
        Assert.DoesNotContain(token, response.Headers.Location!.OriginalString);
        Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("accessToken", response.Headers.Location.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Callback_AsksGoogleWithTheVerifierAndNonceThatStartMadeUp_AndTheCodeFromTheAddress()
    {
        var (cookie, state, start) = await StartAsync(Query("login"));

        await CallbackAsync(cookie, state, code: "code-from-google");

        var call = Assert.Single(_google.Calls);
        Assert.Equal("code-from-google", call.Code);
        Assert.Equal(HttpUtility.ParseQueryString(start.Headers.Location!.Query)["nonce"], call.Nonce);
        Assert.EndsWith("/v1/auth/google/callback", call.RedirectUri);
        Assert.False(string.IsNullOrEmpty(call.Verifier));
        Assert.DoesNotContain(call.Verifier, start.Headers.Location.OriginalString);
    }

    [Fact]
    public async Task Callback_ClearsTheStateCookie_AndDoesNotLetTheAddressTravelOn()
    {
        var response = await RegisterThroughGoogleAsync();

        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        var cleared = ApiTestHost.CookieHeader(response, StateCookie)!;
        Assert.Contains("expires=Thu, 01 Jan 1970", cleared, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    // --- Callback: coming back ---------------------------------------------------------------

    [Fact]
    public async Task Callback_ForAKnownGoogleUser_SignsThemIn_WithoutCreatingAnythingNew()
    {
        await RegisterThroughGoogleAsync("agency", "Bright Ideas");
        var (cookie, state, _) = await StartAsync(Query("login"));

        var response = await CallbackAsync(cookie, state);

        Assert.Equal("/dashboard", RedirectedTo(response));
        Assert.NotNull(ApiTestHost.MemberCookieValue(response));
        Assert.Equal(1, await CountAsync("users"));
        Assert.Equal(1, await CountAsync("agencies"));
        Assert.Equal(1, await CountAsync("user_identities"));
    }

    [Fact]
    public async Task Callback_ForAKnownGoogleUserWhoUsesTheRegisterForm_SignsThemInInsteadOfMakingASecondOrganization()
    {
        await RegisterThroughGoogleAsync("agency", "Bright Ideas");

        var again = await RegisterThroughGoogleAsync("company", "A Different Name");

        Assert.Equal("/dashboard", RedirectedTo(again));
        Assert.Equal(1, await CountAsync("agencies"));
        Assert.Equal("Bright Ideas", await ScalarAsync<string>("SELECT name FROM agencies"));
    }

    [Fact]
    public async Task Callback_ALoginForSomeoneWeNeverSaw_IsSentToRegister_AndCreatesNothing()
    {
        var (cookie, state, _) = await StartAsync(Query("login"));

        var response = await CallbackAsync(cookie, state);

        Assert.Equal("/login?error=google_no_account", RedirectedTo(response));
        Assert.Null(ApiTestHost.MemberCookieValue(response));
        Assert.Equal(0, await CountAsync("users"));
    }

    // --- Callback: an address that already has an account ------------------------------------

    [Theory]
    [InlineData("register", "/register?error=google_email_taken")]
    [InlineData("login", "/login?error=google_email_taken")]
    public async Task Callback_WhenThatEmailIsAlreadyRegisteredWithAPassword_IsRefused_NeverLinked_AndSignsNobodyIn(string intent, string expected)
    {
        var existing = await _host.RegisterAsync(email: "maya.rao@gmail.com", agencyName: "Her own company", businessModel: "company");
        var (cookie, state, _) = await StartAsync(intent == "register" ? Query("register", "creator", "Maya Makes Videos") : Query("login"));

        var response = await CallbackAsync(cookie, state);

        Assert.Equal(expected, RedirectedTo(response));
        Assert.Null(ApiTestHost.MemberCookieValue(response));
        Assert.Equal(1, await CountAsync("users"));
        Assert.Equal(1, await CountAsync("agencies"));
        Assert.Equal(0, await CountAsync("user_identities"));
        Assert.NotNull(await ScalarAsync<string>("SELECT password_hash FROM users"));
        Assert.Equal(existing.UserId, await ScalarAsync<Guid>("SELECT id FROM users"));
    }

    [Fact]
    public async Task Callback_AGoogleEmailThatDiffersOnlyInCapitals_CountsAsTheSameAddress()
    {
        await _host.RegisterAsync(email: "maya.rao@gmail.com");
        _google.Identity = new GoogleIdentity("sub-other", "MAYA.RAO@GMAIL.COM", true, "Maya");
        var (cookie, state, _) = await StartAsync(Query("register", "creator", "Maya Makes Videos"));

        var response = await CallbackAsync(cookie, state);

        Assert.Equal("/register?error=google_email_taken", RedirectedTo(response));
        Assert.Equal(1, await CountAsync("users"));
    }

    [Fact]
    public async Task APasswordSignUp_WithAnAddressAGoogleUserHolds_IsRefusedAsTaken()
    {
        await RegisterThroughGoogleAsync();

        var response = await _host.PostAsync("/v1/auth/register", new
        {
            email = "maya.rao@gmail.com", password = ApiTestHost.DefaultPassword, organizationName = "Squatter", businessModel = "company"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, await CountAsync("users"));
    }

    [Theory]
    [InlineData("a long enough password")]
    [InlineData("")]
    [InlineData("pbkdf2$1$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==")]
    public async Task APasswordLogin_ForAGoogleOnlyUser_IsTheSame401AsAWrongPassword(string typed)
    {
        await RegisterThroughGoogleAsync();

        var response = await _host.PostAsync("/v1/auth/login", new { email = "maya.rao@gmail.com", password = typed.Length == 0 ? "x" : typed });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_credentials", (await ApiTestHost.ReadJsonAsync(response)).GetProperty("code").GetString());
        Assert.Null(ApiTestHost.MemberCookieValue(response));
    }

    // --- Callback: refusing what this browser did not start ----------------------------------

    [Fact]
    public async Task Callback_WithoutTheStateCookie_IsRefused_AndGoogleIsNeverAsked()
    {
        var (_, state, _) = await StartAsync(Query("register", "company", "Acme"));

        var response = await CallbackAsync(cookie: null, state);

        Assert.Equal("/login?error=google_state_invalid", RedirectedTo(response));
        Assert.Empty(_google.Calls);
        Assert.Equal(0, await CountAsync("users"));
    }

    [Fact]
    public async Task Callback_WithAStateThatIsNotTheOneInTheCookie_IsRefused_AndSendsThePersonBackToTheirForm()
    {
        var (cookie, _, _) = await StartAsync(Query("register", "company", "Acme"));
        var (_, otherState, _) = await StartAsync(Query("register", "company", "Acme"));

        var response = await CallbackAsync(cookie, otherState);

        Assert.Equal("/register?error=google_state_invalid", RedirectedTo(response));
        Assert.Empty(_google.Calls);
        Assert.Equal(0, await CountAsync("users"));
    }

    [Fact]
    public async Task Callback_WithACookieThatWasTamperedWith_IsRefused()
    {
        var (cookie, state, _) = await StartAsync(Query("register", "company", "Acme"));
        var middle = cookie.Length / 2;
        var tampered = cookie[..middle] + (cookie[middle] == 'A' ? 'B' : 'A') + cookie[(middle + 1)..];

        var response = await CallbackAsync(tampered, state);

        Assert.Equal("/login?error=google_state_invalid", RedirectedTo(response));
        Assert.Empty(_google.Calls);
    }

    [Fact]
    public async Task Callback_WhenThePersonRefusedAtGoogle_ComesBackWithAnErrorCode()
    {
        var (cookie, state, _) = await StartAsync(Query("register", "company", "Acme"));

        var response = await CallbackAsync(cookie, state, error: "access_denied");

        Assert.Equal("/register?error=google_denied", RedirectedTo(response));
        Assert.Empty(_google.Calls);
    }

    [Fact]
    public async Task Callback_WithAnEmailGoogleHasNotVerified_IsRefused()
    {
        _google.Identity = new GoogleIdentity("sub-x", "someone@gmail.com", false, "Someone");

        var response = await RegisterThroughGoogleAsync();

        Assert.Equal("/register?error=google_email_unverified", RedirectedTo(response));
        Assert.Equal(0, await CountAsync("users"));
    }

    // --- Closed to what it should not serve --------------------------------------------------

    [Theory]
    [InlineData("/v1/auth/google/start?intent=login")]
    [InlineData("/v1/auth/google/callback")]
    [InlineData("/v1/auth/providers")]
    public async Task TheGoogleEndpoints_AreOpenToAVisitorWhoIsNotSignedIn(string path)
    {
        var response = await _host.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TheStartAndCallbackAddresses_ShareTheCredentialsRateLimit()
    {
        await using var limited = await ApiTestHost.StartWithDatabaseAsync(
            postgres,
            (settings, _) =>
            {
                settings["Google:ClientId"] = ClientId;
                settings["Google:ClientSecret"] = ClientSecret;
                settings["RateLimiting:Auth:CredentialRequestsPerWindow"] = "3";
            });

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await limited.GetAsync("/v1/auth/google/start?intent=login", remoteIp: "203.0.113.9")).StatusCode);
        }

        Assert.Equal([HttpStatusCode.Found, HttpStatusCode.Found, HttpStatusCode.Found, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], statuses);
    }
}
