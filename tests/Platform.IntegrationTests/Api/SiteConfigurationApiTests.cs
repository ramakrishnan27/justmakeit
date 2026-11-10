using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.IdentityModel.JsonWebTokens;
using Platform.Auth.Services;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// The Site admin's menu and permission editors end to end, against a real database: what a change does to the
/// sidebars people see, to the permissions in their next token and to the endpoints they call, and that a
/// site admin who was demoted is refused at once even though their token has not expired.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Database")]
public sealed class SiteConfigurationApiTests(PostgresFixture postgres) : AuthApiTestBase(postgres)
{
    private Guid _siteAdminId;
    private string _siteAdmin = string.Empty;

    private async Task<string> SiteAdminTokenAsync()
    {
        if (_siteAdmin.Length == 0)
        {
            await using var owner = await Host.Database.OpenOwnerAsync();
            _siteAdminId = await DbTestSupport.InsertUserAsync(owner, $"{Guid.NewGuid():N}@example.com");
            await DbTestSupport.InsertSiteAdminAsync(owner, _siteAdminId);
            _siteAdmin = Host.Factory.CreateSiteAdminAccessToken(_siteAdminId);
        }

        return _siteAdmin;
    }

    private async Task<JsonElement> ConfigAsync(string menu)
    {
        var response = await Host.GetAsync($"/v1/site/menus/{menu}", await SiteAdminTokenAsync());
        AssertStatus(HttpStatusCode.OK, response);
        return await ApiTestHost.ReadJsonAsync(response);
    }

    // The four placeholder pages seeded by 013_member_studio_menu.sql, between Dashboard and Workspaces.
    private static readonly string[] StudioLabels = ["Content Studio", "Media Library", "AI Studio", "Calendar"];
    private static readonly string[] StudioPages = ["content-studio", "media-library", "ai-studio", "calendar"];

    /// <summary>The labels of the member menu: the first item, the four studio pages, then the rest.</summary>
    private static string[] MenuLabels(string first, params string[] rest) => [first, .. StudioLabels, .. rest];

    /// <summary>The page keys of the member menu: the first page, the four studio pages, then the rest.</summary>
    private static string[] MenuPages(string first, params string[] rest) => [first, .. StudioPages, .. rest];

