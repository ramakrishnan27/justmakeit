using System.Net;
using System.Text.Json;
using Platform.Auth.Services;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// The member's Platform page end to end over a real database: the menu item and its permission, the list with the status the Site
/// admin set for the member's organization (and only theirs), the uploaded icon, and what happens when the Site admin takes the
/// permission away. The API connects as <c>app_user</c>, as in production.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Database")]
public sealed class PlatformsApiTests(PostgresFixture postgres) : AuthApiTestBase(postgres)
{
    private const string AdminPassword = "the site admin password";

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8];

    private static readonly string[] Seeded = ["Instagram", "Facebook", "YouTube", "Email", "SMS", "WhatsApp", "LinkedIn"];

    private readonly Pbkdf2PasswordHasher _hasher = new(iterations: 1_000);

    private sealed record SiteSession(Guid UserId, string AccessToken);

    private async Task<SiteSession> SiteLoginAsync()
    {
        var email = $"root-{Guid.NewGuid():N}@example.com";
        var userId = Guid.NewGuid();
        await OwnerExecuteAsync(
            "INSERT INTO users (id, email, password_hash, full_name) VALUES (@userId, @email, @hash, 'Root Admin'); INSERT INTO site_admins (user_id) VALUES (@userId)",
            new { userId, email, hash = _hasher.Hash(AdminPassword) });
        var response = await Host.PostAsync("/v1/site/login", new { email, password = AdminPassword });
        response.EnsureSuccessStatusCode();
        return new SiteSession(userId, ApiTestHost.SiteCookieValue(response)!);
    }

    private Task<HttpResponseMessage> SiteGetAsync(string path, SiteSession session) =>
        Host.GetAsync(path, tokenCookie: session.AccessToken, cookieName: ApiTestHost.SiteCookieName);

    private async Task<Guid> ChannelIdAsync(SiteSession session, string name)
    {
        var channels = (await ApiTestHost.ReadJsonAsync(await SiteGetAsync("/v1/site/channels", session))).EnumerateArray();
        return channels.Single(c => c.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();
    }

    private async Task EnableAsync(SiteSession session, Guid agencyId, string channel, bool enabled = true)
    {
        var id = await ChannelIdAsync(session, channel);
        var response = await Host.PutAsync(
            $"/v1/site/agencies/{agencyId}/channels/{id}", new { enabled }, tokenCookie: session.AccessToken, cookieName: ApiTestHost.SiteCookieName);
        AssertStatus(HttpStatusCode.NoContent, response);
    }

    private async Task<List<JsonElement>> PlatformsAsync(string memberToken)
    {
        var response = await Host.GetAsync("/v1/platforms", memberToken);
        AssertStatus(HttpStatusCode.OK, response);
        return (await ApiTestHost.ReadJsonAsync(response)).EnumerateArray().ToList();
    }

    private async Task<string[]> MenuLabelsAsync(string memberToken) =>
        [.. (await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/menu", memberToken))).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("label").GetString()!)];

    // --- The menu item -----------------------------------------------------------------------

    [Theory]
    [InlineData("company")]
    [InlineData("agency")]
    [InlineData("creator")]
    public async Task EveryKindOfOrganization_FindsThePlatformItemInItsMenu_AndTheTokenCarriesThePermission(string businessModel)
    {
        var founder = await Host.RegisterAsync(businessModel: businessModel);

        var menu = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/menu", founder.AccessToken));

        var item = menu.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("pageKey").GetString() == "platforms");
        Assert.Equal("Platform", item.GetProperty("label").GetString());
        Assert.Equal("/platforms", item.GetProperty("path").GetString());
        Assert.Equal("list", item.GetProperty("icon").GetString());
        Assert.Contains(menu.GetProperty("permissions").EnumerateArray(), p => p.GetString() == "platforms.view");
    }

    // --- The list ----------------------------------------------------------------------------

    [Theory]
    [InlineData("company")]
    [InlineData("agency")]
    [InlineData("creator")]
    public async Task TheList_HasEveryChannel_NoneEnabledToStartWith_AndOnlyTheFieldsThePageNeeds(string businessModel)
    {
        var founder = await Host.RegisterAsync(businessModel: businessModel);

        var response = await Host.GetAsync("/v1/platforms", founder.AccessToken);

        AssertStatus(HttpStatusCode.OK, response);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var platforms = (await ApiTestHost.ReadJsonAsync(response)).EnumerateArray().ToList();
        Assert.Equal(Seeded, platforms.Select(p => p.GetProperty("name").GetString()));
        Assert.All(platforms, p => Assert.False(p.GetProperty("isEnabled").GetBoolean()));
        Assert.Equal(
            ["id", "name", "description", "iconKey", "hasIconImage", "isEnabled"],
            platforms[0].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task WhatTheSiteAdminEnables_ShowsAsEnabled_ForThatOrganizationOnly_AndFollowsTheChange()
    {
        var site = await SiteLoginAsync();
        var one = await Host.RegisterAsync(businessModel: "company");
        var two = await Host.RegisterAsync(businessModel: "agency");
        await EnableAsync(site, one.AgencyId, "Email");
        await EnableAsync(site, one.AgencyId, "LinkedIn");

        Assert.Equal(["Email", "LinkedIn"], (await PlatformsAsync(one.AccessToken)).Where(p => p.GetProperty("isEnabled").GetBoolean()).Select(p => p.GetProperty("name").GetString()));
        Assert.DoesNotContain(await PlatformsAsync(two.AccessToken), p => p.GetProperty("isEnabled").GetBoolean());

        await EnableAsync(site, one.AgencyId, "Email", enabled: false);

        Assert.Equal(["LinkedIn"], (await PlatformsAsync(one.AccessToken)).Where(p => p.GetProperty("isEnabled").GetBoolean()).Select(p => p.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task AChannelTheSiteAdminAdded_AppearsForMembers_WithItsIconServedBackByteForByte()
    {
        var site = await SiteLoginAsync();
        var member = await Host.RegisterAsync(businessModel: "company");
        var created = await Host.PostAsync(
            "/v1/site/channels", ApiTestHost.ChannelForm("Telegram", "Fast chat", Png, "image/png"), tokenCookie: site.AccessToken, cookieName: ApiTestHost.SiteCookieName);
        AssertStatus(HttpStatusCode.Created, created);
        var id = (await ApiTestHost.ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var telegram = (await PlatformsAsync(member.AccessToken)).Last();

        Assert.Equal(id, telegram.GetProperty("id").GetGuid());
        Assert.True(telegram.GetProperty("hasIconImage").GetBoolean());
        Assert.Equal(JsonValueKind.Null, telegram.GetProperty("iconKey").ValueKind);
        var icon = await Host.GetAsync($"/v1/platforms/{id}/icon", member.AccessToken);
        AssertStatus(HttpStatusCode.OK, icon);
        Assert.Equal("image/png", icon.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await icon.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", icon.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(icon.Headers.CacheControl?.Private);
        Assert.Equal(TimeSpan.FromHours(1), icon.Headers.CacheControl?.MaxAge);
    }

    [Fact]
    public async Task TheIcon_OfABuiltInChannelOrAnUnknownOne_IsAnswered404()
    {
        var site = await SiteLoginAsync();
        var member = await Host.RegisterAsync(businessModel: "creator");

        var builtIn = await Host.GetAsync($"/v1/platforms/{await ChannelIdAsync(site, "Email")}/icon", member.AccessToken);
        var unknown = await Host.GetAsync($"/v1/platforms/{Guid.NewGuid()}/icon", member.AccessToken);

        AssertStatus(HttpStatusCode.NotFound, builtIn);
        Assert.Equal("platform_not_found", await CodeOfAsync(builtIn));
        AssertStatus(HttpStatusCode.NotFound, unknown);
    }

    // --- Who ---------------------------------------------------------------------------------

    [Fact]
    public async Task ASiteAdmin_AndAVisitor_CannotReadThePlatformsAsMembers()
    {
        var site = await SiteLoginAsync();

        AssertStatus(HttpStatusCode.Forbidden, await Host.GetAsync("/v1/platforms", tokenCookie: site.AccessToken, cookieName: ApiTestHost.SiteCookieName));
        AssertStatus(HttpStatusCode.Unauthorized, await Host.GetAsync("/v1/platforms"));
    }

    [Fact]
    public async Task RevokingThePermission_TakesTheItemAndTheEndpointFromThatRole_AfterTheySignInAgain_AndGrantingItBackRestoresThem()
    {
        var site = await SiteLoginAsync();
        var admin = await Host.RegisterAsync(businessModel: "company");
        Assert.Contains("Platform", await MenuLabelsAsync(admin.AccessToken));
        var url = "/v1/site/permissions/platforms.view/grants";

        AssertStatus(HttpStatusCode.OK, await Host.PutAsync(
            url, new { businessModel = "company", role = "organizationadmin", granted = false }, tokenCookie: site.AccessToken, cookieName: ApiTestHost.SiteCookieName));

        // The old token is out of date: the person is told to sign in again.
        AssertStatus(HttpStatusCode.Unauthorized, await Host.GetAsync("/v1/platforms", admin.AccessToken));
        var without = (await Host.LoginAsync(admin.Email)).AccessToken;
        Assert.DoesNotContain("Platform", await MenuLabelsAsync(without));
        AssertStatus(HttpStatusCode.Forbidden, await Host.GetAsync("/v1/platforms", without));

        AssertStatus(HttpStatusCode.OK, await Host.PutAsync(
            url, new { businessModel = "company", role = "organizationadmin", granted = true }, tokenCookie: site.AccessToken, cookieName: ApiTestHost.SiteCookieName));
        var with = (await Host.LoginAsync(admin.Email)).AccessToken;

        Assert.Contains("Platform", await MenuLabelsAsync(with));
        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/platforms", with));
    }
}
