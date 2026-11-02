using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Services;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;

namespace Platform.Auth.Tests;

public sealed class MenuAdminServiceTests
{
    private readonly IMenuRepository _menus = Substitute.For<IMenuRepository>();
    private readonly IMenuCache _cache = Substitute.For<IMenuCache>();
    private readonly TenantContext _tenant = new();
    private readonly MenuAdminService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _itemId = Guid.NewGuid();

    public MenuAdminServiceTests()
    {
        _service = new MenuAdminService(_menus, _cache, _tenant, NullLogger<MenuAdminService>.Instance);
        _tenant.Set(null, _userId, RoleNames.SiteAdmin);
        _menus.FindItemAsync(_userId, _itemId, Arg.Any<CancellationToken>()).Returns(Item(MenuKeys.Member, "Workspaces"));
    }

    private MenuItemRecord Item(string menu, string label, bool locked = false) =>
        new()
        {
            MenuKey = menu,
            Id = _itemId,
            PageKey = "workspaces",
            PageTitle = "Workspaces",
            Path = "/workspaces",
            PermissionKey = PermissionKeys.WorkspacesView,
            Label = label,
            Icon = "folder",
            SortOrder = 20,
            IsVisible = true,
            IsLocked = locked
        };

    private static CreateMenuItemRequest ValidCreate() => new("workspaces", "  Our workspaces  ", "folder", null);

    private static UpdateMenuItemRequest ValidUpdate() => new("  Spaces ", "users", false);

    // --- Who may ------------------------------------------------------------------------------

    [Theory]
    [InlineData("organizationadmin")]
    [InlineData("contentwriter")]
    [InlineData("")]
    [InlineData(null)]
    public async Task EveryOperation_IsForbidden_ToAnyoneWhoIsNotASiteAdmin_AndTouchesNothing(string? role)
    {
        _tenant.Set(Guid.NewGuid(), _userId, role, PermissionKeys.All);

        await AssertForbidden(() => _service.GetConfigAsync(MenuKeys.Member, CancellationToken.None));
        await AssertForbidden(() => _service.CreateItemAsync(MenuKeys.Member, ValidCreate(), CancellationToken.None));
        await AssertForbidden(() => _service.UpdateItemAsync(MenuKeys.Member, _itemId, ValidUpdate(), CancellationToken.None));
        await AssertForbidden(() => _service.DeleteItemAsync(MenuKeys.Member, _itemId, CancellationToken.None));
        await AssertForbidden(() => _service.ReorderAsync(MenuKeys.Member, new ReorderMenuRequest([_itemId]), CancellationToken.None));

        // Not even a read: the repository was never asked anything.
        Assert.Empty(_menus.ReceivedCalls());
        _cache.DidNotReceive().Invalidate();
    }

    private static async Task AssertForbidden(Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<ForbiddenException>(action);
        Assert.Equal("not_site_admin", exception.Code);
    }

