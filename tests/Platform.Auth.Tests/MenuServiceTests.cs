using NSubstitute;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Services;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;

namespace Platform.Auth.Tests;

public sealed class MenuServiceTests
{
    private readonly IMenuCache _cache = Substitute.For<IMenuCache>();
    private readonly TenantContext _tenant = new();
    private readonly MenuService _service;

    private readonly Guid _agencyId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public MenuServiceTests()
    {
        _service = new MenuService(_cache, _tenant);
        _cache.GetAsync(Arg.Any<CancellationToken>()).Returns(new MenuSnapshot(
        [
            Item(MenuKeys.Member, "dashboard", "Dashboard", 10, PermissionKeys.DashboardView),
            Item(MenuKeys.Member, "workspaces", "Workspaces", 20, PermissionKeys.WorkspacesView),
            Item(MenuKeys.Member, "reports", "Reports", 30, null),
            Item(MenuKeys.Member, "secret", "Secret", 40, PermissionKeys.OrganizationManage, visible: false),
            Item(MenuKeys.Admin, "dashboard", "Admin dashboard", 10, null),
            Item(MenuKeys.Admin, "menus", "Menus", 20, null),
            Item(MenuKeys.Admin, "old", "Old", 30, null, visible: false)
        ]));
    }

    private static MenuItemRecord Item(string menu, string page, string label, int order, string? permission, bool visible = true) =>
        new()
        {
            MenuKey = menu,
            Id = Guid.NewGuid(),
            PageKey = page,
            PageTitle = label,
            Path = menu == MenuKeys.Admin ? "/admin/" + page : "/" + page,
            PermissionKey = permission,
            Label = label,
            Icon = "home",
            SortOrder = order,
            IsVisible = visible
        };

    private void SignInAs(string role, params string[] permissions) => _tenant.Set(_agencyId, _userId, role, permissions);

    // --- Member menu -------------------------------------------------------------------------

    [Fact]
    public async Task TheMemberMenu_HoldsTheItemsTheTokensPermissionsAllow_InOrder()
    {
        SignInAs("organizationadmin", PermissionKeys.DashboardView, PermissionKeys.WorkspacesView);

        var menu = await _service.GetMemberMenuAsync(CancellationToken.None);

        Assert.Equal(["Dashboard", "Workspaces", "Reports"], menu.Items.Select(i => i.Label));
        Assert.Equal(["/dashboard", "/workspaces", "/reports"], menu.Items.Select(i => i.Path));
    }

    [Fact]
    public async Task TheMemberMenu_OmitsAnItemWhosePermissionTheTokenLacks()
    {
        SignInAs("organizationadmin", PermissionKeys.DashboardView);

        var menu = await _service.GetMemberMenuAsync(CancellationToken.None);

        Assert.Equal(["Dashboard", "Reports"], menu.Items.Select(i => i.Label));
    }

    [Fact]
    public async Task TheMemberMenu_DependsOnThePermissions_NotOnTheRoleName()
    {
        SignInAs("contentwriter", PermissionKeys.DashboardView, PermissionKeys.WorkspacesView);
        var writer = await _service.GetMemberMenuAsync(CancellationToken.None);
        SignInAs("organizationadmin", PermissionKeys.DashboardView, PermissionKeys.WorkspacesView);
        var admin = await _service.GetMemberMenuAsync(CancellationToken.None);

        Assert.Equal(admin.Items.Select(i => i.Label), writer.Items.Select(i => i.Label));
    }

    [Fact]
    public async Task TheMemberMenu_NeverContainsAdminItems_OrHiddenOnes()
    {
        SignInAs("organizationadmin", PermissionKeys.All.ToArray());

        var menu = await _service.GetMemberMenuAsync(CancellationToken.None);

        Assert.DoesNotContain(menu.Items, i => i.Path.StartsWith("/admin", StringComparison.Ordinal));
        Assert.DoesNotContain(menu.Items, i => i.Label == "Secret");
    }

    [Fact]
    public async Task TheMemberMenu_ReportsThePermissionsOfTheToken_Sorted_SoTheClientCanGuardItsPages()
    {
        SignInAs("organizationadmin", PermissionKeys.WorkspacesView, PermissionKeys.DashboardView);

        var menu = await _service.GetMemberMenuAsync(CancellationToken.None);

        Assert.Equal([PermissionKeys.DashboardView, PermissionKeys.WorkspacesView], menu.Permissions);
    }

    [Fact]
    public async Task TheMemberMenu_ReadsOnlyFromTheCache()
    {
        SignInAs("organizationadmin", PermissionKeys.DashboardView);

        await _service.GetMemberMenuAsync(CancellationToken.None);
        await _service.GetMemberMenuAsync(CancellationToken.None);

        await _cache.Received(2).GetAsync(Arg.Any<CancellationToken>());
        _cache.DidNotReceive().Invalidate();
    }

    [Fact]
    public async Task TheMemberMenu_WithoutAnAgency_IsUnauthorized()
    {
        _tenant.Set(null, _userId, "organizationadmin", [PermissionKeys.DashboardView]);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.GetMemberMenuAsync(CancellationToken.None));
        await _cache.DidNotReceiveWithAnyArgs().GetAsync(default);
    }

    [Fact]
    public async Task TheMemberMenu_WithoutAUser_IsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.GetMemberMenuAsync(CancellationToken.None));
    }

    // --- Site admin menu ---------------------------------------------------------------------

    [Fact]
    public async Task TheSiteAdminMenu_HoldsTheVisibleAdminItems_AndNoPermissions()
    {
        _tenant.Set(null, _userId, RoleNames.SiteAdmin);

        var menu = await _service.GetSiteAdminMenuAsync(CancellationToken.None);

        Assert.Equal(["Admin dashboard", "Menus"], menu.Items.Select(i => i.Label));
        Assert.Empty(menu.Permissions);
    }

    [Theory]
    [InlineData("organizationadmin")]
    [InlineData("contentwriter")]
    [InlineData("")]
    [InlineData(null)]
    public async Task TheSiteAdminMenu_IsForbidden_ToAnyoneWhoIsNotASiteAdmin(string? role)
    {
        _tenant.Set(_agencyId, _userId, role, PermissionKeys.All);

        var exception = await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetSiteAdminMenuAsync(CancellationToken.None));

        Assert.Equal("not_site_admin", exception.Code);
        await _cache.DidNotReceiveWithAnyArgs().GetAsync(default);
    }

    [Fact]
    public async Task TheSiteAdminMenu_WithoutAUser_IsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.GetSiteAdminMenuAsync(CancellationToken.None));
    }
}
