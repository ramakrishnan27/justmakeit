using System.Net;
using Platform.Helpers.Constants;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// Who may call what, through the real pipeline. Every case here is decided by authentication,
/// authorization policies or request validation before any query runs, so no database is involved.
/// </summary>
public sealed class AuthorizationEndpointTests : IAsyncLifetime
{
    private ApiTestHost _host = null!;

    public Task InitializeAsync()
    {
        _host = ApiTestHost.StartWithoutDatabase();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private string Token(AgencyRole role) => _host.Factory.CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), role);

    // --- Closed by default -------------------------------------------------------------------

    [Theory]
    [InlineData("GET", "/v1/workspaces")]
    [InlineData("POST", "/v1/workspaces")]
    [InlineData("GET", "/v1/auth/me")]
    [InlineData("POST", "/v1/auth/switch-agency")]
    public async Task ProtectedEndpoints_AnswerAnAnonymousCaller_With401(string method, string path)
    {
        var response = method == "GET"
            ? await _host.GetAsync(path)
            : await _host.PostAsync(path, new { name = "x", agencyId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.e30.invalidsignature")]
    public async Task AMalformedOrUnsignedToken_IsAnsweredWith401(string token)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.GetAsync("/v1/workspaces", token)).StatusCode);
    }

    [Fact]
    public async Task ATokenSignedWithAnotherKey_IsAnsweredWith401()
    {
        var forged = TestAccessTokens.Create(new string('z', 64), Guid.NewGuid(), Guid.NewGuid(), AgencyRole.Owner);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.GetAsync("/v1/workspaces", forged)).StatusCode);
    }

    // --- Roles -------------------------------------------------------------------------------

    [Theory]
    [InlineData(AgencyRole.Manager)]
    [InlineData(AgencyRole.Viewer)]
    public async Task CreatingAWorkspace_IsForbidden_ForManagersAndViewers(AgencyRole role)
    {
        var response = await _host.PostAsync("/v1/workspaces", new { name = "Client one" }, Token(role));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(AgencyRole.Owner)]
    [InlineData(AgencyRole.Admin)]
    public async Task CreatingAWorkspace_PassesTheRolePolicy_ForOwnersAndAdmins(AgencyRole role)
    {
        // An invalid body is answered 400 by the controller, which only runs once the policy has passed.
        var response = await _host.PostAsync("/v1/workspaces", new { name = "" }, Token(role));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- Site admin console: who may call what -----------------------------------------------

    private string SiteAdminToken() => _host.Factory.CreateSiteAdminAccessToken(Guid.NewGuid());

    [Theory]
    [InlineData("/v1/site/me")]
    [InlineData("/v1/site/overview")]
    [InlineData("/v1/site/agencies")]
    public async Task SiteEndpoints_AnswerAnAnonymousCaller_With401(string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData(AgencyRole.Owner, "/v1/site/me")]
    [InlineData(AgencyRole.Admin, "/v1/site/overview")]
    [InlineData(AgencyRole.Manager, "/v1/site/agencies")]
    [InlineData(AgencyRole.Viewer, "/v1/site/overview")]
    public async Task SiteEndpoints_AreForbidden_ForEveryAgencyRole_EvenTheOwner(AgencyRole role, string path)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _host.GetAsync(path, Token(role))).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/v1/workspaces")]
    [InlineData("POST", "/v1/workspaces")]
    [InlineData("GET", "/v1/auth/me")]
    [InlineData("POST", "/v1/auth/switch-agency")]
    public async Task ASiteAdminToken_IsForbiddenOnEveryAgencyEndpoint(string method, string path)
    {
        var token = SiteAdminToken();

        var response = method == "GET"
            ? await _host.GetAsync(path, token)
            : await _host.PostAsync(path, new { name = "x", agencyId = Guid.NewGuid() }, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ASiteAdminToken_ReachesTheSiteEndpoints_WithoutAnAgency()
    {
        // An invalid page is answered 400 by the controller, which only runs once the policy has passed.
        var response = await _host.GetAsync("/v1/site/agencies?page=0", SiteAdminToken());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("page", out _));
    }

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("page=-2", "page")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=101", "pageSize")]
    public async Task TheAgencyListRefusesBadPaging_BeforeAskingTheDatabase(string query, string field)
    {
        var response = await _host.GetAsync($"/v1/site/agencies?{query}", SiteAdminToken());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task TheAgencyListRefusesAPageThatIsNotANumber()
    {
        var response = await _host.GetAsync("/v1/site/agencies?page=abc", SiteAdminToken());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ASiteAdminTokenSignedWithAnotherKey_IsAnsweredWith401()
    {
        var forged = TestAccessTokens.CreateSiteAdmin(new string('z', 64), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.GetAsync("/v1/site/overview", forged)).StatusCode);
    }

    [Fact]
    public async Task SiteLogin_IsReachableWithoutAToken_AndReportsEveryBadField()
    {
        var response = await _host.PostAsync("/v1/site/login", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
        Assert.True(body.GetProperty("errors").TryGetProperty("email", out _));
        Assert.True(body.GetProperty("errors").TryGetProperty("password", out _));
        Assert.Null(ApiTestHost.SiteCookieHeader(response));
    }

    [Fact]
    public async Task SiteRefresh_WithoutACookie_IsRejectedByTheServiceNotTheAuthMiddleware()
    {
        var response = await _host.PostAsync("/v1/site/refresh");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_refresh_token", (await ApiTestHost.ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task SiteRefresh_ReadsOnlyItsOwnCookie_NotTheAgencyOne()
    {
        // An agency refresh cookie sent to the site endpoint is simply not the cookie it looks for.
        var response = await _host.PostAsync("/v1/site/refresh", refreshCookie: "some-agency-token", cookieName: ApiTestHost.AgencyCookieName);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_refresh_token", (await ApiTestHost.ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task SiteLogout_WithoutACookie_Succeeds_AndExpiresTheSiteCookieScopedToTheSitePath()
    {
        var response = await _host.PostAsync("/v1/site/logout");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = ApiTestHost.SiteCookieHeader(response);
        Assert.NotNull(cookie);
        Assert.Contains("path=/v1/site", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=Thu, 01 Jan 1970", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Null(ApiTestHost.RefreshCookieHeader(response)); // the agency cookie is never touched
    }

    [Theory]
    [InlineData("/v1/site/login")]
    [InlineData("/v1/site/refresh")]
    [InlineData("/v1/site/logout")]
    public async Task EverySiteAuthResponse_IsMarkedNoStore(string path)
    {
        var response = await _host.PostAsync(path, new { });

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }

    // --- Public endpoints --------------------------------------------------------------------

    [Fact]
    public async Task Register_IsReachableWithoutAToken_AndReportsEveryBadField()
    {
        var response = await _host.PostAsync("/v1/auth/register", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
        var errors = body.GetProperty("errors");
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("password", out _));
        Assert.True(errors.TryGetProperty("agencyName", out _));
    }

    [Fact]
    public async Task Login_IsReachableWithoutAToken()
    {
        var response = await _host.PostAsync("/v1/auth/login", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("password", out _));
    }

    [Fact]
    public async Task Refresh_WithoutACookie_IsRejectedByTheServiceNotTheAuthMiddleware()
    {
        var response = await _host.PostAsync("/v1/auth/refresh");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_refresh_token", (await ApiTestHost.ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Logout_WithoutACookie_Succeeds_AndExpiresTheCookie()
    {
        var response = await _host.PostAsync("/v1/auth/logout");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = ApiTestHost.RefreshCookieHeader(response);
        Assert.NotNull(cookie);
        Assert.Contains("path=/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=Thu, 01 Jan 1970", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Null(ApiTestHost.RefreshCookieValue(response));
    }

    [Theory]
    [InlineData("/v1/auth/login")]
    [InlineData("/v1/auth/register")]
    [InlineData("/v1/auth/refresh")]
    [InlineData("/v1/auth/logout")]
    public async Task EveryAuthResponse_IsMarkedNoStore(string path)
    {
        var response = await _host.PostAsync(path, new { });

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }
}
