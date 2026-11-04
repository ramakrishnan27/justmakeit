using System.Net;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// The /v1/auth/* rate limits, through the real pipeline. Requests are chosen so they are answered
/// before any query runs (a malformed body or a missing cookie), so no database is involved.
/// </summary>
public sealed class AuthRateLimitTests
{
    private static ApiTestHost StartHost(int credentials = 3, int session = 2, int windowSeconds = 60) =>
        ApiTestHost.StartWithoutDatabase(settings =>
        {
            settings["RateLimiting:Auth:CredentialRequestsPerWindow"] = credentials.ToString();
            settings["RateLimiting:Auth:SessionRequestsPerWindow"] = session.ToString();
            settings["RateLimiting:Auth:WindowSeconds"] = windowSeconds.ToString();
        });

    private static Task<HttpResponseMessage> BadLogin(ApiTestHost host, string? ip = null) =>
        host.PostAsync("/v1/auth/login", new { email = "", password = "" }, remoteIp: ip);

    // Logout is a session endpoint that is answered (204) without touching a database, so it counts against the session budget.
    private static Task<HttpResponseMessage> LogoutWithoutSession(ApiTestHost host, string? ip = null) =>
        host.PostAsync("/v1/auth/logout", remoteIp: ip);

    [Fact]
    public async Task Login_IsAnsweredNormallyUpToTheLimit_ThenRejectedWith429()
    {
        await using var host = StartHost(credentials: 3);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await BadLogin(host)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await BadLogin(host)).StatusCode);
    }

    [Fact]
    public async Task TheRejection_IsAProblemResponse_WithRetryAfter()
    {
        await using var host = StartHost(credentials: 1, windowSeconds: 60);
        await BadLogin(host);

        var response = await BadLogin(host);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var retryAfter = int.Parse(response.Headers.GetValues("Retry-After").Single());
        Assert.InRange(retryAfter, 1, 60);

        var body = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal(429, body.GetProperty("status").GetInt32());
        Assert.Equal("rate_limited", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ChangePassword_ChecksAPassword_SoItSharesTheCredentialsBudgetWithLogin()
    {
        await using var host = StartHost(credentials: 3);
        var token = host.Factory.CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), Platform.Helpers.Constants.OrganizationRole.OrganizationAdmin);

        // A body with nothing in it is refused before any query runs, so no database is involved.
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync("/v1/auth/change-password", new { }, tokenCookie: token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await BadLogin(host)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync("/v1/auth/change-password", new { }, tokenCookie: token)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.PostAsync("/v1/auth/change-password", new { }, tokenCookie: token)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await BadLogin(host)).StatusCode);
    }

    [Fact]
    public async Task Register_AndLogin_ShareOneCredentialsBudget()
    {
        await using var host = StartHost(credentials: 3);

        Assert.Equal(HttpStatusCode.BadRequest, (await BadLogin(host)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync("/v1/auth/register", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await BadLogin(host)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.PostAsync("/v1/auth/register", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await BadLogin(host)).StatusCode);
    }

    [Fact]
    public async Task SiteLogin_SharesTheCredentialsBudget_WithAgencyLoginAndRegister()
    {
        await using var host = StartHost(credentials: 3);

        Assert.Equal(HttpStatusCode.BadRequest, (await BadLogin(host)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync("/v1/auth/register", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync("/v1/site/login", new { })).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.PostAsync("/v1/site/login", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await BadLogin(host)).StatusCode);
    }

    [Fact]
    public async Task SiteSessionEndpoints_ShareTheSessionBudget_WithTheAgencyOnes()
    {
        await using var host = StartHost(session: 3);

        Assert.Equal(HttpStatusCode.NoContent, (await LogoutWithoutSession(host)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.PostAsync("/v1/site/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetAsync("/v1/site/me")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.PostAsync("/v1/site/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.GetAsync("/v1/site/overview", host.Factory.CreateSiteAdminAccessToken(Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task EachClientAddress_HasItsOwnBudget()
    {
        await using var host = StartHost(credentials: 2);

        await BadLogin(host, "203.0.113.1");
        await BadLogin(host, "203.0.113.1");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await BadLogin(host, "203.0.113.1")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await BadLogin(host, "203.0.113.2")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await BadLogin(host, "2001:db8::1")).StatusCode);
    }

    [Theory]
    [InlineData("logout", HttpStatusCode.NoContent)]
    [InlineData("me", HttpStatusCode.Unauthorized)]
    public async Task SessionEndpoints_HaveTheirOwnLimit(string endpoint, HttpStatusCode answered)
    {
        await using var host = StartHost(session: 2);

        Task<HttpResponseMessage> Call() => endpoint == "logout"
            ? LogoutWithoutSession(host)
            : host.GetAsync("/v1/auth/me");

        Assert.Equal(answered, (await Call()).StatusCode);
        Assert.Equal(answered, (await Call()).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Call()).StatusCode);
    }

    [Fact]
    public async Task TheLimitCountsRequestsBeforeAuthentication_SoGuessingTokensIsLimitedToo()
    {
        await using var host = StartHost(session: 1);

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetAsync("/v1/auth/me", accessToken: "guess-1")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.GetAsync("/v1/auth/me", accessToken: "guess-2")).StatusCode);
    }

    [Fact]
    public async Task ExhaustingCredentials_DoesNotBlockTheSessionEndpoints_AndTheOtherWayAround()
    {
        await using var host = StartHost(credentials: 1, session: 1);

        await BadLogin(host);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await BadLogin(host)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await LogoutWithoutSession(host)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LogoutWithoutSession(host)).StatusCode);
    }

    [Fact]
    public async Task OtherEndpoints_AreNotCountedAgainstTheAuthLimits()
    {
        await using var host = StartHost(credentials: 1, session: 1);

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetAsync("/v1/workspaces")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await BadLogin(host)).StatusCode);
    }

    [Fact]
    public async Task TheBudgetComesBack_WhenTheWindowHasPassed()
    {
        await using var host = StartHost(credentials: 1, windowSeconds: 1);

        await BadLogin(host);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await BadLogin(host)).StatusCode);

        await Task.Delay(TimeSpan.FromMilliseconds(1_300));

        Assert.Equal(HttpStatusCode.BadRequest, (await BadLogin(host)).StatusCode);
    }

    [Theory]
    [InlineData("CredentialRequestsPerWindow", "0")]
    [InlineData("SessionRequestsPerWindow", "0")]
    [InlineData("WindowSeconds", "0")]
    public void AnInvalidLimit_StopsTheAppAtStartup_WithAClearMessage(string setting, string value)
    {
        var settings = ApiHostFactory.ValidSettings();
        settings[$"RateLimiting:Auth:{setting}"] = value;
        using var factory = new ApiHostFactory(settings);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("RateLimiting:Auth", exception.ToString());
    }
}
