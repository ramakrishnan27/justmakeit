using System.Net;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class LoginAndSessionApiTests(PostgresFixture postgres) : AuthApiTestBase(postgres)
{
    private Task<HttpResponseMessage> LoginRawAsync(string email, string password = ApiTestHost.DefaultPassword, Guid? agencyId = null) =>
        Host.PostAsync("/v1/auth/login", new { email, password, agencyId });

    // --- Login -------------------------------------------------------------------------------

    [Fact]
    public async Task Login_WithTheRightPassword_ReturnsATokenAndANewRefreshCookie()
    {
        var registered = await Host.RegisterAsync();

        var response = await LoginRawAsync(registered.Email);

        AssertStatus(HttpStatusCode.OK, response);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal("Bearer", body.GetProperty("tokenType").GetString());
        Assert.Equal(900, body.GetProperty("expiresIn").GetInt32());
        Assert.DoesNotContain(body.EnumerateObject(), p => p.Name.Contains("refresh", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(registered.RefreshToken, ApiTestHost.RefreshCookieValue(response));
        var cookie = ApiTestHost.RefreshCookieHeader(response)!;
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_ReturnsATokenThatWorksOnProtectedEndpoints()
    {
        var registered = await Host.RegisterAsync();

        var session = await Host.LoginAsync(registered.Email);

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
        Assert.Null(ApiTestHost.RefreshCookieHeader(response));
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
        var second = await AddMembershipAsync(registered, "viewer", "Second agency", DateTimeOffset.UtcNow.AddDays(1));

        var session = await Host.LoginAsync(registered.Email);

        Assert.Equal(second, session.AgencyId);
        var me = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/auth/me", session.AccessToken));
        Assert.Equal("viewer", me.GetProperty("role").GetString());
        Assert.Equal("Second agency", me.GetProperty("agency").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Login_WithAnAgencyId_SignsInToThatAgency_AndRemembersTheChoice()
    {
        var registered = await Host.RegisterAsync(agencyName: "First agency");
        var second = await AddMembershipAsync(registered, "viewer", "Second agency", DateTimeOffset.UtcNow.AddDays(-1));
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
        Assert.Null(ApiTestHost.RefreshCookieHeader(response));
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
        var second = await AddMembershipAsync(registered, "manager", "Second agency");

        var response = await Host.GetAsync("/v1/auth/me", registered.AccessToken);

        AssertStatus(HttpStatusCode.OK, response);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var me = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal(registered.UserId, me.GetProperty("user").GetProperty("id").GetGuid());
        Assert.Equal(registered.Email, me.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal("Test User", me.GetProperty("user").GetProperty("fullName").GetString());
        Assert.False(me.GetProperty("user").TryGetProperty("passwordHash", out _));
        Assert.Equal(registered.AgencyId, me.GetProperty("agency").GetProperty("id").GetGuid());
        Assert.Equal("owner", me.GetProperty("role").GetString());

        var memberships = me.GetProperty("memberships").EnumerateArray()
            .ToDictionary(m => m.GetProperty("agencyId").GetGuid(), m => (m.GetProperty("agencyName").GetString(), m.GetProperty("role").GetString()));
        Assert.Equal(2, memberships.Count);
        Assert.Equal(("First agency", "owner"), memberships[registered.AgencyId]);
        Assert.Equal(("Second agency", "manager"), memberships[second]);
    }

    [Fact]
    public async Task Me_NeverShowsAnotherUsersMemberships()
    {
        var ana = await Host.RegisterAsync();
        var bob = await Host.RegisterAsync(agencyName: "Bob only");
        await AddMembershipAsync(bob, "viewer", "Bob's second");

        var me = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/auth/me", ana.AccessToken));

        Assert.Single(me.GetProperty("memberships").EnumerateArray());
        Assert.DoesNotContain("Bob", me.ToString());
    }

    [Fact]
    public async Task Me_TakesTheRoleFromTheDatabase_NotFromTheToken()
    {
        var registered = await Host.RegisterAsync();
        await OwnerExecuteAsync(
            "UPDATE agency_members SET role = 'viewer' WHERE user_id = @userId", new { userId = registered.UserId });

        var me = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/auth/me", registered.AccessToken));

        Assert.Equal("viewer", me.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Me_AfterTheUserLeftTheAgency_Returns401()
    {
        var registered = await Host.RegisterAsync();
        await OwnerExecuteAsync("DELETE FROM agency_members WHERE user_id = @userId", new { userId = registered.UserId });

        var response = await Host.GetAsync("/v1/auth/me", registered.AccessToken);

        AssertStatus(HttpStatusCode.Unauthorized, response);
        Assert.Equal("membership_ended", await CodeOfAsync(response));
    }

    // --- Logout ------------------------------------------------------------------------------

    [Fact]
    public async Task Logout_Returns204_ExpiresTheCookie_AndEndsTheSession()
    {
        var registered = await Host.RegisterAsync();

        var response = await Host.PostAsync("/v1/auth/logout", refreshCookie: registered.RefreshToken);

        AssertStatus(HttpStatusCode.NoContent, response);
        var cookie = ApiTestHost.RefreshCookieHeader(response)!;
        Assert.Contains("expires=Thu, 01 Jan 1970", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await OwnerQueryAsync<long>(
            "SELECT count(*) FROM refresh_tokens WHERE user_id = @userId AND revoked_at IS NULL", new { userId = registered.UserId }));
        AssertStatus(HttpStatusCode.Unauthorized, await Host.PostAsync("/v1/auth/refresh", refreshCookie: registered.RefreshToken));
    }

    [Fact]
    public async Task Logout_EndsOnlyTheSessionItWasGiven()
    {
        var registered = await Host.RegisterAsync();
        var otherDevice = await Host.LoginAsync(registered.Email);

        await Host.PostAsync("/v1/auth/logout", refreshCookie: registered.RefreshToken);

        AssertStatus(HttpStatusCode.OK, await Host.PostAsync("/v1/auth/refresh", refreshCookie: otherDevice.RefreshToken));
    }

    [Fact]
    public async Task Logout_WithAnUnknownCookie_StillSucceeds()
    {
        var response = await Host.PostAsync("/v1/auth/logout", refreshCookie: "not-a-token-we-issued");

        AssertStatus(HttpStatusCode.NoContent, response);
    }

    [Fact]
    public async Task Logout_CannotEndAnotherUsersSession_WithoutHavingTheirCookie()
    {
        var ana = await Host.RegisterAsync();
        var bob = await Host.RegisterAsync();

        await Host.PostAsync("/v1/auth/logout", refreshCookie: "guess", accessToken: ana.AccessToken);

        AssertStatus(HttpStatusCode.OK, await Host.PostAsync("/v1/auth/refresh", refreshCookie: bob.RefreshToken));
    }

    // --- Switch agency -----------------------------------------------------------------------

    [Fact]
    public async Task SwitchAgency_IssuesTokensForTheOtherAgency_WithThatRole()
    {
        var registered = await Host.RegisterAsync(agencyName: "First agency");
        var second = await AddMembershipAsync(registered, "viewer", "Second agency");

        var response = await Host.PostAsync(
            "/v1/auth/switch-agency", new { agencyId = second }, registered.AccessToken, registered.RefreshToken);

        AssertStatus(HttpStatusCode.OK, response);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await ApiTestHost.ReadJsonAsync(response);
        var me = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/auth/me", body.GetProperty("accessToken").GetString()));
        Assert.Equal(second, me.GetProperty("agency").GetProperty("id").GetGuid());
        Assert.Equal("viewer", me.GetProperty("role").GetString());
        Assert.NotEqual(registered.RefreshToken, ApiTestHost.RefreshCookieValue(response));
    }

    [Fact]
    public async Task SwitchAgency_ReplacesTheRefreshCookie_AndEndsTheOldAgencysToken()
    {
        var registered = await Host.RegisterAsync();
        var second = await AddMembershipAsync(registered, "manager");

        var response = await Host.PostAsync(
            "/v1/auth/switch-agency", new { agencyId = second }, registered.AccessToken, registered.RefreshToken);

        var newCookie = ApiTestHost.RefreshCookieValue(response)!;
        AssertStatus(HttpStatusCode.OK, await Host.PostAsync("/v1/auth/refresh", refreshCookie: newCookie));
        Assert.Equal(1, await OwnerQueryAsync<long>(
            "SELECT count(*) FROM refresh_tokens WHERE user_id = @userId AND agency_id = @agencyId AND revoked_at IS NOT NULL",
            new { userId = registered.UserId, agencyId = registered.AgencyId }));
    }

    [Fact]
    public async Task SwitchAgency_TheNewAgencyBecomesTheDefaultForTheNextLogin()
    {
        var registered = await Host.RegisterAsync();
        var second = await AddMembershipAsync(registered, "manager", lastActiveAt: DateTimeOffset.UtcNow.AddDays(-30));

        await Host.PostAsync("/v1/auth/switch-agency", new { agencyId = second }, registered.AccessToken, registered.RefreshToken);

        Assert.Equal(second, (await Host.LoginAsync(registered.Email)).AgencyId);
    }

    [Fact]
    public async Task SwitchAgency_ToAnAgencyTheUserIsNotIn_Returns403_AndChangesNothing()
    {
        var ana = await Host.RegisterAsync();
        var bob = await Host.RegisterAsync();

        var response = await Host.PostAsync(
            "/v1/auth/switch-agency", new { agencyId = bob.AgencyId }, ana.AccessToken, ana.RefreshToken);

        AssertStatus(HttpStatusCode.Forbidden, response);
        Assert.Equal("not_a_member", await CodeOfAsync(response));
        Assert.Null(ApiTestHost.RefreshCookieHeader(response));
        AssertStatus(HttpStatusCode.OK, await Host.PostAsync("/v1/auth/refresh", refreshCookie: ana.RefreshToken));
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
    public async Task SwitchAgency_NeverEndsAnotherUsersRefreshToken()
    {
        var ana = await Host.RegisterAsync();
        var bob = await Host.RegisterAsync();
        var second = await AddMembershipAsync(ana, "viewer");

        // Ana presents Bob's cookie by mistake or on purpose: it is not hers, so it is left alone.
        await Host.PostAsync("/v1/auth/switch-agency", new { agencyId = second }, ana.AccessToken, bob.RefreshToken);

        AssertStatus(HttpStatusCode.OK, await Host.PostAsync("/v1/auth/refresh", refreshCookie: bob.RefreshToken));
    }
}
