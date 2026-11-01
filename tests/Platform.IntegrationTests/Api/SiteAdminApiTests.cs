using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.JsonWebTokens;
using Platform.Auth.Services;
using Platform.Helpers.Utilities;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

/// <summary>The site admin console API end to end: sign-in, the token, the dashboard data and who may see it.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class SiteAdminApiTests(PostgresFixture postgres) : AuthApiTestBase(postgres)
{
    private const string AdminPassword = "the site admin password";

    private readonly Pbkdf2PasswordHasher _hasher = new(iterations: 1_000);

    private sealed record SiteSession(string Email, Guid UserId, string AccessToken, string RefreshToken);

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
        var accessToken = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("accessToken").GetString()!;
        return new SiteSession(address, userId, accessToken, ApiTestHost.SiteCookieValue(response)!);
    }

    private Task<HttpResponseMessage> SiteRefreshAsync(string? cookie) =>
        Host.PostAsync("/v1/site/refresh", refreshCookie: cookie, cookieName: ApiTestHost.SiteCookieName);

    private async Task<long> LiveSiteTokensAsync(Guid userId) => await OwnerQueryAsync<long>(
        "SELECT count(*) FROM site_admin_refresh_tokens WHERE user_id = @userId AND revoked_at IS NULL", new { userId });

    // --- Login and the token -----------------------------------------------------------------

    [Fact]
    public async Task Login_Returns200_WithAFifteenMinuteAccessToken_AndNoRefreshTokenInTheBody()
    {
        var (_, email) = await CreateSiteAdminAsync();

        var response = await SiteLoginRawAsync(email);

        AssertStatus(HttpStatusCode.OK, response);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await ApiTestHost.ReadJsonAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.Equal("Bearer", body.GetProperty("tokenType").GetString());
        Assert.Equal(900, body.GetProperty("expiresIn").GetInt32());
        Assert.DoesNotContain(body.EnumerateObject(), p => p.Name.Contains("refresh", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Login_Token_ContainsTheUserId_AndTheSiteAdminRole_ButNoAgency()
    {
        var (userId, email) = await CreateSiteAdminAsync();

        var response = await SiteLoginRawAsync(email);

        var token = new JsonWebToken((await ApiTestHost.ReadJsonAsync(response)).GetProperty("accessToken").GetString()!);
        Assert.Equal("HS256", token.Alg);
        Assert.Equal(TimeSpan.FromMinutes(15), token.ValidTo - token.ValidFrom);
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
        Assert.Null(ApiTestHost.RefreshCookieHeader(response)); // no agency cookie is issued

        var value = ApiTestHost.SiteCookieValue(response)!;
        Assert.Matches("^[A-Za-z0-9_-]{43}$", value);
        Assert.DoesNotContain(value, await response.Content.ReadAsStringAsync());
        var expires = DateTimeOffset.Parse(
            Regex.Match(cookie, "expires=([^;]+)", RegexOptions.IgnoreCase).Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.InRange((expires - DateTimeOffset.UtcNow).TotalDays, 29.9, 30.1);
    }

    [Fact]
    public async Task Login_StoresOnlyTheSha256HashOfTheRefreshToken()
    {
        var session = await SiteLoginAsync();

        var stored = await OwnerQueryAsync<string>(
            "SELECT token_hash FROM site_admin_refresh_tokens WHERE user_id = @userId", new { userId = session.UserId });

        Assert.Equal(HashHelper.Sha256Hex(session.RefreshToken), stored);
        Assert.NotEqual(session.RefreshToken, stored);
    }

    [Fact]
    public async Task Login_ByAnOrdinaryUser_WithTheRightPassword_Returns401_LikeAnyOtherFailure()
    {
        var agencyOwner = await Host.RegisterAsync();

        var response = await SiteLoginRawAsync(agencyOwner.Email, ApiTestHost.DefaultPassword);

        AssertStatus(HttpStatusCode.Unauthorized, response);
        Assert.Equal("invalid_credentials", await CodeOfAsync(response));
        Assert.Null(ApiTestHost.SiteCookieHeader(response));
        Assert.Equal(0, await OwnerQueryAsync<long>("SELECT count(*) FROM site_admin_refresh_tokens"));
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
    public async Task Me_ReturnsTheSiteAdmin()
    {
        var session = await SiteLoginAsync();

        var response = await Host.GetAsync("/v1/site/me", session.AccessToken);

        AssertStatus(HttpStatusCode.OK, response);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var me = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal(session.UserId, me.GetProperty("user").GetProperty("id").GetGuid());
        Assert.Equal(session.Email, me.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal("Root Admin", me.GetProperty("user").GetProperty("fullName").GetString());
        Assert.Equal("siteadmin", me.GetProperty("role").GetString());
        Assert.False(me.GetProperty("user").TryGetProperty("passwordHash", out _));
    }

    [Fact]
    public async Task Overview_CountsTheWholePlatform()
    {
        await Host.RegisterAsync(agencyName: "Alpha");
        var bravo = await Host.RegisterAsync(agencyName: "Bravo");
        await Host.PostAsync("/v1/workspaces", new { name = "Bravo client" }, bravo.AccessToken);
        await OwnerExecuteAsync("UPDATE agencies SET plan = 'paid' WHERE id = @id", new { id = bravo.AgencyId });
        var session = await SiteLoginAsync();

        var response = await Host.GetAsync("/v1/site/overview", session.AccessToken);

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

        var response = await Host.GetAsync("/v1/site/agencies", session.AccessToken);

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

        var first = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/site/agencies?page=1&pageSize=2", session.AccessToken));
        var second = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/site/agencies?page=2&pageSize=2", session.AccessToken));
        var beyond = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/site/agencies?page=9&pageSize=2", session.AccessToken));

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

        AssertStatus(HttpStatusCode.Forbidden, await Host.GetAsync(path, owner.AccessToken));
    }

    [Fact]
    public async Task ASiteAdmin_CannotUseTheAgencyEndpoints_WithTheirSiteToken()
    {
        var session = await SiteLoginAsync();

        AssertStatus(HttpStatusCode.Forbidden, await Host.GetAsync("/v1/workspaces", session.AccessToken));
        AssertStatus(HttpStatusCode.Forbidden, await Host.GetAsync("/v1/auth/me", session.AccessToken));
        AssertStatus(HttpStatusCode.Forbidden, await Host.PostAsync("/v1/workspaces", new { name = "x" }, session.AccessToken));
    }

    [Fact]
    public async Task ASiteAdminWhoIsAlsoAnAgencyOwner_UsesTwoSeparateSessions()
    {
        var owner = await Host.RegisterAsync();
        await OwnerExecuteAsync("INSERT INTO site_admins (user_id) VALUES (@id)", new { id = owner.UserId });

        var agency = await Host.LoginAsync(owner.Email);
        var site = await SiteLoginAsync(owner.Email, ApiTestHost.DefaultPassword);

        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/workspaces", agency.AccessToken));
        AssertStatus(HttpStatusCode.Forbidden, await Host.GetAsync("/v1/site/overview", agency.AccessToken));
        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/site/overview", site.AccessToken));
        AssertStatus(HttpStatusCode.Forbidden, await Host.GetAsync("/v1/workspaces", site.AccessToken));
    }

    [Fact]
    public async Task ASiteAdminsRefreshToken_IsNotAnAgencyRefreshToken_AndTheOtherWayAround()
    {
        var owner = await Host.RegisterAsync();
        var site = await SiteLoginAsync();

        // Each cookie is looked up only in its own table, so one kind of token never opens the other's session.
        var siteTokenAsAgency = await Host.PostAsync("/v1/auth/refresh", refreshCookie: site.RefreshToken);
        var agencyTokenAsSite = await Host.PostAsync(
            "/v1/site/refresh", refreshCookie: owner.RefreshToken, cookieName: ApiTestHost.SiteCookieName);

        AssertStatus(HttpStatusCode.Unauthorized, siteTokenAsAgency);
        AssertStatus(HttpStatusCode.Unauthorized, agencyTokenAsSite);
    }

    // --- Losing the role ---------------------------------------------------------------------

    [Fact]
    public async Task AfterTheUserIsNoLongerASiteAdmin_TheDatabaseRefusesTheDataAtOnce_EvenWithAValidToken()
    {
        var session = await SiteLoginAsync();
        await OwnerExecuteAsync("DELETE FROM site_admins WHERE user_id = @userId", new { userId = session.UserId });

        // The token still says siteadmin for up to 15 minutes, so the policy and the service let the call
        // through; it is the database function that says no.
        var overview = await Host.GetAsync("/v1/site/overview", session.AccessToken);
        var agencies = await Host.GetAsync("/v1/site/agencies", session.AccessToken);
        var me = await Host.GetAsync("/v1/site/me", session.AccessToken);

        AssertStatus(HttpStatusCode.Forbidden, overview);
        Assert.Equal("not_site_admin", await CodeOfAsync(overview));
        AssertStatus(HttpStatusCode.Forbidden, agencies);
        AssertStatus(HttpStatusCode.Unauthorized, me);
        Assert.Equal("not_site_admin", await CodeOfAsync(me));
    }

    [Fact]
    public async Task AfterTheUserIsNoLongerASiteAdmin_TheyCannotRefresh_AndTheirTokensAreRevoked()
    {
        var session = await SiteLoginAsync();
        await OwnerExecuteAsync("DELETE FROM site_admins WHERE user_id = @userId", new { userId = session.UserId });

        var response = await SiteRefreshAsync(session.RefreshToken);

        AssertStatus(HttpStatusCode.Unauthorized, response);
        Assert.Equal("invalid_refresh_token", await CodeOfAsync(response));
        Assert.Equal(0, await LiveSiteTokensAsync(session.UserId));
    }

    [Fact]
    public async Task AfterTheUserIsNoLongerASiteAdmin_TheyCannotLogInAgain()
    {
        var (userId, email) = await CreateSiteAdminAsync();
        await OwnerExecuteAsync("DELETE FROM site_admins WHERE user_id = @userId", new { userId });

        AssertStatus(HttpStatusCode.Unauthorized, await SiteLoginRawAsync(email));
    }

    // --- Refresh -----------------------------------------------------------------------------

    [Fact]
    public async Task Refresh_ReturnsAWorkingSiteAdminToken_AndANewCookie()
    {
        var session = await SiteLoginAsync();

        var response = await SiteRefreshAsync(session.RefreshToken);

        AssertStatus(HttpStatusCode.OK, response);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var newCookie = ApiTestHost.SiteCookieValue(response);
        Assert.NotNull(newCookie);
        Assert.NotEqual(session.RefreshToken, newCookie);
        Assert.Contains("path=/v1/site", ApiTestHost.SiteCookieHeader(response)!, StringComparison.OrdinalIgnoreCase);

        var body = await ApiTestHost.ReadJsonAsync(response);
        Assert.DoesNotContain(body.EnumerateObject(), p => p.Name.Contains("refresh", StringComparison.OrdinalIgnoreCase));
        var token = new JsonWebToken(body.GetProperty("accessToken").GetString()!);
        Assert.Equal(session.UserId.ToString(), token.GetClaim("user_id").Value);
        Assert.Equal("siteadmin", token.GetClaim("role").Value);
        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/site/overview", token.EncodedToken));
        Assert.Equal(1, await LiveSiteTokensAsync(session.UserId));
    }

    [Fact]
    public async Task Refresh_ReplayOfARotatedToken_IsRejected_AndEndsTheWholeSession()
    {
        var session = await SiteLoginAsync();
        var rotated = await SiteRefreshAsync(session.RefreshToken);
        var newCookie = ApiTestHost.SiteCookieValue(rotated)!;

        var replay = await SiteRefreshAsync(session.RefreshToken);

        AssertStatus(HttpStatusCode.Unauthorized, replay);
        Assert.Equal("invalid_refresh_token", await CodeOfAsync(replay));
        AssertStatus(HttpStatusCode.Unauthorized, await SiteRefreshAsync(newCookie));
        Assert.Equal(0, await LiveSiteTokensAsync(session.UserId));
    }

    [Fact]
    public async Task Refresh_WhenManyRequestsPresentTheSameCookie_AtMostOneSucceeds()
    {
        var session = await SiteLoginAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => SiteRefreshAsync(session.RefreshToken))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
        Assert.Equal(0, await LiveSiteTokensAsync(session.UserId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public async Task Refresh_WithoutAValidCookie_Returns401(string? cookie)
    {
        var response = await SiteRefreshAsync(cookie);

        AssertStatus(HttpStatusCode.Unauthorized, response);
        Assert.Equal("invalid_refresh_token", await CodeOfAsync(response));
        Assert.Null(ApiTestHost.SiteCookieHeader(response));
    }

    [Fact]
    public async Task Refresh_OfAnExpiredToken_Returns401_AndIssuesNothing()
    {
        var session = await SiteLoginAsync();
        await OwnerExecuteAsync(
            "UPDATE site_admin_refresh_tokens SET expires_at = now() - interval '1 minute' WHERE user_id = @userId",
            new { userId = session.UserId });

        AssertStatus(HttpStatusCode.Unauthorized, await SiteRefreshAsync(session.RefreshToken));
        Assert.Equal(1, await OwnerQueryAsync<long>("SELECT count(*) FROM site_admin_refresh_tokens"));
    }

    [Fact]
    public async Task Refresh_DoesNotNeedAnAccessToken_AndIgnoresAnExpiredOne()
    {
        var session = await SiteLoginAsync();

        var response = await Host.PostAsync(
            "/v1/site/refresh", accessToken: "an-expired-access-token", refreshCookie: session.RefreshToken, cookieName: ApiTestHost.SiteCookieName);

        AssertStatus(HttpStatusCode.OK, response);
    }

    // --- Logout ------------------------------------------------------------------------------

    [Fact]
    public async Task Logout_Returns204_ExpiresTheSiteCookie_AndEndsTheSession()
    {
        var session = await SiteLoginAsync();

        var response = await Host.PostAsync(
            "/v1/site/logout", refreshCookie: session.RefreshToken, cookieName: ApiTestHost.SiteCookieName);

        AssertStatus(HttpStatusCode.NoContent, response);
        var cookie = ApiTestHost.SiteCookieHeader(response)!;
        Assert.Contains("expires=Thu, 01 Jan 1970", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/v1/site", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await LiveSiteTokensAsync(session.UserId));
        AssertStatus(HttpStatusCode.Unauthorized, await SiteRefreshAsync(session.RefreshToken));
    }

    [Fact]
    public async Task Logout_EndsOnlyTheSessionItWasGiven()
    {
        var (_, email) = await CreateSiteAdminAsync();
        var first = await SiteLoginAsync(email);
        var second = await SiteLoginAsync(email);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);

        await Host.PostAsync("/v1/site/logout", refreshCookie: first.RefreshToken, cookieName: ApiTestHost.SiteCookieName);

        AssertStatus(HttpStatusCode.OK, await SiteRefreshAsync(second.RefreshToken));
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
        var accessToken = (await ApiTestHost.ReadJsonAsync(login)).GetProperty("accessToken").GetString()!;
        var overview = await host.GetAsync("/v1/site/overview", accessToken);
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
