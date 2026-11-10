using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Platform.Helpers.Constants;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// The menu and permission endpoints through the real pipeline, with no database: who may call each route,
/// what the member and site admin sidebars hold, and every rule that is decided before a query runs.
/// </summary>
public sealed class MenuEndpointTests : IAsyncLifetime
{
    private ApiTestHost _host = null!;

    public Task InitializeAsync()
    {
        _host = ApiTestHost.StartWithoutDatabase();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private string MemberToken(OrganizationRole role = OrganizationRole.OrganizationAdmin, IEnumerable<string>? permissions = null) =>
        _host.Factory.CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), role, permissions);

    private string SiteAdminToken() => _host.Factory.CreateSiteAdminAccessToken(Guid.NewGuid());

    private static string[] LabelsIn(JsonElement menu) =>
        [.. menu.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("label").GetString()!)];

    private static string[] StringsIn(JsonElement array) => [.. array.EnumerateArray().Select(value => value.GetString()!)];

    // --- The member menu ---------------------------------------------------------------------

    [Fact]
    public async Task TheMemberMenu_IsClosedToAnAnonymousCaller()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.GetAsync("/v1/menu")).StatusCode);
    }

    [Theory]
    [InlineData(OrganizationRole.OrganizationAdmin)]
    [InlineData(OrganizationRole.ClientManager)]
    [InlineData(OrganizationRole.MarketingManager)]
    [InlineData(OrganizationRole.ContentWriter)]
    public async Task EveryRoleWithTheDefaultPermissions_SeesTheDashboardAndTheWorkspacesPage(OrganizationRole role)
    {
        var response = await _host.GetAsync("/v1/menu", MemberToken(role));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var menu = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal(["Dashboard", "Workspaces"], LabelsIn(menu));
        Assert.Equal(
            DefaultGrantsPermissionService.DefaultKeys(role).Order(StringComparer.Ordinal),
            StringsIn(menu.GetProperty("permissions")));
    }

    [Fact]
    public async Task TheMemberMenu_CarriesTheIdLabelIconPathAndPageOfEachItem()
    {
        var menu = await ApiTestHost.ReadJsonAsync(await _host.GetAsync("/v1/menu", MemberToken()));

        var workspaces = menu.GetProperty("items")[1];
        Assert.NotEqual(Guid.Empty, workspaces.GetProperty("id").GetGuid());
        Assert.Equal("workspaces", workspaces.GetProperty("pageKey").GetString());
        Assert.Equal("Workspaces", workspaces.GetProperty("label").GetString());
        Assert.Equal("folder", workspaces.GetProperty("icon").GetString());
        Assert.Equal("/workspaces", workspaces.GetProperty("path").GetString());
    }

    [Fact]
    public async Task ATokenWithoutTheWorkspacesPermission_DoesNotSeeTheWorkspacesItem()
    {
        var token = MemberToken(OrganizationRole.ClientManager, [PermissionKeys.DashboardView, PermissionKeys.DataRead]);

        var menu = await ApiTestHost.ReadJsonAsync(await _host.GetAsync("/v1/menu", token));

        Assert.Equal(["Dashboard"], LabelsIn(menu));
        Assert.Equal([PermissionKeys.DashboardView, PermissionKeys.DataRead], StringsIn(menu.GetProperty("permissions")));
    }

    [Fact]
    public async Task ATokenWithNoPermissions_SeesAnEmptyMenu_NotAnError()
    {
        var response = await _host.GetAsync("/v1/menu", MemberToken(permissions: []));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var menu = await ApiTestHost.ReadJsonAsync(response);
        Assert.Empty(LabelsIn(menu));
        Assert.Empty(menu.GetProperty("permissions").EnumerateArray());
    }

    [Fact]
    public async Task TheMemberMenu_NeverContainsAnAdminPage()
    {
        var menu = await ApiTestHost.ReadJsonAsync(await _host.GetAsync("/v1/menu", MemberToken(permissions: PermissionKeys.All)));

        Assert.DoesNotContain(menu.GetProperty("items").EnumerateArray(), item => item.GetProperty("path").GetString()!.StartsWith("/admin", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheMemberMenu_IsRefused_ForASiteAdminToken_AndForAStaleOne()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _host.GetAsync("/v1/menu", SiteAdminToken())).StatusCode);

        var stale = _host.Factory.CreateAccessTokenWithVersion(Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.OrganizationAdmin, 99);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.GetAsync("/v1/menu", stale)).StatusCode);
    }

    [Fact]
    public async Task TheMenuResponses_AreNeverCached()
    {
        var response = await _host.GetAsync("/v1/menu", MemberToken());

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }

    // --- The site admin menu -----------------------------------------------------------------

    [Fact]
    public async Task TheSiteAdminMenu_HoldsTheAdminPages_AndNoPermissions()
    {
        var response = await _host.GetAsync("/v1/site/menu", SiteAdminToken());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var menu = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal(["Dashboard", "Menus", "Permissions"], LabelsIn(menu));
        Assert.Equal(
            ["/admin", "/admin/menus", "/admin/permissions"],
            menu.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("path").GetString()));
        Assert.Empty(menu.GetProperty("permissions").EnumerateArray());
    }

    [Fact]
    public async Task TheSiteAdminMenu_IsClosedToAnonymousCallers_AndToEveryMember()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.GetAsync("/v1/site/menu")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _host.GetAsync("/v1/site/menu", MemberToken())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _host.GetAsync("/v1/site/menu", MemberToken(permissions: PermissionKeys.All))).StatusCode);
    }

    // --- Who may call the configuration routes -----------------------------------------------

    public static TheoryData<string, string> ConfigurationRoutes() => new()
    {
        { "GET", "/v1/site/menus/member" },
        { "POST", "/v1/site/menus/member/items" },
        { "PUT", "/v1/site/menus/member/items/11111111-1111-1111-1111-111111111111" },
        { "DELETE", "/v1/site/menus/member/items/11111111-1111-1111-1111-111111111111" },
        { "PUT", "/v1/site/menus/member/order" },
        { "GET", "/v1/site/permissions" },
        { "PUT", "/v1/site/permissions/workspaces.create/grants" }
    };

    private async Task<HttpResponseMessage> SendAsync(string method, string path, string? token)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new { });
        }

        if (token is not null)
        {
            request.Headers.Authorization = new("Bearer", token);
        }

        return await _host.Client.SendAsync(request);
    }

    [Theory]
    [MemberData(nameof(ConfigurationRoutes))]
    public async Task EveryConfigurationRoute_IsClosedToAnAnonymousCaller(string method, string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(method, path, null)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(ConfigurationRoutes))]
    public async Task EveryConfigurationRoute_IsForbiddenToEveryMember_EvenWithEveryPermission(string method, string path)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(method, path, MemberToken(permissions: PermissionKeys.All))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(method, path, MemberToken(OrganizationRole.ContentWriter))).StatusCode);
    }

    // --- Rules decided before any query ------------------------------------------------------

    [Theory]
    [InlineData("other")]
    [InlineData("Member")]
    [InlineData("members")]
    public async Task AnUnknownMenu_IsNotFound(string menuKey)
    {
        var response = await _host.GetAsync($"/v1/site/menus/{menuKey}", SiteAdminToken());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("menu_not_found", (await ApiTestHost.ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task AddingAnItem_WithBadFields_ListsEachOne()
    {
        var response = await _host.PostAsync("/v1/site/menus/member/items", new { pageKey = "Bad Key", label = " ", icon = "rocket" }, SiteAdminToken());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
        var errors = body.GetProperty("errors");
        Assert.True(errors.TryGetProperty("pageKey", out _));
        Assert.True(errors.TryGetProperty("label", out _));
        Assert.True(errors.TryGetProperty("icon", out _));
    }

    [Fact]
    public async Task ChangingAnItem_WithoutAVisibility_IsABadRequest()
    {
        var response = await _host.PutAsync(
            "/v1/site/menus/member/items/11111111-1111-1111-1111-111111111111", new { label = "Spaces", icon = "users" }, SiteAdminToken());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("isVisible", out _));
    }

    [Fact]
    public async Task ReorderingWithADuplicateId_IsABadRequest()
    {
        var id = Guid.NewGuid();

        var response = await _host.PutAsync("/v1/site/menus/member/order", new { itemIds = new[] { id, id } }, SiteAdminToken());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("itemIds", out _));
    }

    [Fact]
    public async Task AnItemIdThatIsNotAGuid_MatchesNoRoute_SoNothingRuns()
    {
        var response = await _host.PutAsync("/v1/site/menus/member/items/not-a-guid", new { label = "x", icon = "home", isVisible = true }, SiteAdminToken());

        // A route that does not exist falls to the fallback policy, which needs an agency: a site admin token gets
        // 403 there, as on every unknown route (see the site admin decisions in CLAUDE.md). Either way no service ran.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SettingAGrant_WithBadFields_ListsEachOne()
    {
        var response = await _host.PutAsync(
            "/v1/site/permissions/workspaces.create/grants", new { businessModel = "holding", role = "owner" }, SiteAdminToken());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("businessModel", out _));
        Assert.True(errors.TryGetProperty("role", out _));
        Assert.True(errors.TryGetProperty("granted", out _));
    }

    [Fact]
    public async Task SettingAGrant_ForARoleThatDoesNotExistInThatModel_IsAnInvalidSlot()
    {
        var response = await _host.PutAsync(
            "/v1/site/permissions/workspaces.create/grants", new { businessModel = "creator", role = "organizationadmin", granted = true }, SiteAdminToken());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_slot", (await ApiTestHost.ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task SettingAGrant_ForAPermissionThatDoesNotExist_IsNotFound()
    {
        var response = await _host.PutAsync(
            "/v1/site/permissions/workspaces.crate/grants", new { businessModel = "company", role = "organizationadmin", granted = true }, SiteAdminToken());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("permission_not_found", (await ApiTestHost.ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task RevokingTheProtectedPermission_IsAConflict()
    {
        var response = await _host.PutAsync(
            "/v1/site/permissions/dashboard.view/grants", new { businessModel = "agency", role = "clientmanager", granted = false }, SiteAdminToken());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("permission_protected", (await ApiTestHost.ReadJsonAsync(response)).GetProperty("code").GetString());
    }
}
