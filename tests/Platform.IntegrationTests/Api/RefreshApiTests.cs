using System.Net;
using Microsoft.IdentityModel.JsonWebTokens;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

/// <summary>Refresh token rotation and reuse detection, end to end through the API and the database.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class RefreshApiTests(PostgresFixture postgres) : AuthApiTestBase(postgres)
{
    private Task<HttpResponseMessage> RefreshAsync(string? cookie) => Host.PostAsync("/v1/auth/refresh", refreshCookie: cookie);

    private async Task<long> LiveTokensAsync(Session session) => await OwnerQueryAsync<long>(
        "SELECT count(*) FROM refresh_tokens WHERE user_id = @userId AND revoked_at IS NULL", new { userId = session.UserId });

    [Fact]
    public async Task Refresh_ReturnsAWorkingAccessToken_AndANewRefreshCookie()
    {
        var registered = await Host.RegisterAsync();

        var response = await RefreshAsync(registered.RefreshToken);

        AssertStatus(HttpStatusCode.OK, response);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var newCookie = ApiTestHost.RefreshCookieValue(response);
        Assert.NotNull(newCookie);
        Assert.NotEqual(registered.RefreshToken, newCookie);
        var body = await ApiTestHost.ReadJsonAsync(response);
        Assert.DoesNotContain(body.EnumerateObject(), p => p.Name.Contains("refresh", StringComparison.OrdinalIgnoreCase));
        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/auth/me", body.GetProperty("accessToken").GetString()));
        var cookie = ApiTestHost.RefreshCookieHeader(response)!;
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_RevokesTheOldToken_AndLeavesExactlyOneLive()
    {
        var registered = await Host.RegisterAsync();

        await RefreshAsync(registered.RefreshToken);

        Assert.Equal(1, await LiveTokensAsync(registered));
        Assert.Equal(1, await OwnerQueryAsync<long>(
            "SELECT count(*) FROM refresh_tokens WHERE user_id = @userId AND revoked_at IS NOT NULL AND replaced_by_id IS NOT NULL",
            new { userId = registered.UserId }));
    }

    [Fact]
    public async Task Refresh_ChainsAcrossManyRotations()
    {
        var cookie = (await Host.RegisterAsync()).RefreshToken;

        for (var i = 0; i < 5; i++)
        {
            var response = await RefreshAsync(cookie);
            AssertStatus(HttpStatusCode.OK, response);
            cookie = ApiTestHost.RefreshCookieValue(response)!;
        }
    }

    [Fact]
    public async Task Refresh_AccessToken_IsForTheSameUserAgencyAndRole()
    {
        var registered = await Host.RegisterAsync();

        var body = await ApiTestHost.ReadJsonAsync(await RefreshAsync(registered.RefreshToken));

        var token = new JsonWebToken(body.GetProperty("accessToken").GetString()!);
        Assert.Equal(registered.UserId.ToString(), token.GetClaim("user_id").Value);
        Assert.Equal(registered.AgencyId.ToString(), token.GetClaim("agency_id").Value);
        Assert.Equal("owner", token.GetClaim("role").Value);
    }

    // --- Reuse detection ---------------------------------------------------------------------

    [Fact]
    public async Task Refresh_ReplayOfARotatedToken_IsRejected_AndEndsTheWholeSession()
    {
        var registered = await Host.RegisterAsync();
        var rotated = await RefreshAsync(registered.RefreshToken);
        var newCookie = ApiTestHost.RefreshCookieValue(rotated)!;

        // The thief replays the old cookie: rejected, and the rotated-to token dies with it.
        var replay = await RefreshAsync(registered.RefreshToken);

        AssertStatus(HttpStatusCode.Unauthorized, replay);
        Assert.Equal("invalid_refresh_token", await CodeOfAsync(replay));
        AssertStatus(HttpStatusCode.Unauthorized, await RefreshAsync(newCookie));
        Assert.Equal(0, await LiveTokensAsync(registered));
    }

    [Fact]
    public async Task Refresh_ReuseEndsOnlyThatUsersTokensForThatAgency()
    {
        var ana = await Host.RegisterAsync();
        var anaOtherAgency = await AddMembershipAsync(ana, "viewer");
        var anaInOther = await Host.LoginAsync(ana.Email, agencyId: anaOtherAgency);
        var bob = await Host.RegisterAsync();
        await RefreshAsync(ana.RefreshToken);

        await RefreshAsync(ana.RefreshToken); // replay

        AssertStatus(HttpStatusCode.OK, await RefreshAsync(anaInOther.RefreshToken));
        AssertStatus(HttpStatusCode.OK, await RefreshAsync(bob.RefreshToken));
    }

    [Fact]
    public async Task Refresh_ReplayAfterLogout_IsRejected()
    {
        var registered = await Host.RegisterAsync();
        await Host.PostAsync("/v1/auth/logout", refreshCookie: registered.RefreshToken);

        AssertStatus(HttpStatusCode.Unauthorized, await RefreshAsync(registered.RefreshToken));
    }

    [Fact]
    public async Task Refresh_WhenManyRequestsPresentTheSameCookie_AtMostOneSucceeds()
    {
        var registered = await Host.RegisterAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => RefreshAsync(registered.RefreshToken))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
        // The losers were reuse: nothing the user held for this agency stays alive.
        Assert.Equal(0, await LiveTokensAsync(registered));
    }

    // --- Rejections --------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Refresh_WithoutAValidCookie_Returns401(string? cookie)
    {
        var response = await RefreshAsync(cookie);

        AssertStatus(HttpStatusCode.Unauthorized, response);
        Assert.Equal("invalid_refresh_token", await CodeOfAsync(response));
        Assert.Null(ApiTestHost.RefreshCookieHeader(response));
    }

    [Fact]
    public async Task Refresh_OfAnExpiredToken_Returns401_AndIssuesNothing()
    {
        var registered = await Host.RegisterAsync();
        await OwnerExecuteAsync(
            "UPDATE refresh_tokens SET expires_at = now() - interval '1 minute' WHERE user_id = @userId", new { userId = registered.UserId });

        var response = await RefreshAsync(registered.RefreshToken);

        AssertStatus(HttpStatusCode.Unauthorized, response);
        Assert.Null(ApiTestHost.RefreshCookieHeader(response));
        Assert.Equal(1, await OwnerQueryAsync<long>("SELECT count(*) FROM refresh_tokens"));
    }

    [Fact]
    public async Task Refresh_TakesTheRoleFromTheDatabase_SoADemotionAppliesAtOnce()
    {
        var registered = await Host.RegisterAsync();
        await OwnerExecuteAsync("UPDATE agency_members SET role = 'viewer' WHERE user_id = @userId", new { userId = registered.UserId });

        var body = await ApiTestHost.ReadJsonAsync(await RefreshAsync(registered.RefreshToken));

        var token = new JsonWebToken(body.GetProperty("accessToken").GetString()!);
        Assert.Equal("viewer", token.GetClaim("role").Value);
        var create = await Host.PostAsync("/v1/workspaces", new { name = "Not allowed" }, body.GetProperty("accessToken").GetString());
        AssertStatus(HttpStatusCode.Forbidden, create);
    }

    [Fact]
    public async Task Refresh_AfterTheUserLeftTheAgency_Returns401_AndEndsTheirSessionsThere()
    {
        var registered = await Host.RegisterAsync();
        var second = await Host.LoginAsync(registered.Email);
        await OwnerExecuteAsync("DELETE FROM agency_members WHERE user_id = @userId", new { userId = registered.UserId });

        AssertStatus(HttpStatusCode.Unauthorized, await RefreshAsync(registered.RefreshToken));

        Assert.Equal(0, await LiveTokensAsync(registered));
        AssertStatus(HttpStatusCode.Unauthorized, await RefreshAsync(second.RefreshToken));
    }

    [Fact]
    public async Task Refresh_ACookieFromOneUser_NeverYieldsATokenForAnother()
    {
        var ana = await Host.RegisterAsync();
        var bob = await Host.RegisterAsync();

        var body = await ApiTestHost.ReadJsonAsync(await RefreshAsync(ana.RefreshToken));

        var token = new JsonWebToken(body.GetProperty("accessToken").GetString()!);
        Assert.Equal(ana.UserId.ToString(), token.GetClaim("user_id").Value);
        Assert.NotEqual(bob.UserId.ToString(), token.GetClaim("user_id").Value);
        Assert.Equal(ana.AgencyId.ToString(), token.GetClaim("agency_id").Value);
    }

    [Fact]
    public async Task Refresh_DoesNotNeedAnAccessToken_AndIgnoresAnExpiredOne()
    {
        var registered = await Host.RegisterAsync();

        var response = await Host.PostAsync("/v1/auth/refresh", accessToken: "an-expired-or-invalid-access-token", refreshCookie: registered.RefreshToken);

        AssertStatus(HttpStatusCode.OK, response);
    }
}
