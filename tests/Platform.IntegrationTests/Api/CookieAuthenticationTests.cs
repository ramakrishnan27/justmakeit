using System.Net;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Platform.Helpers.Constants;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// The access token as the web pages carry it: an HttpOnly cookie, not a header. No database is involved: a request
/// that gets past authentication and authorization is answered by the controller with a 400 for an empty form, and one
/// that does not is answered 401 or 403 before any controller runs.
/// </summary>
public sealed class CookieAuthenticationTests : IAsyncLifetime
{
    private ApiTestHost _host = null!;

    public Task InitializeAsync()
    {
        _host = ApiTestHost.StartWithoutDatabase();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private string MemberToken() => _host.Factory.CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.OrganizationAdmin);

    private string SiteToken() => _host.Factory.CreateSiteAdminAccessToken(Guid.NewGuid());

    private string Mint(string? key = null, TimeSpan? lifetime = null)
    {
        var now = DateTime.UtcNow;
        var lifespan = lifetime ?? TimeSpan.FromDays(30);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "platform",
            Audience = "platform-web",
            IssuedAt = lifespan > TimeSpan.Zero ? now : now + lifespan - TimeSpan.FromMinutes(1),
            NotBefore = lifespan > TimeSpan.Zero ? now : now + lifespan - TimeSpan.FromMinutes(1),
            Expires = now + lifespan,
            Claims = new Dictionary<string, object>
            {
                [ClaimNames.UserId] = Guid.NewGuid().ToString(),
                [ClaimNames.AgencyId] = Guid.NewGuid().ToString(),
                [ClaimNames.Role] = "organizationadmin",
                [ClaimNames.Permission] = DefaultKeys(),
                [ClaimNames.PermissionVersion] = DefaultGrantsPermissionService.Version
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key ?? _host.Factory.SigningKey)), SecurityAlgorithms.HmacSha256)
        });
    }

    private static string[] DefaultKeys() => [.. PermissionKeys.All];

    // A body the controller refuses with 400 shows the request got through authentication and authorization.
    private Task<HttpResponseMessage> CreateWorkspaceAsync(string? tokenCookie = null, string? bearer = null, string cookieName = ApiTestHost.MemberCookieName) =>
        _host.PostAsync("/v1/workspaces", new { name = "" }, bearer, tokenCookie, cookieName: cookieName);

    [Fact]
    public async Task AMemberTokenInTheMemberCookie_IsAuthenticated()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateWorkspaceAsync(tokenCookie: MemberToken())).StatusCode);
    }

    [Fact]
    public async Task TheSameTokenInAnAuthorizationHeader_IsAuthenticatedToo()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateWorkspaceAsync(bearer: MemberToken())).StatusCode);
    }

    [Fact]
    public async Task NoCookieAndNoHeader_IsAnonymous_AndRefused()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateWorkspaceAsync()).StatusCode);
    }

    [Theory]
    [InlineData("not.a.jwt")]
    [InlineData("garbage")]
    [InlineData("a.b.c")]
    public async Task AGarbageCookie_IsRefused(string cookie)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateWorkspaceAsync(tokenCookie: cookie)).StatusCode);
    }

    [Fact]
    public async Task AnExpiredToken_InTheCookie_IsRefused_SoThePersonSignsInAgain()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateWorkspaceAsync(tokenCookie: Mint(lifetime: TimeSpan.FromMinutes(-10)))).StatusCode);
    }

    [Fact]
    public async Task ATokenSignedWithAnotherKey_InTheCookie_IsRefused()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateWorkspaceAsync(tokenCookie: Mint(key: new string('x', 64)))).StatusCode);
    }

    [Fact]
    public async Task AGoodTokenMintedHere_IsAccepted_SoTheTestsAboveRefuseForTheRightReason()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateWorkspaceAsync(tokenCookie: Mint())).StatusCode);
    }

    [Fact]
    public async Task ACookieWithAnotherName_IsNotAToken()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateWorkspaceAsync(tokenCookie: MemberToken(), cookieName: "token")).StatusCode);
    }

    [Fact]
    public async Task TheHeaderDecides_WhenThereIsOneBesideACookie()
    {
        // A request that names its token in a header is judged by that alone: a garbage cookie next to it changes nothing,
        // and a good cookie next to a garbage header does not rescue the request.
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateWorkspaceAsync(tokenCookie: "garbage", bearer: MemberToken())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateWorkspaceAsync(tokenCookie: MemberToken(), bearer: "garbage")).StatusCode);
    }

    // --- The two apps' cookies never stand in for each other ----------------------------------

    [Fact]
    public async Task ASiteAdminToken_InTheSiteCookie_IsAuthenticatedOnASiteRoute()
    {
        var response = await _host.GetAsync("/v1/site/agencies?page=0", tokenCookie: SiteToken(), cookieName: ApiTestHost.SiteCookieName);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/v1/site/agencies?page=0")]
    [InlineData("/V1/SITE/agencies?page=0")]
    [InlineData("/v1/site/overview")]
    public async Task TheMemberCookie_IsNotReadOnASiteRoute_EvenWhenItHoldsASiteAdminToken(string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.GetAsync(path, tokenCookie: SiteToken(), cookieName: ApiTestHost.MemberCookieName)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.GetAsync(path, tokenCookie: MemberToken(), cookieName: ApiTestHost.MemberCookieName)).StatusCode);
    }

    [Fact]
    public async Task TheSiteCookie_IsNotReadOnAMemberRoute()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateWorkspaceAsync(tokenCookie: MemberToken(), cookieName: ApiTestHost.SiteCookieName)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateWorkspaceAsync(tokenCookie: SiteToken(), cookieName: ApiTestHost.SiteCookieName)).StatusCode);
    }

    [Fact]
    public async Task WithBothCookies_EachRoutePicksItsOwn()
    {
        var both = new (string Name, string Value)[]
        {
            (ApiTestHost.MemberCookieName, MemberToken()),
            (ApiTestHost.SiteCookieName, SiteToken())
        };

        Assert.Equal(HttpStatusCode.BadRequest, (await _host.GetWithCookiesAsync("/v1/site/agencies?page=0", both)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _host.GetWithCookiesAsync("/v1/menu", both)).StatusCode);
    }

    // --- Signing in with a stale cookie in the browser ---------------------------------------

    [Fact]
    public async Task AGarbageOrExpiredCookie_DoesNotStopAPersonFromSigningInOrOut()
    {
        var expired = Mint(lifetime: TimeSpan.FromMinutes(-10));

        // Public endpoints answer for themselves (400 for an empty form, 204 for logout) whatever cookie the browser still holds.
        Assert.Equal(HttpStatusCode.BadRequest, (await _host.PostAsync("/v1/auth/login", new { }, tokenCookie: expired)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _host.PostAsync("/v1/auth/register", new { }, tokenCookie: "garbage")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _host.PostAsync("/v1/auth/logout", tokenCookie: expired)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _host.PostAsync("/v1/site/login", new { }, tokenCookie: expired, cookieName: ApiTestHost.SiteCookieName)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _host.PostAsync("/v1/site/logout", tokenCookie: expired, cookieName: ApiTestHost.SiteCookieName)).StatusCode);
    }
}
