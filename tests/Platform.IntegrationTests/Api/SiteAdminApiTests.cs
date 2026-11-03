using System.Net;
using Microsoft.IdentityModel.JsonWebTokens;
using Platform.Auth.Services;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

/// <summary>The site admin console API end to end: sign-in, the token, the dashboard data and who may see it.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class SiteAdminApiTests(PostgresFixture postgres) : AuthApiTestBase(postgres)
{
    private const string AdminPassword = "the site admin password";

    private readonly Pbkdf2PasswordHasher _hasher = new(iterations: 1_000);

    private sealed record SiteSession(string Email, Guid UserId, string AccessToken);

    private async Task<(Guid UserId, string Email)> CreateSiteAdminAsync(string? email = null)
    {
        email ??= $"root-{Guid.NewGuid():N}@example.com";
        var userId = Guid.NewGuid();
        await OwnerExecuteAsync(
            "INSERT INTO users (id, email, password_hash, full_name) VALUES (@userId, @email, @hash, 'Root Admin'); " +
            "INSERT INTO site_admins (user_id) VALUES (@userId)",
            new { userId, email, hash = _hasher.Hash(AdminPassword) });
        return (userId, email);
    }

    private Task<HttpResponseMessage> SiteLoginRawAsync(string email, string password = AdminPassword, ApiTestHost? host = null) =>
        (host ?? Host).PostAsync("/v1/site/login", new { email, password });

    /// <summary>Signs in as a brand-new site admin, or as the given existing account with the given password.</summary>
    private async Task<SiteSession> SiteLoginAsync(string? email = null, string password = AdminPassword)
    {
        var (userId, address) = email is null ? await CreateSiteAdminAsync() : (Guid.Empty, email);
        var response = await SiteLoginRawAsync(address, password);
        response.EnsureSuccessStatusCode();
        return new SiteSession(address, userId, ApiTestHost.SiteCookieValue(response)!);
    }

    /// <summary>A GET as the browser makes it: the Site admin cookie, which is the one a /v1/site route reads.</summary>
    private Task<HttpResponseMessage> SiteGetAsync(string path, SiteSession session) =>
        Host.GetAsync(path, tokenCookie: session.AccessToken, cookieName: ApiTestHost.SiteCookieName);

    // --- Login and the token -----------------------------------------------------------------

    [Fact]
    public async Task Login_Returns200_AndTheBodyOnlySaysWhenTheSignInEnds_NeverTheToken()
    {
        var (_, email) = await CreateSiteAdminAsync();

        var response = await SiteLoginRawAsync(email);

        AssertStatus(HttpStatusCode.OK, response);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal(["expiresAt"], body.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain(ApiTestHost.SiteCookieValue(response)!, body.ToString());
    }

    [Fact]
    public async Task Login_Token_ContainsTheUserId_AndTheSiteAdminRole_ButNoAgency_ForThirtyDays()
    {
        var (userId, email) = await CreateSiteAdminAsync();

        var response = await SiteLoginRawAsync(email);

        var token = new JsonWebToken(ApiTestHost.SiteCookieValue(response)!);
        Assert.Equal("HS256", token.Alg);
        Assert.Equal(TimeSpan.FromDays(30), token.ValidTo - token.ValidFrom);
        Assert.Equal(userId.ToString(), token.GetClaim("user_id").Value);
        Assert.Equal("siteadmin", token.GetClaim("role").Value);
        Assert.False(token.TryGetClaim("agency_id", out _));
    }

    [Fact]
    public async Task Login_SetsTheSiteCookie_HttpOnly_Secure_SameSiteStrict_ScopedToTheSitePath()
    {
        var (_, email) = await CreateSiteAdminAsync();

        var response = await SiteLoginRawAsync(email);

        var cookie = ApiTestHost.SiteCookieHeader(response);
        Assert.NotNull(cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/v1/site", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Null(ApiTestHost.MemberCookieHeader(response)); // no member cookie is issued

        var value = ApiTestHost.SiteCookieValue(response)!;
        Assert.Equal(3, value.Split('.').Length); // header.payload.signature
        Assert.DoesNotContain(value, await response.Content.ReadAsStringAsync());
        var expires = ApiTestHost.CookieExpires(response, ApiTestHost.SiteCookieName)!.Value;
        Assert.InRange((expires - DateTimeOffset.UtcNow).TotalDays, 29.9, 30.1);
    }

    [Fact]
    public async Task Login_ByAnOrdinaryUser_WithTheRightPassword_Returns401_LikeAnyOtherFailure()
    {
        var agencyOwner = await Host.RegisterAsync();

        var response = await SiteLoginRawAsync(agencyOwner.Email, ApiTestHost.DefaultPassword);

        AssertStatus(HttpStatusCode.Unauthorized, response);
        Assert.Equal("invalid_credentials", await CodeOfAsync(response));
        Assert.Null(ApiTestHost.SiteCookieHeader(response));
    }

    [Fact]
    public async Task Login_FailsIdentically_ForAnOrdinaryUser_AWrongPassword_AndAnUnknownEmail()
    {
        var agencyOwner = await Host.RegisterAsync();
        var (_, adminEmail) = await CreateSiteAdminAsync();

        var ordinary = await SiteLoginRawAsync(agencyOwner.Email, ApiTestHost.DefaultPassword);
        var wrongPassword = await SiteLoginRawAsync(adminEmail, "definitely not the password");
        var unknown = await SiteLoginRawAsync("nobody@example.com");

        foreach (var response in new[] { ordinary, wrongPassword, unknown })
        {
            AssertStatus(HttpStatusCode.Unauthorized, response);
        }

        var bodies = new[] { ordinary, wrongPassword, unknown }.Select(r => ApiTestHost.ReadJsonAsync(r).GetAwaiter().GetResult()).ToList();
        Assert.Single(bodies.Select(b => b.GetProperty("detail").GetString()).Distinct());
        Assert.Single(bodies.Select(b => b.GetProperty("code").GetString()).Distinct());
        Assert.Single(bodies.Select(b => b.GetProperty("title").GetString()).Distinct());
    }

    [Fact]
    public async Task Login_WithInvalidInput_Returns400()
    {
        var response = await Host.PostAsync("/v1/site/login", new { email = "nope", password = "" });

        AssertStatus(HttpStatusCode.BadRequest, response);
    }

    // --- The dashboard data ------------------------------------------------------------------

    [Fact]
    public async Task Me_ReturnsTheSiteAdmin_AndWhenTheSignInEnds()
    {
        var session = await SiteLoginAsync();

        var response = await SiteGetAsync("/v1/site/me", session);

        AssertStatus(HttpStatusCode.OK, response);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var me = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal(session.UserId, me.GetProperty("user").GetProperty("id").GetGuid());
        Assert.Equal(session.Email, me.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal("Root Admin", me.GetProperty("user").GetProperty("fullName").GetString());
        Assert.Equal("siteadmin", me.GetProperty("role").GetString());
        Assert.False(me.GetProperty("user").TryGetProperty("passwordHash", out _));
        Assert.InRange(
            me.GetProperty("sessionExpiresAt").GetDateTimeOffset(),
            DateTimeOffset.UtcNow.AddDays(30).AddMinutes(-2),
            DateTimeOffset.UtcNow.AddDays(30).AddMinutes(2));
    }

    [Fact]
    public async Task Overview_CountsTheWholePlatform()
    {
        await Host.RegisterAsync(agencyName: "Alpha");
        var bravo = await Host.RegisterAsync(agencyName: "Bravo");
        await Host.PostAsync("/v1/workspaces", new { name = "Bravo client" }, bravo.AccessToken);
        await OwnerExecuteAsync("UPDATE agencies SET plan = 'paid' WHERE id = @id", new { id = bravo.AgencyId });
        var session = await SiteLoginAsync();

        var response = await SiteGetAsync("/v1/site/overview", session);

        AssertStatus(HttpStatusCode.OK, response);
        var overview = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal(2, overview.GetProperty("agencies").GetInt32());
        Assert.Equal(1, overview.GetProperty("trialAgencies").GetInt32());
        Assert.Equal(1, overview.GetProperty("paidAgencies").GetInt32());
        Assert.Equal(3, overview.GetProperty("users").GetInt32());       // two owners and the site admin
        Assert.Equal(3, overview.GetProperty("workspaces").GetInt32());  // one each, and Bravo's second
        Assert.Equal(2, overview.GetProperty("memberships").GetInt32());
    }

    [Fact]
    public async Task Agencies_ListsEveryAgency_NewestFirst_WithTheirCounts()
    {
        await Host.RegisterAsync(agencyName: "Alpha");
        var bravo = await Host.RegisterAsync(agencyName: "Bravo");
        await Host.PostAsync("/v1/workspaces", new { name = "Bravo client" }, bravo.AccessToken);
        var session = await SiteLoginAsync();

        var response = await SiteGetAsync("/v1/site/agencies", session);

        AssertStatus(HttpStatusCode.OK, response);
        var page = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, page.GetProperty("totalPages").GetInt32());
        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["Bravo", "Alpha"], items.Select(i => i.GetProperty("name").GetString()));
        Assert.Equal("trial", items[0].GetProperty("plan").GetString());
        Assert.Equal(1, items[0].GetProperty("members").GetInt32());
        Assert.Equal(2, items[0].GetProperty("workspaces").GetInt32());
        Assert.Equal(1, items[1].GetProperty("workspaces").GetInt32());
        Assert.Equal(bravo.AgencyId, items[0].GetProperty("id").GetGuid());
        Assert.True(items[0].TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task Agencies_Pages()
    {
        foreach (var name in new[] { "Alpha", "Bravo", "Charlie" })
        {
            await Host.RegisterAsync(agencyName: name);
        }

        var session = await SiteLoginAsync();

        var first = await ApiTestHost.ReadJsonAsync(await SiteGetAsync("/v1/site/agencies?page=1&pageSize=2", session));
        var second = await ApiTestHost.ReadJsonAsync(await SiteGetAsync("/v1/site/agencies?page=2&pageSize=2", session));
        var beyond = await ApiTestHost.ReadJsonAsync(await SiteGetAsync("/v1/site/agencies?page=9&pageSize=2", session));

        Assert.Equal(["Charlie", "Bravo"], first.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()));
        Assert.Equal(["Alpha"], second.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()));
        Assert.Equal(2, first.GetProperty("totalPages").GetInt32());
        Assert.Equal(3, second.GetProperty("totalCount").GetInt32());
        Assert.Empty(beyond.GetProperty("items").EnumerateArray());
        Assert.Equal(3, beyond.GetProperty("totalCount").GetInt32());
    }

    // --- Site admin and agency tokens never meet ---------------------------------------------

    [Theory]
    [InlineData("/v1/site/me")]
    [InlineData("/v1/site/overview")]
    [InlineData("/v1/site/agencies")]
    public async Task AnAgencyOwner_CannotUseTheSiteEndpoints(string path)
    {
        var owner = await Host.RegisterAsync();

        // The owner's token as a header is a real token, and it is refused for what it is: not a Site admin's.
        AssertStatus(HttpStatusCode.Forbidden, await Host.GetAsync(path, owner.AccessToken));
        // Sent as a cookie, as the browser would, it is not even read on a Site admin route.
        AssertStatus(HttpStatusCode.Unauthorized, await Host.GetAsync(path, tokenCookie: owner.AccessToken));
    }

    [Fact]
    public async Task ASiteAdmin_CannotUseTheAgencyEndpoints_WithTheirSiteToken()
    {
        var session = await SiteLoginAsync();

        AssertStatus(HttpStatusCode.Forbidden, await Host.GetAsync("/v1/workspaces", session.AccessToken));
        AssertStatus(HttpStatusCode.Forbidden, await Host.GetAsync("/v1/auth/me", session.AccessToken));
        AssertStatus(HttpStatusCode.Forbidden, await Host.PostAsync("/v1/workspaces", new { name = "x" }, session.AccessToken));
        // Their cookie is for /v1/site and is never read anywhere else, even if a client sends it.
        AssertStatus(HttpStatusCode.Unauthorized, await Host.GetAsync("/v1/workspaces", tokenCookie: session.AccessToken, cookieName: ApiTestHost.SiteCookieName));
    }

    [Fact]
    public async Task ASiteAdminWhoIsAlsoAnAgencyOwner_UsesTwoSeparateSessions()
    {
        var owner = await Host.RegisterAsync();
        await OwnerExecuteAsync("INSERT INTO site_admins (user_id) VALUES (@id)", new { id = owner.UserId });

        var agency = await Host.LoginAsync(owner.Email);
        var site = await SiteLoginAsync(owner.Email, ApiTestHost.DefaultPassword);

        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/workspaces", tokenCookie: agency.AccessToken));
        AssertStatus(HttpStatusCode.Forbidden, await Host.GetAsync("/v1/site/overview", agency.AccessToken));
        AssertStatus(HttpStatusCode.OK, await SiteGetAsync("/v1/site/overview", site));
        AssertStatus(HttpStatusCode.Forbidden, await Host.GetAsync("/v1/workspaces", site.AccessToken));
    }

    [Fact]
    public async Task WhenTheBrowserSendsBothCookiesToASiteRoute_OnlyTheSiteOneDecides()
    {
        // The member cookie is scoped to /v1, so a browser sends it to /v1/site as well, next to the Site admin one.
        var owner = await Host.RegisterAsync();
        var site = await SiteLoginAsync();

        var response = await Host.GetWithCookiesAsync(
            "/v1/site/overview", (ApiTestHost.MemberCookieName, owner.AccessToken), (ApiTestHost.SiteCookieName, site.AccessToken));

        AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task WhenTheBrowserSendsBothCookiesToAMemberRoute_OnlyTheMemberOneDecides()
    {
        // The Site admin cookie is not sent outside /v1/site by a browser, but if it were it must not matter.
        var owner = await Host.RegisterAsync();
        var site = await SiteLoginAsync();

        var response = await Host.GetWithCookiesAsync(
            "/v1/workspaces", (ApiTestHost.MemberCookieName, owner.AccessToken), (ApiTestHost.SiteCookieName, site.AccessToken));

        AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task ATokenInAnAuthorizationHeader_WinsOverACookie()
    {
        var owner = await Host.RegisterAsync();
        var site = await SiteLoginAsync();

        // A header names the token that the request is judged by; the cookie next to it is not consulted.
        var response = await Host.GetAsync("/v1/site/overview", owner.AccessToken, tokenCookie: site.AccessToken, cookieName: ApiTestHost.SiteCookieName);

        AssertStatus(HttpStatusCode.Forbidden, response);
    }

    // --- Losing the role ---------------------------------------------------------------------

    [Fact]
    public async Task AfterTheUserIsNoLongerASiteAdmin_TheDatabaseRefusesTheDataAtOnce_EvenWithAValidToken()
    {
        var session = await SiteLoginAsync();
        await OwnerExecuteAsync("DELETE FROM site_admins WHERE user_id = @userId", new { userId = session.UserId });

        // The token still says siteadmin until it expires (30 days), so the policy and the service let the call
        // through; it is the database function that says no.
        var overview = await SiteGetAsync("/v1/site/overview", session);
        var agencies = await SiteGetAsync("/v1/site/agencies", session);
        var me = await SiteGetAsync("/v1/site/me", session);

        AssertStatus(HttpStatusCode.Forbidden, overview);
        Assert.Equal("not_site_admin", await CodeOfAsync(overview));
        AssertStatus(HttpStatusCode.Forbidden, agencies);
        AssertStatus(HttpStatusCode.Unauthorized, me);
        Assert.Equal("not_site_admin", await CodeOfAsync(me));
    }

    [Fact]
    public async Task AfterTheUserIsNoLongerASiteAdmin_TheyCannotLogInAgain()
    {
        var (userId, email) = await CreateSiteAdminAsync();
        await OwnerExecuteAsync("DELETE FROM site_admins WHERE user_id = @userId", new { userId });

        AssertStatus(HttpStatusCode.Unauthorized, await SiteLoginRawAsync(email));
    }

    // --- Logout ------------------------------------------------------------------------------

    [Fact]
    public async Task Logout_Returns204_AndExpiresTheSiteCookieAtTheSamePathItWasSetAt()
    {
        var session = await SiteLoginAsync();

        var response = await Host.PostAsync("/v1/site/logout", tokenCookie: session.AccessToken, cookieName: ApiTestHost.SiteCookieName);

        AssertStatus(HttpStatusCode.NoContent, response);
        var cookie = ApiTestHost.SiteCookieHeader(response)!;
        Assert.Contains("expires=Thu, 01 Jan 1970", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/v1/site", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Null(ApiTestHost.SiteCookieValue(response));
        Assert.Null(ApiTestHost.MemberCookieHeader(response));
    }

    [Fact]
    public async Task Logout_OnlyClearsTheBrowsersCookie_ATokenThatWasCopiedStaysValidUntilItExpires()
    {
        // Nothing is stored for a session, so there is nothing to revoke. This is the price of having no refresh token.
        var session = await SiteLoginAsync();

        await Host.PostAsync("/v1/site/logout", tokenCookie: session.AccessToken, cookieName: ApiTestHost.SiteCookieName);

        AssertStatus(HttpStatusCode.OK, await SiteGetAsync("/v1/site/overview", session));
    }

    // --- The first site admin, created when the app starts -----------------------------------

    [Fact]
    public async Task TheAppCreatesTheFirstSiteAdmin_OnStartup_FromItsSettings_SoTheyCanSignInAtOnce()
    {
        await using var host = await ApiTestHost.StartWithDatabaseAsync(Postgres, (settings, database) =>
        {
            settings["Database:MigrateOnStartup"] = "true";
            settings["ConnectionStrings:Migrator"] = database.OwnerConnectionString;
            settings["SiteAdmin:Email"] = "First.Admin@Example.com";
            settings["SiteAdmin:Password"] = "the very first password";
        });

        var login = await host.PostAsync("/v1/site/login", new { email = "first.admin@example.com", password = "the very first password" });

        AssertStatus(HttpStatusCode.OK, login);
        var accessToken = ApiTestHost.SiteCookieValue(login)!;
        var overview = await host.GetAsync("/v1/site/overview", tokenCookie: accessToken, cookieName: ApiTestHost.SiteCookieName);
        AssertStatus(HttpStatusCode.OK, overview);
        Assert.Equal(0, (await ApiTestHost.ReadJsonAsync(overview)).GetProperty("agencies").GetInt32());
    }

    [Fact]
    public async Task WithNoSiteAdminConfigured_NobodyCanSignInAsOne()
    {
        // The host this test class starts has no SiteAdmin settings: the default is no site admin at all.
        Assert.Equal(0, await OwnerQueryAsync<long>("SELECT count(*) FROM site_admins"));
        AssertStatus(HttpStatusCode.Unauthorized, await SiteLoginRawAsync("anyone@example.com", "any password at all"));
    }
}
