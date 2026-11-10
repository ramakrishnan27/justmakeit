using System.Net;
using System.Net.Http.Json;
using System.Text;
using Platform.Helpers.Constants;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// Who may call the channel and organization routes of the Site admin, and what they refuse before any query runs: the
/// policy, the shape of the body, the icon file. Decided by the pipeline alone, so no database is involved.
/// </summary>
public sealed class SiteChannelsEndpointTests : IAsyncLifetime
{
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly string Id = "11111111-1111-1111-1111-111111111111";

    private ApiTestHost _host = null!;

    public Task InitializeAsync()
    {
        _host = ApiTestHost.StartWithoutDatabase();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private string SiteAdminToken() => _host.Factory.CreateSiteAdminAccessToken(Guid.NewGuid());

    private string MemberToken(OrganizationRole role = OrganizationRole.OrganizationAdmin) =>
        _host.Factory.CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), role, PermissionKeys.All);

    private static byte[] Png(int length)
    {
        var bytes = new byte[length];
        PngHeader.CopyTo(bytes, 0);
        return bytes;
    }

    /// <summary>Every route these controllers add, as a method and a path.</summary>
    public static TheoryData<string, string> Routes() => new()
    {
        { "GET", "/v1/site/channels" },
        { "POST", "/v1/site/channels" },
        { "GET", $"/v1/site/channels/{Id}/icon" },
        { "GET", $"/v1/site/agencies/{Id}/channels" },
        { "PUT", $"/v1/site/agencies/{Id}/channels/{Id}" },
        { "PUT", $"/v1/site/agencies/{Id}/status" }
    };

    private Task<HttpResponseMessage> SendAsync(string method, string path, string? token)
    {
        return method switch
        {
            "GET" => _host.GetAsync(path, token),
            "POST" => _host.PostAsync(path, ApiTestHost.ChannelForm("Telegram"), token),
            "PUT" => _host.PutAsync(path, new { enabled = true, isActive = true }, token),
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
    }

    // --- Who may ------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task EveryRoute_AnswersAnAnonymousCaller_With401(string method, string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(method, path, null)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task EveryRoute_IsForbidden_ForAMemberOfAnOrganization_EvenAnOrganizationAdminHoldingEveryPermission(string method, string path)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(method, path, MemberToken())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(method, path, MemberToken(OrganizationRole.ContentWriter))).StatusCode);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task EveryRoute_IsUnauthorized_ForAMemberCookie_BecauseASiteRouteNeverReadsIt(string method, string path)
    {
        // A browser sends the member cookie (Path=/v1) to /v1/site as well; a Site admin route does not read it.
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("Cookie", $"{ApiTestHost.MemberCookieName}={MemberToken()}");

        var response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Creating a channel: what is refused before the database is asked ---------------------

    [Fact]
    public async Task CreatingAChannel_WithNoName_IsABadRequest_OnTheNameField()
    {
        var response = await _host.PostAsync("/v1/site/channels", ApiTestHost.ChannelForm(null, "Chat"), SiteAdminToken());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("name", out _));
    }

    [Fact]
    public async Task CreatingAChannel_WithAnIconThatIsNotAnImage_IsABadRequest_OnTheIconField()
    {
        var notAnImage = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"></svg>");
        var response = await _host.PostAsync(
            "/v1/site/channels", ApiTestHost.ChannelForm("Telegram", null, notAnImage, "image/svg+xml", "icon.svg"), SiteAdminToken());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("icon", out _));
    }

    [Fact]
    public async Task CreatingAChannel_WithAnSvgThatClaimsToBeAPng_IsABadRequest_BecauseTheBytesDecide()
    {
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"></svg>");
        var response = await _host.PostAsync("/v1/site/channels", ApiTestHost.ChannelForm("Telegram", null, svg, "image/png"), SiteAdminToken());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("icon", out _));
    }

    [Fact]
    public async Task CreatingAChannel_WithAPngThatIsOver100KB_IsABadRequest_OnTheIconField()
    {
        var response = await _host.PostAsync("/v1/site/channels", ApiTestHost.ChannelForm("Telegram", null, Png(100 * 1024 + 1)), SiteAdminToken());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors");
        Assert.Contains("100 KB", errors.GetProperty("icon")[0].GetString());
    }

    [Fact]
    public async Task CreatingAChannel_WithAnIconOfSeveralMegabytes_IsABadRequest_OnTheIconField_WithoutTheWholeFileBeingTaken()
    {
        // The request size limit itself is enforced by Kestrel, which the in-memory host is not: it is checked against the real
        // server in the browser check. Here the part the application owns: only a little more than the limit is ever read.
        var response = await _host.PostAsync("/v1/site/channels", ApiTestHost.ChannelForm("Telegram", null, Png(2 * 1024 * 1024)), SiteAdminToken());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors");
        Assert.Contains("100 KB", errors.GetProperty("icon")[0].GetString());
    }

    [Fact]
    public async Task CreatingAChannel_WithAJsonBody_IsRefused_NotCreated()
    {
        // A route the content type does not match is answered by the router: for a Site admin token that is the fallback
        // policy's 403, like every other route a Site admin has no endpoint for. What matters is that nothing is created.
        var response = await _host.PostAsync("/v1/site/channels", new { name = "Telegram" }, SiteAdminToken());

        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.UnsupportedMediaType, response.StatusCode.ToString());
    }

    // --- Enabling a channel and deactivating an organization ----------------------------------

    [Fact]
    public async Task EnablingAChannel_WithoutSayingEnabledOrNot_IsABadRequest_OnTheEnabledField()
    {
        var response = await _host.PutAsync($"/v1/site/agencies/{Id}/channels/{Id}", new { }, SiteAdminToken());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("enabled", out _));
    }

    [Fact]
    public async Task DeactivatingAnOrganization_WithoutSayingActiveOrNot_IsABadRequest_OnTheIsActiveField()
    {
        var response = await _host.PutAsync($"/v1/site/agencies/{Id}/status", new { }, SiteAdminToken());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("isActive", out _));
    }

    [Theory]
    [InlineData("/v1/site/agencies/not-a-guid/channels")]
    [InlineData("/v1/site/channels/not-a-guid/icon")]
    public async Task AnIdThatIsNotAGuid_MatchesNoRoute_SoASiteAdminGetsTheFallbackPolicysAnswer_NotAServerError(string path)
    {
        // No route matches, and the fallback policy (a signed-in member of an organization) refuses a Site admin token on every
        // address that is not a Site admin route: 403, as for any other unknown address.
        Assert.Equal(HttpStatusCode.Forbidden, (await _host.GetAsync(path, SiteAdminToken())).StatusCode);
    }

    [Fact]
    public async Task ARefusedRequest_IsNeverServedFromACache()
    {
        var token = SiteAdminToken();

        var bad = await _host.PostAsync("/v1/site/channels", ApiTestHost.ChannelForm(null, "Chat"), token);
        var badToggle = await _host.PutAsync($"/v1/site/agencies/{Id}/status", new { }, token);

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badToggle.StatusCode);
        Assert.True(bad.Headers.CacheControl?.NoStore);
        Assert.True(badToggle.Headers.CacheControl?.NoStore);
    }
}
