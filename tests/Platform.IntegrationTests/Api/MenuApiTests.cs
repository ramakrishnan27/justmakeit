using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Repositories;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// The member menu against a real database, and the proof that it and the permission checks are served
/// from memory: the repositories are wrapped in counters, and many requests must cost a handful of reads, not one each.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Database")]
public sealed class MenuApiTests(PostgresFixture postgres) : AuthApiTestBase(postgres)
{
    private readonly ReadCounter _reads = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(_reads);
        services.AddScoped<IMenuRepository>(provider =>
            new CountingMenuRepository(ActivatorUtilities.CreateInstance<MenuRepository>(provider), _reads));
        services.AddScoped<IPermissionRepository>(provider =>
            new CountingPermissionRepository(ActivatorUtilities.CreateInstance<PermissionRepository>(provider), _reads));
    }

    private static string[] LabelsIn(JsonElement menu) =>
        [.. menu.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("label").GetString()!)];

    [Theory]
    [InlineData("company", "/dashboard,/content-studio,/media-library,/ai-studio,/calendar,/workspaces,/platforms,/team")]
    [InlineData("agency", "/dashboard,/content-studio,/media-library,/ai-studio,/calendar,/workspaces,/platforms,/team")]
    [InlineData("creator", "/dashboard,/content-studio,/media-library,/ai-studio,/calendar,/workspaces,/platforms")] // a creator works alone: no team permission, so no Team page
    public async Task TheFounder_SeesTheDashboardTheStudioPagesAndTheWorkspacesPage_AndTheTeamPageWhenTheyHoldThePermission(string businessModel, string paths)
    {
        var founder = await Host.RegisterAsync(businessModel: businessModel);

        var response = await Host.GetAsync("/v1/menu", founder.AccessToken);

        AssertStatus(HttpStatusCode.OK, response);
        var menu = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal(paths.Split(','), menu.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("path").GetString()));
        Assert.Equal(
            paths.Contains("/team"),
            menu.GetProperty("permissions").EnumerateArray().Any(p => p.GetString() == PermissionKeys.MembersManage));
    }

    [Fact]
    public async Task TheTeamPage_IsInTheMenuOfAnOrganizationAdminOnly_NotOfTheOtherRolesOfThatOrganization()
    {
        // Every other role is a content writer or a marketing manager signed up through an invitation.
        var admin = await Host.RegisterAsync(businessModel: "company");
        var (token, _) = await InviteAsync(admin, "writer@example.com", "contentwriter");
        var accepted = await Host.PostAsync("/v1/auth/invitations/accept", new { token, fullName = "Writer", password = "writer password 123" });
        var writerCookie = ApiTestHost.MemberCookieValue(accepted)!;

        var adminMenu = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/menu", admin.AccessToken));
        var writerMenu = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/menu", writerCookie));

        Assert.Equal(["Dashboard", "Content Studio", "Media Library", "AI Studio", "Calendar", "Workspaces", "Platform", "Team"], LabelsIn(adminMenu));
        Assert.Equal(["Dashboard", "Content Studio", "Media Library", "AI Studio", "Calendar", "Workspaces", "Platform"], LabelsIn(writerMenu));
    }

    private async Task<(string Token, Guid InvitationId)> InviteAsync(Session admin, string email, string role)
    {
        var response = await Host.PostAsync("/v1/members/invitations", new { fullName = "Test Person", email, role }, admin.AccessToken);
        AssertStatus(HttpStatusCode.Created, response);
        var body = await ApiTestHost.ReadJsonAsync(response);
        return (body.GetProperty("token").GetString()!, body.GetProperty("invitation").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task TheMenusPermissionList_IsTheOneInTheToken()
    {
        var creator = await Host.RegisterAsync(businessModel: "creator");

        var menu = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/menu", creator.AccessToken));

        var fromMenu = menu.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToArray();
        var fromToken = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(creator.AccessToken)
            .GetPayloadValue<string[]>(ClaimNames.Permission).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(fromToken, fromMenu);
    }

    [Fact]
    public async Task ManyRequests_ReadTheMenuAndThePermissionsFromMemory_NotFromTheDatabase()
    {
        var admin = await Host.RegisterAsync(businessModel: "company");
        var token = admin.AccessToken;

        for (var i = 0; i < 25; i++)
        {
            AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/menu", token));
            AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/workspaces", token));
            AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/auth/me", token));
        }

        // 75 requests, each checked for its version and its permission, 25 of them a menu: a load or two, not 75.
        Assert.InRange(_reads.MenuLoads, 1, 2);
        Assert.InRange(_reads.PermissionLoads, 1, 2);
    }

    [Fact]
    public async Task TheMenu_IsStillServed_WhileTheCacheIsFresh_EvenIfTheMenuTableCouldNotBeRead()
    {
        var admin = await Host.RegisterAsync(businessModel: "company");
        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/menu", admin.AccessToken));

        // Break the read path: the table can no longer be reached by the application role.
        await OwnerExecuteAsync("REVOKE ALL ON FUNCTION menu_snapshot() FROM app_user");
        try
        {
            var response = await Host.GetAsync("/v1/menu", admin.AccessToken);

            AssertStatus(HttpStatusCode.OK, response);
            Assert.Equal(["Dashboard", "Content Studio", "Media Library", "AI Studio", "Calendar", "Workspaces", "Platform", "Team"], LabelsIn(await ApiTestHost.ReadJsonAsync(response)));
        }
        finally
        {
            await OwnerExecuteAsync("GRANT EXECUTE ON FUNCTION menu_snapshot() TO app_user");
        }
    }

    [Fact]
    public async Task WhenTheMenuCannotBeReadAtAll_TheRequestFails_ItIsNeverAnEmptyOrMadeUpMenu()
    {
        // A host that has never loaded the menu, with the read path broken.
        var admin = await Host.RegisterAsync(businessModel: "company");
        await OwnerExecuteAsync("REVOKE ALL ON FUNCTION menu_snapshot() FROM app_user");
        try
        {
            var response = await Host.GetAsync("/v1/menu", admin.AccessToken);

            AssertStatus(HttpStatusCode.InternalServerError, response);
            Assert.Equal("server_error", await CodeOfAsync(response));
        }
        finally
        {
            await OwnerExecuteAsync("GRANT EXECUTE ON FUNCTION menu_snapshot() TO app_user");
        }
    }

    [Fact]
    public async Task EveryPageAMenuCanLinkTo_IsAnAddressThatServesTheAppShell_SoReloadingItWorks()
    {
        await using var owner = await Host.Database.OpenOwnerAsync();
        var paths = (await owner.QueryAsync<string>("SELECT path FROM menu_pages")).ToList();
        Assert.NotEmpty(paths);

        foreach (var path in paths)
        {
            var response = await Host.GetAsync(path);

            AssertStatus(HttpStatusCode.OK, response);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("<div id=\"app\">", await response.Content.ReadAsStringAsync());
        }
    }

    private sealed class ReadCounter
    {
        private int _menuLoads;
        private int _permissionLoads;

        public int MenuLoads => Volatile.Read(ref _menuLoads);
        public int PermissionLoads => Volatile.Read(ref _permissionLoads);

        public void CountMenu() => Interlocked.Increment(ref _menuLoads);
        public void CountPermissions() => Interlocked.Increment(ref _permissionLoads);
    }

    private sealed class CountingMenuRepository(IMenuRepository inner, ReadCounter counter) : IMenuRepository
    {
        public Task<MenuSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken)
        {
            counter.CountMenu();
            return inner.LoadSnapshotAsync(cancellationToken);
        }

        public Task<MenuConfig> GetConfigAsync(Guid userId, string menuKey, CancellationToken cancellationToken) =>
            inner.GetConfigAsync(userId, menuKey, cancellationToken);

        public Task<MenuItemRecord?> FindItemAsync(Guid userId, Guid itemId, CancellationToken cancellationToken) =>
            inner.FindItemAsync(userId, itemId, cancellationToken);

        public Task<Guid> CreateItemAsync(
            Guid userId, string menuKey, string pageKey, string label, string icon, bool isVisible, CancellationToken cancellationToken) =>
            inner.CreateItemAsync(userId, menuKey, pageKey, label, icon, isVisible, cancellationToken);

        public Task<bool> UpdateItemAsync(
            Guid userId, Guid itemId, string label, string icon, bool? isVisible, CancellationToken cancellationToken) =>
            inner.UpdateItemAsync(userId, itemId, label, icon, isVisible, cancellationToken);

        public Task<bool> DeleteItemAsync(Guid userId, Guid itemId, CancellationToken cancellationToken) =>
            inner.DeleteItemAsync(userId, itemId, cancellationToken);

        public Task ReorderAsync(Guid userId, string menuKey, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken) =>
            inner.ReorderAsync(userId, menuKey, itemIds, cancellationToken);
    }

    private sealed class CountingPermissionRepository(IPermissionRepository inner, ReadCounter counter) : IPermissionRepository
    {
        public Task<PermissionSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken)
        {
            counter.CountPermissions();
            return inner.LoadSnapshotAsync(cancellationToken);
        }

        public Task<PermissionCatalog> GetCatalogAsync(Guid userId, CancellationToken cancellationToken) =>
            inner.GetCatalogAsync(userId, cancellationToken);

        public Task<bool> SetGrantAsync(
            Guid userId, string businessModel, string role, string permissionKey, bool granted, CancellationToken cancellationToken) =>
            inner.SetGrantAsync(userId, businessModel, role, permissionKey, granted, cancellationToken);
    }
}