    private static Guid IdOf(JsonElement config, string pageKey) =>
        config.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("pageKey").GetString() == pageKey).GetProperty("id").GetGuid();

    private static string[] LabelsIn(JsonElement menu) =>
        [.. menu.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("label").GetString()!)];

    private static string[] PagesIn(JsonElement config) =>
        [.. config.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("pageKey").GetString()!)];

    private async Task<string[]> MemberLabelsAsync(string accessToken) =>
        LabelsIn(await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/menu", accessToken)));

    private async Task<HttpResponseMessage> UpdateAsync(string menu, Guid id, string label, string icon, bool visible) =>
        await Host.PutAsync($"/v1/site/menus/{menu}/items/{id}", new { label, icon, isVisible = visible }, await SiteAdminTokenAsync());

    // There is no refresh: a person whose token is out of date signs in again, and the new token carries today's permissions.
    private async Task<string> SignInAgainAsync(Session member) => (await Host.LoginAsync(member.Email)).AccessToken;

    // --- Reading a menu ----------------------------------------------------------------------

    [Fact]
    public async Task TheMemberMenuConfiguration_ListsEveryItem_AndTheIconsToChooseFrom()
    {
        var config = await ConfigAsync("member");

        Assert.Equal("member", config.GetProperty("menuKey").GetString());
        Assert.Equal(MenuPages("dashboard", "workspaces", "platforms", "team"), PagesIn(config));
        var dashboard = config.GetProperty("items")[0];
        Assert.True(dashboard.GetProperty("isLocked").GetBoolean());
        Assert.Equal(PermissionKeys.DashboardView, dashboard.GetProperty("requiredPermission").GetString());
        Assert.Equal("/dashboard", dashboard.GetProperty("path").GetString());
        var items = config.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("pageKey").GetString()!);
        Assert.False(items["workspaces"].GetProperty("isLocked").GetBoolean());
        var team = items["team"];
        Assert.False(team.GetProperty("isLocked").GetBoolean());
        Assert.Equal(PermissionKeys.MembersManage, team.GetProperty("requiredPermission").GetString());
        Assert.Equal("/team", team.GetProperty("path").GetString());
        Assert.Empty(config.GetProperty("availablePages").EnumerateArray());
        Assert.Equal(MenuIcons.All, config.GetProperty("icons").EnumerateArray().Select(i => i.GetString()));
    }

    [Fact]
    public async Task TheAdminMenuConfiguration_HasFiveItems_OnlyTheThreeRepairPagesLocked_AndNoneNeedsAPermission()
    {
        var config = await ConfigAsync("admin");

        Assert.Equal(["dashboard", "users", "channels", "menus", "permissions"], PagesIn(config));
        Assert.All(config.GetProperty("items").EnumerateArray(), item =>
        {
            // Dashboard, Menus and Permissions can repair a broken menu, so they stay; Users and Channels may be hidden or deleted.
            var repairsTheMenu = item.GetProperty("pageKey").GetString() is "dashboard" or "menus" or "permissions";
            Assert.Equal(repairsTheMenu, item.GetProperty("isLocked").GetBoolean());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("requiredPermission").ValueKind);
        });
    }

    // --- What a menu edit does to the sidebar people see --------------------------------------

    [Fact]
    public async Task HidingAnItem_RemovesItFromTheMembersMenuAtOnce_AndShowingBringsItBack()
    {
        var member = await Host.RegisterAsync(businessModel: "company");
        var workspaces = IdOf(await ConfigAsync("member"), "workspaces");
        Assert.Equal(MenuLabels("Dashboard", "Workspaces", "Platform", "Team"), await MemberLabelsAsync(member.AccessToken));

        AssertStatus(HttpStatusCode.OK, await UpdateAsync("member", workspaces, "Workspaces", "folder", visible: false));
        Assert.Equal(MenuLabels("Dashboard", "Platform", "Team"), await MemberLabelsAsync(member.AccessToken));

        // The configuration still lists it, so the Site admin can show it again.
        Assert.Equal(MenuPages("dashboard", "workspaces", "platforms", "team"), PagesIn(await ConfigAsync("member")));

        AssertStatus(HttpStatusCode.OK, await UpdateAsync("member", workspaces, "Workspaces", "folder", visible: true));
        Assert.Equal(MenuLabels("Dashboard", "Workspaces", "Platform", "Team"), await MemberLabelsAsync(member.AccessToken));
    }

    [Fact]
    public async Task RenamingAnItemAndChangingItsIcon_ShowInTheMembersMenu_WithTheLabelTrimmed()
    {
        var member = await Host.RegisterAsync(businessModel: "agency");
        var workspaces = IdOf(await ConfigAsync("member"), "workspaces");

        var response = await UpdateAsync("member", workspaces, "  Client spaces  ", "users", visible: true);

        AssertStatus(HttpStatusCode.OK, response);
        var updated = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal("Client spaces", updated.GetProperty("label").GetString());
        Assert.Equal("users", updated.GetProperty("icon").GetString());
        var menu = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/menu", member.AccessToken));
        Assert.Equal(MenuLabels("Dashboard", "Client spaces", "Platform", "Team"), LabelsIn(menu));
        var workspacesItem = menu.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("path").GetString() == "/workspaces");
        Assert.Equal("users", workspacesItem.GetProperty("icon").GetString());
    }

    [Fact]
    public async Task DeletingAnItem_RemovesIt_ThePageBecomesAddable_AndAddingItAgainPutsItLast()
    {
        var member = await Host.RegisterAsync(businessModel: "company");
        var workspaces = IdOf(await ConfigAsync("member"), "workspaces");

        AssertStatus(HttpStatusCode.NoContent, await Host.DeleteAsync($"/v1/site/menus/member/items/{workspaces}", await SiteAdminTokenAsync()));

        Assert.Equal(MenuLabels("Dashboard", "Platform", "Team"), await MemberLabelsAsync(member.AccessToken));
        var afterDelete = await ConfigAsync("member");
        var available = Assert.Single(afterDelete.GetProperty("availablePages").EnumerateArray());
        Assert.Equal("workspaces", available.GetProperty("key").GetString());
        Assert.Equal("/workspaces", available.GetProperty("path").GetString());
        Assert.Equal(PermissionKeys.WorkspacesView, available.GetProperty("requiredPermission").GetString());

        var added = await Host.PostAsync(
            "/v1/site/menus/member/items", new { pageKey = "workspaces", label = "  Spaces ", icon = "users" }, await SiteAdminTokenAsync());

        AssertStatus(HttpStatusCode.Created, added);
        var item = await ApiTestHost.ReadJsonAsync(added);
        Assert.Equal("Spaces", item.GetProperty("label").GetString());
        Assert.True(item.GetProperty("isVisible").GetBoolean());
        Assert.False(item.GetProperty("isLocked").GetBoolean());
        Assert.Equal(MenuLabels("Dashboard", "Platform", "Team", "Spaces"), await MemberLabelsAsync(member.AccessToken)); // last, after Team
        Assert.Empty((await ConfigAsync("member")).GetProperty("availablePages").EnumerateArray());
    }

    [Fact]
    public async Task AnItemAddedHidden_IsInTheConfiguration_ButNotInTheMembersMenu()
    {
        var member = await Host.RegisterAsync(businessModel: "company");
        var workspaces = IdOf(await ConfigAsync("member"), "workspaces");
        await Host.DeleteAsync($"/v1/site/menus/member/items/{workspaces}", await SiteAdminTokenAsync());

        var added = await Host.PostAsync(
            "/v1/site/menus/member/items", new { pageKey = "workspaces", label = "Spaces", icon = "list", isVisible = false }, await SiteAdminTokenAsync());

        AssertStatus(HttpStatusCode.Created, added);
        Assert.False((await ApiTestHost.ReadJsonAsync(added)).GetProperty("isVisible").GetBoolean());
        Assert.Equal(MenuLabels("Dashboard", "Platform", "Team"), await MemberLabelsAsync(member.AccessToken));
    }

    [Fact]
    public async Task Reordering_ChangesTheMembersMenu_AndRewritesThePositions()
    {
        var member = await Host.RegisterAsync(businessModel: "company");
        var config = await ConfigAsync("member");
        string[] inOrder = MenuPages("dashboard", "workspaces", "platforms", "team");
        string[] reversed = [.. inOrder.Reverse()];
        var ids = reversed.Select(page => IdOf(config, page)).ToArray();

        var response = await Host.PutAsync("/v1/site/menus/member/order", new { itemIds = ids }, await SiteAdminTokenAsync());

        AssertStatus(HttpStatusCode.OK, response);
        var reordered = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal(reversed, PagesIn(reordered));
        Assert.Equal([10, 20, 30, 40, 50, 60, 70, 80], reordered.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("sortOrder").GetInt32()));
        Assert.Equal(["Team", "Platform", "Workspaces", "Calendar", "AI Studio", "Media Library", "Content Studio", "Dashboard"], await MemberLabelsAsync(member.AccessToken));
    }

    [Fact]
    public async Task AListThatIsNotExactlyTheMenu_IsAConflict_AndChangesNothing()
    {
        var config = await ConfigAsync("member");
        var token = await SiteAdminTokenAsync();

        var everyItem = PagesIn(config).Select(page => IdOf(config, page)).ToArray();
        Guid[][] notTheMenu =
        [
            [everyItem[0]],                                    // all but one missing
            [.. everyItem.Skip(1)],                            // one missing
            [.. everyItem, Guid.NewGuid()],                    // one extra
            [.. everyItem.Skip(1), Guid.NewGuid()]             // one unknown in place of one
        ];

        foreach (var ids in notTheMenu)
        {
            var response = await Host.PutAsync("/v1/site/menus/member/order", new { itemIds = ids }, token);

            AssertStatus(HttpStatusCode.Conflict, response);
            Assert.Equal("menu_changed", await CodeOfAsync(response));
        }

        Assert.Equal(MenuPages("dashboard", "workspaces", "platforms", "team"), PagesIn(await ConfigAsync("member")));
    }

    [Fact]
    public async Task TheMenusAreIndependent_AnIdOfTheOtherMenuIsNotAnItemOfThisOne()
    {
        var adminMenus = IdOf(await ConfigAsync("admin"), "menus");
        var memberConfig = await ConfigAsync("member");
        var token = await SiteAdminTokenAsync();

        var reorder = await Host.PutAsync(
            "/v1/site/menus/member/order", new { itemIds = new[] { IdOf(memberConfig, "dashboard"), adminMenus } }, token);
        var rename = await UpdateAsync("member", adminMenus, "Hijacked", "home", visible: true);

        AssertStatus(HttpStatusCode.Conflict, reorder);
        AssertStatus(HttpStatusCode.NotFound, rename);
        Assert.Equal("menu_item_not_found", await CodeOfAsync(rename));
    }

    // --- Locked items, conflicts and missing things --------------------------------------------

    [Fact]
    public async Task ALockedItem_CanBeRenamedAndMoved_ButNeverHiddenOrDeleted()
    {
        var member = await Host.RegisterAsync(businessModel: "company");
        var dashboard = IdOf(await ConfigAsync("member"), "dashboard");
        var token = await SiteAdminTokenAsync();

        var hide = await UpdateAsync("member", dashboard, "Dashboard", "home", visible: false);
        var delete = await Host.DeleteAsync($"/v1/site/menus/member/items/{dashboard}", token);
        var rename = await UpdateAsync("member", dashboard, "Home", "chart", visible: true);

        AssertStatus(HttpStatusCode.Conflict, hide);
        Assert.Equal("menu_item_locked", await CodeOfAsync(hide));
        AssertStatus(HttpStatusCode.Conflict, delete);
        Assert.Equal("menu_item_locked", await CodeOfAsync(delete));
        AssertStatus(HttpStatusCode.OK, rename);
        Assert.Equal(MenuLabels("Home", "Workspaces", "Platform", "Team"), await MemberLabelsAsync(member.AccessToken));
    }

    [Theory]
    [InlineData("dashboard")]
    [InlineData("menus")]
    [InlineData("permissions")]
    public async Task EveryRepairItemOfTheSiteAdminsOwnMenu_IsLocked_SoTheyCannotLockThemselvesOut(string page)
    {
        var item = IdOf(await ConfigAsync("admin"), page);
        var token = await SiteAdminTokenAsync();

        AssertStatus(HttpStatusCode.Conflict, await UpdateAsync("admin", item, "Gone", "home", visible: false));
        AssertStatus(HttpStatusCode.Conflict, await Host.DeleteAsync($"/v1/site/menus/admin/items/{item}", token));
        Assert.Equal(["dashboard", "users", "channels", "menus", "permissions"], PagesIn(await ConfigAsync("admin")));
    }

    [Theory]
    [InlineData("users")]
    [InlineData("channels")]
    public async Task TheUsersAndChannelsItems_CanBeHidden_ThenDeleted_AndAreThenOfferedAgainAsAvailablePages(string page)
    {
        var token = await SiteAdminTokenAsync();
        var item = IdOf(await ConfigAsync("admin"), page);

        AssertStatus(HttpStatusCode.OK, await UpdateAsync("admin", item, "Hidden for now", "list", visible: false));
        var sidebar = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/site/menu", token));
        Assert.DoesNotContain("Hidden for now", LabelsIn(sidebar));

        AssertStatus(HttpStatusCode.NoContent, await Host.DeleteAsync($"/v1/site/menus/admin/items/{item}", token));
        var config = await ConfigAsync("admin");
        Assert.DoesNotContain(page, PagesIn(config));
        Assert.Contains(page, config.GetProperty("availablePages").EnumerateArray().Select(p => p.GetProperty("key").GetString()));
    }

    [Fact]
    public async Task TheSiteAdminSidebar_FollowsARename_OfAnAdminPage()
    {
        var permissions = IdOf(await ConfigAsync("admin"), "permissions");

        AssertStatus(HttpStatusCode.OK, await UpdateAsync("admin", permissions, "Access", "shield", visible: true));

        var menu = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/site/menu", await SiteAdminTokenAsync()));
        Assert.Equal(["Dashboard", "Users", "Channels", "Menus", "Access"], LabelsIn(menu));
    }

    [Fact]
    public async Task AddingAPageThatIsAlreadyThere_IsAConflict()
    {
        var response = await Host.PostAsync(
            "/v1/site/menus/member/items", new { pageKey = "dashboard", label = "Again", icon = "home" }, await SiteAdminTokenAsync());

        AssertStatus(HttpStatusCode.Conflict, response);
        Assert.Equal("page_already_in_menu", await CodeOfAsync(response));
    }

    [Theory]
    [InlineData("member", "nope")]
    [InlineData("member", "menus")] // a page of the admin app cannot go in the member menu
    [InlineData("admin", "workspaces")] // and the other way round
    public async Task AddingAPageThatIsNotInThisMenusApp_IsNotFound(string menu, string pageKey)
    {
        var response = await Host.PostAsync(
            $"/v1/site/menus/{menu}/items", new { pageKey, label = "Somewhere", icon = "home" }, await SiteAdminTokenAsync());

        AssertStatus(HttpStatusCode.NotFound, response);
        Assert.Equal("page_not_found", await CodeOfAsync(response));
    }

    [Fact]
    public async Task ChangingOrDeletingAnItemThatDoesNotExist_IsNotFound()
    {
        var missing = Guid.NewGuid();

        var update = await UpdateAsync("member", missing, "x", "home", visible: true);
        var delete = await Host.DeleteAsync($"/v1/site/menus/member/items/{missing}", await SiteAdminTokenAsync());

        AssertStatus(HttpStatusCode.NotFound, update);
        AssertStatus(HttpStatusCode.NotFound, delete);
        Assert.Equal("menu_item_not_found", await CodeOfAsync(delete));
    }

    // --- The permission editor ----------------------------------------------------------------

    [Fact]
    public async Task TheMatrix_HoldsEveryPermissionForEverySlot_AsSeeded()
    {
        var response = await Host.GetAsync("/v1/site/permissions", await SiteAdminTokenAsync());

        AssertStatus(HttpStatusCode.OK, response);
        var matrix = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal(8, matrix.GetProperty("slots").GetArrayLength());
        var permissions = matrix.GetProperty("permissions").EnumerateArray().ToList();
        Assert.Equal(PermissionKeys.All.Order(StringComparer.Ordinal), permissions.Select(p => p.GetProperty("key").GetString()!));

        foreach (var permission in permissions)
        {
            var key = permission.GetProperty("key").GetString()!;
            Assert.Equal(PermissionKeys.IsProtected(key), permission.GetProperty("isProtected").GetBoolean());
            var grants = permission.GetProperty("grants").EnumerateArray().ToList();
            Assert.Equal(8, grants.Count);
            foreach (var grant in grants)
            {
                RolePermissions.TryParse(grant.GetProperty("role").GetString(), out var role);
                BusinessModelRules.TryParse(grant.GetProperty("businessModel").GetString(), out var model);
                Assert.True(
                    role.GrantedByDefault(model, key) == grant.GetProperty("granted").GetBoolean(),
                    $"{grant.GetProperty("businessModel").GetString()}/{role.ToDb()} {key}");
            }
        }
    }

    [Fact]
    public async Task SettingAGrant_ChangesIt_RaisesTheVersionOnce_AndTheMatrixShowsIt()
    {
        var token = await SiteAdminTokenAsync();
        var start = await OwnerQueryAsync<long>("SELECT version FROM permission_state");

        var response = await Host.PutAsync(
            $"/v1/site/permissions/{PermissionKeys.WorkspacesCreate}/grants",
            new { businessModel = "agency", role = "organizationadmin", granted = false }, token);

        AssertStatus(HttpStatusCode.OK, response);
        var body = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal(PermissionKeys.WorkspacesCreate, body.GetProperty("permissionKey").GetString());
        Assert.False(body.GetProperty("granted").GetBoolean());
        Assert.Equal(start + 1, await OwnerQueryAsync<long>("SELECT version FROM permission_state"));
        Assert.False(await OwnerQueryAsync<bool>(
            "SELECT is_granted FROM role_permissions WHERE business_model = 'agency' AND role = 'organizationadmin' AND permission_key = 'workspaces.create'"));

        // The same again changes nothing, and so does not send everybody back to sign in.
        AssertStatus(HttpStatusCode.OK, await Host.PutAsync(
            $"/v1/site/permissions/{PermissionKeys.WorkspacesCreate}/grants",
            new { businessModel = "agency", role = "organizationadmin", granted = false }, token));
        Assert.Equal(start + 1, await OwnerQueryAsync<long>("SELECT version FROM permission_state"));

        var matrix = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/site/permissions", token));
        var create = matrix.GetProperty("permissions").EnumerateArray().Single(p => p.GetProperty("key").GetString() == PermissionKeys.WorkspacesCreate);
        var agencyAdmin = create.GetProperty("grants").EnumerateArray().Single(g =>
            g.GetProperty("businessModel").GetString() == "agency" && g.GetProperty("role").GetString() == "organizationadmin");
        Assert.False(agencyAdmin.GetProperty("granted").GetBoolean());
    }

    [Fact]
    public async Task ARevokeOfTheProtectedPermission_IsAConflict_AndChangesNothing()
    {
        var start = await OwnerQueryAsync<long>("SELECT version FROM permission_state");

        var response = await Host.PutAsync(
            $"/v1/site/permissions/{PermissionKeys.DashboardView}/grants",
            new { businessModel = "agency", role = "clientmanager", granted = false }, await SiteAdminTokenAsync());

        AssertStatus(HttpStatusCode.Conflict, response);
        Assert.Equal("permission_protected", await CodeOfAsync(response));
        Assert.Equal(start, await OwnerQueryAsync<long>("SELECT version FROM permission_state"));
        Assert.Equal(8, await OwnerQueryAsync<long>("SELECT count(*) FROM role_permissions WHERE permission_key = 'dashboard.view' AND is_granted"));
    }

    // --- A change reaches the people it concerns ----------------------------------------------

    [Fact]
    public async Task RevokingWorkspacesView_TakesTheItemAndTheEndpointFromThoseWhoLoseIt_AfterTheySignInAgain()
    {
        var companyAdmin = await Host.RegisterAsync(businessModel: "company");
        var agencyAdmin = await Host.RegisterAsync(businessModel: "agency");
        var token = await SiteAdminTokenAsync();
        Assert.Equal(MenuLabels("Dashboard", "Workspaces", "Platform", "Team"), await MemberLabelsAsync(companyAdmin.AccessToken));
        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/workspaces", companyAdmin.AccessToken));

        AssertStatus(HttpStatusCode.OK, await Host.PutAsync(
            $"/v1/site/permissions/{PermissionKeys.WorkspacesView}/grants",
            new { businessModel = "company", role = "organizationadmin", granted = false }, token));

        // Everybody's token is out of date now, whoever the change concerned: they are told to sign in again.
        AssertStatus(HttpStatusCode.Unauthorized, await Host.GetAsync("/v1/menu", companyAdmin.AccessToken));
        AssertStatus(HttpStatusCode.Unauthorized, await Host.GetAsync("/v1/menu", agencyAdmin.AccessToken));

        var companyFresh = await SignInAgainAsync(companyAdmin);
        var agencyFresh = await SignInAgainAsync(agencyAdmin);

        // The company admin lost it: not in the menu, not in the token, and the endpoint refuses.
        Assert.Equal(MenuLabels("Dashboard", "Platform", "Team"), await MemberLabelsAsync(companyFresh));
        Assert.DoesNotContain(PermissionKeys.WorkspacesView, new JsonWebToken(companyFresh).GetPayloadValue<string[]>(ClaimNames.Permission));
        AssertStatus(HttpStatusCode.Forbidden, await Host.GetAsync("/v1/workspaces", companyFresh));

        // The agency admin did not.
        Assert.Equal(MenuLabels("Dashboard", "Workspaces", "Platform", "Team"), await MemberLabelsAsync(agencyFresh));
        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/workspaces", agencyFresh));
    }

    [Fact]
    public async Task GrantingItBack_RestoresTheItemAfterTheySignInAgain()
    {
        var admin = await Host.RegisterAsync(businessModel: "company");
        var token = await SiteAdminTokenAsync();
        var revoke = new { businessModel = "company", role = "organizationadmin", granted = false };
        var grant = new { businessModel = "company", role = "organizationadmin", granted = true };
        var url = $"/v1/site/permissions/{PermissionKeys.WorkspacesView}/grants";

        await Host.PutAsync(url, revoke, token);
        var without = await SignInAgainAsync(admin);
        Assert.Equal(MenuLabels("Dashboard", "Platform", "Team"), await MemberLabelsAsync(without));

        await Host.PutAsync(url, grant, token);
        AssertStatus(HttpStatusCode.Unauthorized, await Host.GetAsync("/v1/menu", without));
        var with = await SignInAgainAsync(admin);

        Assert.Equal(MenuLabels("Dashboard", "Workspaces", "Platform", "Team"), await MemberLabelsAsync(with));
        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/workspaces", with));
    }

    [Fact]
    public async Task GrantingWorkspacesCreate_ToAContentWriter_LetsThemCreateAfterTheySignInAgain()
    {
        // A content writer is a member of a company; sign them in for real so their token comes from the issuer.
        var founder = await Host.RegisterAsync(businessModel: "company");
        var writerEmail = $"writer-{Guid.NewGuid():N}@example.com";
        await using (var owner = await Host.Database.OpenOwnerAsync())
        {
            var hash = new Pbkdf2PasswordHasher(iterations: 1_000).Hash(ApiTestHost.DefaultPassword);
            var writerId = await owner.QuerySingleAsync<Guid>(
                "INSERT INTO users (email, password_hash, full_name) VALUES (@writerEmail, @hash, 'Writer') RETURNING id", new { writerEmail, hash });
            await DbTestSupport.InsertMemberAsync(owner, founder.AgencyId, writerId, "contentwriter");
        }

        var writer = await Host.LoginAsync(writerEmail);
        AssertStatus(HttpStatusCode.Forbidden, await Host.PostAsync("/v1/workspaces", new { name = "Before" }, writer.AccessToken));

        AssertStatus(HttpStatusCode.OK, await Host.PutAsync(
            $"/v1/site/permissions/{PermissionKeys.WorkspacesCreate}/grants",
            new { businessModel = "company", role = "contentwriter", granted = true }, await SiteAdminTokenAsync()));

        AssertStatus(HttpStatusCode.Unauthorized, await Host.PostAsync("/v1/workspaces", new { name = "Stale" }, writer.AccessToken));
        var fresh = await SignInAgainAsync(writer);
        AssertStatus(HttpStatusCode.Created, await Host.PostAsync("/v1/workspaces", new { name = "After" }, fresh));
    }

    // --- A site admin who was demoted ---------------------------------------------------------

    [Fact]
    public async Task ADemotedSiteAdmin_IsRefusedAtOnce_ByEveryConfigurationRoute_WhileTheirTokenIsStillValid()
    {
        var token = await SiteAdminTokenAsync();
        var workspaces = IdOf(await ConfigAsync("member"), "workspaces");
        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/site/permissions", token));

        await OwnerExecuteAsync("DELETE FROM site_admins WHERE user_id = @id", new { id = _siteAdminId });

        var responses = new[]
        {
            await Host.GetAsync("/v1/site/menus/member", token),
            await Host.PostAsync("/v1/site/menus/member/items", new { pageKey = "dashboard", label = "x", icon = "home" }, token),
            await UpdateAsync("member", workspaces, "x", "home", visible: true),
            await Host.DeleteAsync($"/v1/site/menus/member/items/{workspaces}", token),
            await Host.PutAsync("/v1/site/menus/member/order", new { itemIds = new[] { workspaces } }, token),
            await Host.GetAsync("/v1/site/permissions", token),
            await Host.PutAsync(
                $"/v1/site/permissions/{PermissionKeys.DataRead}/grants", new { businessModel = "company", role = "organizationadmin", granted = false }, token)
        };

        foreach (var response in responses)
        {
            AssertStatus(HttpStatusCode.Forbidden, response);
            Assert.Equal("not_site_admin", await CodeOfAsync(response));
        }

        // And nothing was changed on the way.
        Assert.Equal(3, await OwnerQueryAsync<long>("SELECT count(*) FROM menu_items WHERE menu_key = 'member'"));
        Assert.True(await OwnerQueryAsync<bool>(
            "SELECT is_granted FROM role_permissions WHERE business_model = 'company' AND role = 'organizationadmin' AND permission_key = 'data.read'"));
    }

    [Fact]
    public async Task AnOrdinaryUser_WhoIsNotInSiteAdmins_IsRefusedEvenWithASiteAdminShapedToken()
    {
        // A signed token of the right shape for a user who was never a site admin: the database says no.
        await using var owner = await Host.Database.OpenOwnerAsync();
        var userId = await DbTestSupport.InsertUserAsync(owner, $"{Guid.NewGuid():N}@example.com");
        var token = Host.Factory.CreateSiteAdminAccessToken(userId);

        var response = await Host.GetAsync("/v1/site/menus/member", token);

        AssertStatus(HttpStatusCode.Forbidden, response);
        Assert.Equal("not_site_admin", await CodeOfAsync(response));
    }
}