    [Fact]
    public async Task WithoutAUser_EveryOperationIsUnauthorized()
    {
        _tenant.Clear();

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.GetConfigAsync(MenuKeys.Member, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.DeleteItemAsync(MenuKeys.Member, _itemId, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Member")]
    [InlineData("members")]
    [InlineData("other")]
    public async Task AnUnknownMenu_IsNotFound_ForEveryOperation(string menuKey)
    {
        await AssertNotFound("menu_not_found", () => _service.GetConfigAsync(menuKey, CancellationToken.None));
        await AssertNotFound("menu_not_found", () => _service.CreateItemAsync(menuKey, ValidCreate(), CancellationToken.None));
        await AssertNotFound("menu_not_found", () => _service.UpdateItemAsync(menuKey, _itemId, ValidUpdate(), CancellationToken.None));
        await AssertNotFound("menu_not_found", () => _service.DeleteItemAsync(menuKey, _itemId, CancellationToken.None));
        await AssertNotFound("menu_not_found", () => _service.ReorderAsync(menuKey, new ReorderMenuRequest([]), CancellationToken.None));
        await _menus.DidNotReceiveWithAnyArgs().CreateItemAsync(default, default!, default!, default!, default!, default, default);
    }

    private static async Task AssertNotFound(string code, Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<NotFoundException>(action);
        Assert.Equal(code, exception.Code);
    }

    // --- Reading ------------------------------------------------------------------------------

    [Fact]
    public async Task GetConfig_ListsEveryItem_TheAddablePages_AndTheIconsToChooseFrom()
    {
        _menus.GetConfigAsync(_userId, MenuKeys.Member, Arg.Any<CancellationToken>()).Returns(new MenuConfig(
            [Item(MenuKeys.Member, "Workspaces", locked: true)],
            [new MenuPageRecord { App = MenuKeys.Member, Key = "reports", Title = "Reports", Path = "/reports", PermissionKey = PermissionKeys.DataRead }]));

        var config = await _service.GetConfigAsync(MenuKeys.Member, CancellationToken.None);

        Assert.Equal(MenuKeys.Member, config.MenuKey);
        var item = Assert.Single(config.Items);
        Assert.Equal(new MenuConfigItemResponse(_itemId, "workspaces", "Workspaces", "/workspaces", "Workspaces", "folder", 20, true, true, PermissionKeys.WorkspacesView), item);
        Assert.Equal(new MenuPageResponse("reports", "Reports", "/reports", PermissionKeys.DataRead), Assert.Single(config.AvailablePages));
        Assert.Equal(MenuIcons.All, config.Icons);
    }

    // --- Create -------------------------------------------------------------------------------

    [Fact]
    public async Task Create_TrimsTheLabel_ShowsTheItemByDefault_AndForgetsTheCache()
    {
        var newId = Guid.NewGuid();
        _menus.CreateItemAsync(_userId, MenuKeys.Member, "workspaces", "Our workspaces", "folder", true, Arg.Any<CancellationToken>()).Returns(newId);
        _menus.FindItemAsync(_userId, newId, Arg.Any<CancellationToken>()).Returns(new MenuItemRecord
        {
            MenuKey = MenuKeys.Member, Id = newId, PageKey = "workspaces", PageTitle = "Workspaces", Path = "/workspaces",
            Label = "Our workspaces", Icon = "folder", SortOrder = 30, IsVisible = true
        });

        var created = await _service.CreateItemAsync(MenuKeys.Member, ValidCreate(), CancellationToken.None);

        Assert.Equal(newId, created.Id);
        Assert.Equal("Our workspaces", created.Label);
        _cache.Received(1).Invalidate();
    }

    [Fact]
    public async Task Create_WithBadFields_IsABadRequest_ListingEachOne_AndCallsNothing()
    {
        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.CreateItemAsync(MenuKeys.Member, new CreateMenuItemRequest("Bad Key", " ", "sparkles", null), CancellationToken.None));

        Assert.Equal(["icon", "label", "pageKey"], exception.Errors!.Keys.Order());
        Assert.Empty(_menus.ReceivedCalls());
        _cache.DidNotReceive().Invalidate();
    }

    [Fact]
    public async Task Create_WhenThePageIsAlreadyThere_ExplainsIt_AndStillForgetsTheCache()
    {
        _menus.CreateItemAsync(default, default!, default!, default!, default!, default, default)
            .ThrowsAsyncForAnyArgs(new ConflictException("That page is already in the menu.", "page_already_in_menu"));

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateItemAsync(MenuKeys.Member, ValidCreate(), CancellationToken.None));

        Assert.Equal("page_already_in_menu", exception.Code);
        _cache.Received(1).Invalidate();
    }

    // --- Update -------------------------------------------------------------------------------

    [Fact]
    public async Task Update_ChangesLabelIconAndVisibility_ReturnsTheItemAsItIsNow_AndForgetsTheCache()
    {
        _menus.UpdateItemAsync(_userId, _itemId, "Spaces", "users", false, Arg.Any<CancellationToken>()).Returns(true);

        var updated = await _service.UpdateItemAsync(MenuKeys.Member, _itemId, ValidUpdate(), CancellationToken.None);

        Assert.Equal(_itemId, updated.Id);
        await _menus.Received(1).UpdateItemAsync(_userId, _itemId, "Spaces", "users", false, Arg.Any<CancellationToken>());
        _cache.Received(1).Invalidate();
    }

    [Fact]
    public async Task Update_OfAnItemThatBelongsToTheOtherMenu_IsNotFound_AndChangesNothing()
    {
        _menus.FindItemAsync(_userId, _itemId, Arg.Any<CancellationToken>()).Returns(Item(MenuKeys.Admin, "Menus"));

        await AssertNotFound("menu_item_not_found", () => _service.UpdateItemAsync(MenuKeys.Member, _itemId, ValidUpdate(), CancellationToken.None));

        await _menus.DidNotReceiveWithAnyArgs().UpdateItemAsync(default, default, default!, default!, default, default);
    }

    [Fact]
    public async Task Update_OfAnItemThatDoesNotExist_IsNotFound()
    {
        _menus.FindItemAsync(_userId, _itemId, Arg.Any<CancellationToken>()).Returns((MenuItemRecord?)null);

        await AssertNotFound("menu_item_not_found", () => _service.UpdateItemAsync(MenuKeys.Member, _itemId, ValidUpdate(), CancellationToken.None));
    }

    [Fact]
    public async Task Update_WhenTheItemWasDeletedInTheMeantime_IsNotFound_AndStillForgetsTheCache()
    {
        _menus.UpdateItemAsync(_userId, _itemId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>()).Returns(false);

        await AssertNotFound("menu_item_not_found", () => _service.UpdateItemAsync(MenuKeys.Member, _itemId, ValidUpdate(), CancellationToken.None));

        _cache.Received(1).Invalidate();
    }

    [Fact]
    public async Task Update_ThatHidesALockedItem_ReachesTheCaller_AsAConflict()
    {
        _menus.UpdateItemAsync(default, default, default!, default!, default, default)
            .ThrowsAsyncForAnyArgs(new ConflictException("locked", "menu_item_locked"));

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.UpdateItemAsync(MenuKeys.Member, _itemId, ValidUpdate(), CancellationToken.None));

        Assert.Equal("menu_item_locked", exception.Code);
    }

