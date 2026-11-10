using System.Net;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Database")]
public sealed class LoginAndSessionApiTests(PostgresFixture postgres) : AuthApiTestBase(postgres)
{
    private Task<HttpResponseMessage> LoginRawAsync(string email, string password = ApiTestHost.DefaultPassword, Guid? agencyId = null) =>
        Host.PostAsync("/v1/auth/login", new { email, password, agencyId });

    // --- Login -------------------------------------------------------------------------------

    [Fact]
    public async Task Login_WithTheRightPassword_SetsTheTokenCookie_AndTheBodyOnlySaysWhenItEnds()
    {
        var registered = await Host.RegisterAsync();

        var response = await LoginRawAsync(registered.Email);

        AssertStatus(HttpStatusCode.OK, response);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await ApiTestHost.ReadJsonAsync(response);
        // Scripts on the page must never be able to read the token: it is in the cookie and nowhere in the body.
        Assert.Equal(["expiresAt"], body.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain(registered.AccessToken, body.ToString());
        Assert.NotEqual(registered.AccessToken, ApiTestHost.MemberCookieValue(response));
        var cookie = ApiTestHost.MemberCookieHeader(response)!;
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/v1;", cookie + ";", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_TheCookieAndTheBodyEndTogether_ThirtyDaysFromNow()
    {
        var registered = await Host.RegisterAsync();

        var response = await LoginRawAsync(registered.Email);

        var expires = ApiTestHost.CookieExpires(response, ApiTestHost.MemberCookieName)!.Value;
        Assert.InRange(expires, DateTimeOffset.UtcNow.AddDays(30).AddMinutes(-2), DateTimeOffset.UtcNow.AddDays(30).AddMinutes(2));
        var endsAt = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange((endsAt - expires).TotalSeconds, -1, 1);
    }

    [Fact]
    public async Task Login_ReturnsATokenThatWorksOnProtectedEndpoints_AsACookieOrAsABearerHeader()
    {
        var registered = await Host.RegisterAsync();

        var session = await Host.LoginAsync(registered.Email);

        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/workspaces", tokenCookie: session.AccessToken));
        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/workspaces", session.AccessToken));
        Assert.Equal(registered.AgencyId, session.AgencyId);
    }

    [Fact]
    public async Task Login_WithTheWrongPassword_Returns401_AndNoCookie()
    {
        var registered = await Host.RegisterAsync();

        var response = await LoginRawAsync(registered.Email, "definitely not the password");

        AssertStatus(HttpStatusCode.Unauthorized, response);
        Assert.Equal("invalid_credentials", await CodeOfAsync(response));
        Assert.Null(ApiTestHost.MemberCookieHeader(response));
    }

    [Fact]
    public async Task Login_WithAnUnknownEmail_LooksExactlyLikeAWrongPassword()
    {
        var registered = await Host.RegisterAsync();

        var unknown = await LoginRawAsync("nobody@example.com");
        var wrong = await LoginRawAsync(registered.Email, "definitely not the password");

        AssertStatus(HttpStatusCode.Unauthorized, unknown);
        Assert.Equal(wrong.StatusCode, unknown.StatusCode);
        var unknownBody = await ApiTestHost.ReadJsonAsync(unknown);
        var wrongBody = await ApiTestHost.ReadJsonAsync(wrong);
        Assert.Equal(wrongBody.GetProperty("detail").GetString(), unknownBody.GetProperty("detail").GetString());
        Assert.Equal(wrongBody.GetProperty("code").GetString(), unknownBody.GetProperty("code").GetString());
        Assert.Equal(wrongBody.GetProperty("title").GetString(), unknownBody.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Login_WithoutAnAgencyId_UsesTheMostRecentlyUsedMembership()
    {
        var registered = await Host.RegisterAsync(agencyName: "First agency");
        var second = await AddMembershipAsync(registered, "clientmanager", "Second agency", DateTimeOffset.UtcNow.AddDays(1));

        var session = await Host.LoginAsync(registered.Email);

        Assert.Equal(second, session.AgencyId);
        var me = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/auth/me", tokenCookie: session.AccessToken));
        Assert.Equal("clientmanager", me.GetProperty("role").GetString());
        Assert.Equal("Second agency", me.GetProperty("agency").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Login_WithAnAgencyId_SignsInToThatAgency_AndRemembersTheChoice()
    {
        var registered = await Host.RegisterAsync(agencyName: "First agency");
        var second = await AddMembershipAsync(registered, "clientmanager", "Second agency", DateTimeOffset.UtcNow.AddDays(-1));
        await OwnerExecuteAsync(
            "UPDATE agency_members SET last_active_at = now() - interval '2 days' WHERE user_id = @userId AND agency_id = @agencyId",
            new { userId = registered.UserId, agencyId = registered.AgencyId });
        Assert.Equal(second, (await Host.LoginAsync(registered.Email)).AgencyId); // the second agency is the more recent one

        var chosen = await Host.LoginAsync(registered.Email, agencyId: registered.AgencyId);
        Assert.Equal(registered.AgencyId, chosen.AgencyId);

        // The choice counts as use: a plain login now lands in the same agency.
        var next = await Host.LoginAsync(registered.Email);
        Assert.Equal(registered.AgencyId, next.AgencyId);
    }

    [Fact]
    public async Task Login_WithAnAgencyTheUserDoesNotBelongTo_Returns403_AndNoCookie()
    {
        var ana = await Host.RegisterAsync();
        var bob = await Host.RegisterAsync();

        var response = await LoginRawAsync(ana.Email, agencyId: bob.AgencyId);

        AssertStatus(HttpStatusCode.Forbidden, response);
        Assert.Equal("not_a_member", await CodeOfAsync(response));
        Assert.Null(ApiTestHost.MemberCookieHeader(response));
    }

    [Fact]
    public async Task Login_ForAUserWhoNoLongerBelongsToAnyAgency_Returns403()
    {
        var registered = await Host.RegisterAsync();
        await OwnerExecuteAsync("DELETE FROM agency_members WHERE user_id = @userId", new { userId = registered.UserId });

        var response = await LoginRawAsync(registered.Email);

        AssertStatus(HttpStatusCode.Forbidden, response);
        Assert.Equal("no_agency", await CodeOfAsync(response));
    }

    // --- Me ----------------------------------------------------------------------------------

    [Fact]
    public async Task Me_ReturnsTheUserAgencyRoleAndEveryMembership()
    {
        var registered = await Host.RegisterAsync(agencyName: "First agency");
        var second = await AddMembershipAsync(registered, "marketingmanager", "Second agency");

        var response = await Host.GetAsync("/v1/auth/me", tokenCookie: registered.AccessToken);

        AssertStatus(HttpStatusCode.OK, response);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var me = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal(registered.UserId, me.GetProperty("user").GetProperty("id").GetGuid());
        Assert.Equal(registered.Email, me.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal("Test User", me.GetProperty("user").GetProperty("fullName").GetString());
        Assert.False(me.GetProperty("user").TryGetProperty("passwordHash", out _));
        Assert.Equal(registered.AgencyId, me.GetProperty("agency").GetProperty("id").GetGuid());
        Assert.Equal("organizationadmin", me.GetProperty("role").GetString());

        var memberships = me.GetProperty("memberships").EnumerateArray()
            .ToDictionary(m => m.GetProperty("agencyId").GetGuid(), m => (m.GetProperty("agencyName").GetString(), m.GetProperty("role").GetString()));
        Assert.Equal(2, memberships.Count);
        Assert.Equal(("First agency", "organizationadmin"), memberships[registered.AgencyId]);
        Assert.Equal(("Second agency", "marketingmanager"), memberships[second]);
    }

    [Fact]
    public async Task Me_SaysWhenTheSignInEnds_WhichIsTheTokensOwnExpiry()
    {
        var login = await LoginRawAsync((await Host.RegisterAsync()).Email);
        var token = ApiTestHost.MemberCookieValue(login)!;

        var me = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/auth/me", tokenCookie: token));

        var endsAt = me.GetProperty("sessionExpiresAt").GetDateTimeOffset();
        Assert.InRange(endsAt, DateTimeOffset.UtcNow.AddDays(30).AddMinutes(-2), DateTimeOffset.UtcNow.AddDays(30).AddMinutes(2));
        var cookieEnds = ApiTestHost.CookieExpires(login, ApiTestHost.MemberCookieName)!.Value;
        Assert.InRange((endsAt - cookieEnds).TotalSeconds, -1, 1);
    }

    [Fact]
    public async Task Me_NeverShowsAnotherUsersMemberships()
    {
        var ana = await Host.RegisterAsync();
        var bob = await Host.RegisterAsync(agencyName: "Bob only");
        await AddMembershipAsync(bob, "clientmanager", "Bob's second");

        var me = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/auth/me", tokenCookie: ana.AccessToken));

        Assert.Single(me.GetProperty("memberships").EnumerateArray());
        Assert.DoesNotContain("Bob", me.ToString());
    }

    [Fact]
    public async Task Me_TakesTheRoleFromTheDatabase_NotFromTheToken()
    {
        var registered = await Host.RegisterAsync();
        await OwnerExecuteAsync(
            "UPDATE agency_members SET role = 'clientmanager' WHERE user_id = @userId", new { userId = registered.UserId });

        var me = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/auth/me", tokenCookie: registered.AccessToken));

        Assert.Equal("clientmanager", me.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Me_AfterTheUserLeftTheAgency_Returns401()
    {
        var registered = await Host.RegisterAsync();
        await OwnerExecuteAsync("DELETE FROM agency_members WHERE user_id = @userId", new { userId = registered.UserId });

        var response = await Host.GetAsync("/v1/auth/me", tokenCookie: registered.AccessToken);

        AssertStatus(HttpStatusCode.Unauthorized, response);
        Assert.Equal("membership_ended", await CodeOfAsync(response));
    }

    // --- Logout ------------------------------------------------------------------------------

    [Fact]
    public async Task Logout_Returns204_AndExpiresTheCookieAtTheSamePathItWasSetAt()
    {
        var registered = await Host.RegisterAsync();

        var response = await Host.PostAsync("/v1/auth/logout", tokenCookie: registered.AccessToken);

        AssertStatus(HttpStatusCode.NoContent, response);
        var cookie = ApiTestHost.MemberCookieHeader(response)!;
        Assert.Contains("expires=Thu, 01 Jan 1970", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/v1;", cookie + ";", StringComparison.OrdinalIgnoreCase);
        Assert.Null(ApiTestHost.MemberCookieValue(response));
    }

    [Fact]
    public async Task Logout_OnlyClearsTheBrowsersCookie_ATokenThatWasCopiedStaysValidUntilItExpires()
    {
        // Nothing is stored for a session, so there is nothing to revoke. This test records that on purpose: it is the
        // price of having no refresh token, and a member-removal feature will need its own way to end sessions.
        var registered = await Host.RegisterAsync();

        await Host.PostAsync("/v1/auth/logout", tokenCookie: registered.AccessToken);

        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/auth/me", tokenCookie: registered.AccessToken));
    }

    [Fact]
    public async Task Logout_WithNoSessionAtAll_StillSucceeds()
    {
        AssertStatus(HttpStatusCode.NoContent, await Host.PostAsync("/v1/auth/logout"));
    }

    [Fact]
    public async Task Logout_WithAnUnknownCookie_StillSucceeds()
    {
        var response = await Host.PostAsync("/v1/auth/logout", tokenCookie: "not-a-token-we-issued");

        AssertStatus(HttpStatusCode.NoContent, response);
    }

    // --- Switch agency -----------------------------------------------------------------------

    [Fact]
    public async Task SwitchAgency_SetsANewCookieForTheOtherAgency_WithThatRole()
    {
        var registered = await Host.RegisterAsync(agencyName: "First agency");
        var second = await AddMembershipAsync(registered, "clientmanager", "Second agency");

        var response = await Host.PostAsync("/v1/auth/switch-agency", new { agencyId = second }, tokenCookie: registered.AccessToken);

        AssertStatus(HttpStatusCode.OK, response);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(["expiresAt"], (await ApiTestHost.ReadJsonAsync(response)).EnumerateObject().Select(p => p.Name));
        var newToken = ApiTestHost.MemberCookieValue(response)!;
        Assert.NotEqual(registered.AccessToken, newToken);
        var me = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/auth/me", tokenCookie: newToken));
        Assert.Equal(second, me.GetProperty("agency").GetProperty("id").GetGuid());
        Assert.Equal("clientmanager", me.GetProperty("role").GetString());
    }

    [Fact]
    public async Task SwitchAgency_TheNewAgencyBecomesTheDefaultForTheNextLogin()
    {
        var registered = await Host.RegisterAsync();
        var second = await AddMembershipAsync(registered, "marketingmanager", lastActiveAt: DateTimeOffset.UtcNow.AddDays(-30));

        await Host.PostAsync("/v1/auth/switch-agency", new { agencyId = second }, tokenCookie: registered.AccessToken);

        Assert.Equal(second, (await Host.LoginAsync(registered.Email)).AgencyId);
    }

    [Fact]
    public async Task SwitchAgency_ToAnAgencyTheUserIsNotIn_Returns403_AndSetsNoCookie()
    {
        var ana = await Host.RegisterAsync();
        var bob = await Host.RegisterAsync();

        var response = await Host.PostAsync("/v1/auth/switch-agency", new { agencyId = bob.AgencyId }, tokenCookie: ana.AccessToken);

        AssertStatus(HttpStatusCode.Forbidden, response);
        Assert.Equal("not_a_member", await CodeOfAsync(response));
        Assert.Null(ApiTestHost.MemberCookieHeader(response));
    }

    [Fact]
    public async Task SwitchAgency_WithAnUnknownAgency_Returns403()
    {
        var registered = await Host.RegisterAsync();

        var response = await Host.PostAsync("/v1/auth/switch-agency", new { agencyId = Guid.NewGuid() }, registered.AccessToken);

        AssertStatus(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task SwitchAgency_WithoutAnAgencyId_Returns400()
    {
        var registered = await Host.RegisterAsync();

        var response = await Host.PostAsync("/v1/auth/switch-agency", new { }, registered.AccessToken);

        AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.True((await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("agencyId", out _));
    }

    [Fact]
    public async Task SwitchAgency_WithoutASession_Returns401()
    {
        var response = await Host.PostAsync("/v1/auth/switch-agency", new { agencyId = Guid.NewGuid() });

        AssertStatus(HttpStatusCode.Unauthorized, response);
    }
}