    [Theory]
    [InlineData(null, "users", true, "label")]
    [InlineData("Spaces", null, true, "icon")]
    [InlineData("Spaces", "users", null, "isVisible")]
    public async Task Update_WithAMissingField_IsABadRequest(string? label, string? icon, bool? visible, string field)
    {
        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.UpdateItemAsync(MenuKeys.Member, _itemId, new UpdateMenuItemRequest(label, icon, visible), CancellationToken.None));

        Assert.Contains(field, exception.Errors!.Keys);
    }

    // --- Delete -------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_RemovesTheItem_AndForgetsTheCache()
    {
        _menus.DeleteItemAsync(_userId, _itemId, Arg.Any<CancellationToken>()).Returns(true);

        await _service.DeleteItemAsync(MenuKeys.Member, _itemId, CancellationToken.None);

        await _menus.Received(1).DeleteItemAsync(_userId, _itemId, Arg.Any<CancellationToken>());
        _cache.Received(1).Invalidate();
    }

    [Fact]
    public async Task Delete_OfAnItemInTheOtherMenu_IsNotFound_AndDeletesNothing()
    {
        await AssertNotFound("menu_item_not_found", () => _service.DeleteItemAsync(MenuKeys.Admin, _itemId, CancellationToken.None));

        await _menus.DidNotReceiveWithAnyArgs().DeleteItemAsync(default, default, default);
    }

    [Fact]
    public async Task Delete_OfALockedItem_ReachesTheCaller_AsAConflict_AndTheCacheIsStillForgotten()
    {
        _menus.DeleteItemAsync(_userId, _itemId, Arg.Any<CancellationToken>()).ThrowsAsync(new ConflictException("locked", "menu_item_locked"));

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.DeleteItemAsync(MenuKeys.Member, _itemId, CancellationToken.None));

        Assert.Equal("menu_item_locked", exception.Code);
        _cache.Received(1).Invalidate();
    }

    [Fact]
    public async Task Delete_WhenAnotherAdminDeletedItFirst_IsNotFound()
    {
        _menus.DeleteItemAsync(_userId, _itemId, Arg.Any<CancellationToken>()).Returns(false);

        await AssertNotFound("menu_item_not_found", () => _service.DeleteItemAsync(MenuKeys.Member, _itemId, CancellationToken.None));
    }

    // --- Reorder ------------------------------------------------------------------------------

    [Fact]
    public async Task Reorder_PassesTheListOn_ForgetsTheCache_AndReturnsTheMenuAsItIsNow()
    {
        var second = Guid.NewGuid();
        _menus.GetConfigAsync(_userId, MenuKeys.Member, Arg.Any<CancellationToken>()).Returns(new MenuConfig([Item(MenuKeys.Member, "Workspaces")], []));

        var config = await _service.ReorderAsync(MenuKeys.Member, new ReorderMenuRequest([second, _itemId]), CancellationToken.None);

        await _menus.Received(1).ReorderAsync(
            _userId, MenuKeys.Member, Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { second, _itemId })), Arg.Any<CancellationToken>());
        _cache.Received(1).Invalidate();
        Assert.Single(config.Items);
    }

    [Fact]
    public async Task Reorder_OfAMenuThatChanged_IsAConflict_AndTheCacheIsStillForgotten()
    {
        _menus.ReorderAsync(default, default!, default!, default).ThrowsAsyncForAnyArgs(new ConflictException("changed", "menu_changed"));

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.ReorderAsync(MenuKeys.Member, new ReorderMenuRequest([_itemId]), CancellationToken.None));

        Assert.Equal("menu_changed", exception.Code);
        _cache.Received(1).Invalidate();
    }

    [Fact]
    public async Task Reorder_WithADuplicateOrMissingList_IsABadRequest_AndCallsNothing()
    {
        await Assert.ThrowsAsync<BadRequestException>(
            () => _service.ReorderAsync(MenuKeys.Member, new ReorderMenuRequest([_itemId, _itemId]), CancellationToken.None));
        await Assert.ThrowsAsync<BadRequestException>(
            () => _service.ReorderAsync(MenuKeys.Member, new ReorderMenuRequest(null), CancellationToken.None));

        await _menus.DidNotReceiveWithAnyArgs().ReorderAsync(default, default!, default!, default);
    }
}
