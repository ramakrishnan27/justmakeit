using Platform.Auth.Models;
using Platform.Auth.Services;
using Platform.Helpers.Constants;

namespace Platform.Auth.Tests;

public sealed class MenuVisibilityTests
{
    private static MenuItemRecord Item(
        string label, string menu = MenuKeys.Member, string? permission = null, int order = 10, bool visible = true, Guid? id = null) =>
        new()
        {
            MenuKey = menu,
            Id = id ?? Guid.NewGuid(),
            PageKey = label.ToLowerInvariant(),
            PageTitle = label,
            Path = "/" + label.ToLowerInvariant(),
            PermissionKey = permission,
            Label = label,
            Icon = "home",
            SortOrder = order,
            IsVisible = visible
        };

    private static HashSet<string> Holding(params string[] keys) => [.. keys];

    [Fact]
    public void AMember_SeesAnItemThatNeedsNoPermission_EvenWithNone()
    {
        var items = MenuVisibility.ForMember([Item("Home")], Holding());

        Assert.Equal(["Home"], items.Select(i => i.Label));
    }

    [Fact]
    public void AMember_SeesAnItemOnlyWhenTheirTokenCarriesItsPermission()
    {
        var workspaces = Item("Workspaces", permission: PermissionKeys.WorkspacesView);

        Assert.Empty(MenuVisibility.ForMember([workspaces], Holding(PermissionKeys.DashboardView)));
        Assert.Single(MenuVisibility.ForMember([workspaces], Holding(PermissionKeys.WorkspacesView)));
    }

    [Fact]
    public void AMember_NeverSeesAHiddenItem_WhateverTheyHold()
    {
        var hidden = Item("Workspaces", permission: PermissionKeys.WorkspacesView, visible: false);

        Assert.Empty(MenuVisibility.ForMember([hidden], Holding(PermissionKeys.All.ToArray())));
    }

    [Fact]
    public void AMember_NeverSeesAnItemOfTheAdminMenu()
    {
        var adminPage = Item("Menus", menu: MenuKeys.Admin);

        Assert.Empty(MenuVisibility.ForMember([adminPage], Holding(PermissionKeys.All.ToArray())));
    }

    [Theory]
    [InlineData("Workspaces.View")]
    [InlineData("workspaces.view ")]
    [InlineData("workspaces")]
    [InlineData("")]
    public void APermissionMustMatchTheKeyExactly(string held)
    {
        var workspaces = Item("Workspaces", permission: PermissionKeys.WorkspacesView);

        Assert.Empty(MenuVisibility.ForMember([workspaces], Holding(held)));
    }

    [Fact]
    public void Items_AreOrderedBySortOrder_ThenById_NotByTheOrderTheyArrivedIn()
    {
        var low = new Guid("00000000-0000-0000-0000-000000000001");
        var high = new Guid("00000000-0000-0000-0000-000000000002");
        var items = MenuVisibility.ForMember(
            [Item("Last", order: 30), Item("TieB", order: 20, id: high), Item("First", order: 10), Item("TieA", order: 20, id: low)],
            Holding());

        Assert.Equal(["First", "TieA", "TieB", "Last"], items.Select(i => i.Label));
    }

    [Fact]
    public void ASiteAdmin_SeesTheVisibleItemsOfTheAdminMenuOnly()
    {
        var items = MenuVisibility.ForSiteAdmin(
        [
            Item("Dashboard", menu: MenuKeys.Admin, order: 10),
            Item("Hidden", menu: MenuKeys.Admin, order: 20, visible: false),
            Item("Menus", menu: MenuKeys.Admin, order: 30),
            Item("Workspaces", menu: MenuKeys.Member, permission: PermissionKeys.WorkspacesView)
        ]);

        Assert.Equal(["Dashboard", "Menus"], items.Select(i => i.Label));
    }

    [Fact]
    public void TheInputIsNotChanged()
    {
        var list = new List<MenuItemRecord> { Item("B", order: 20), Item("A", order: 10) };

        MenuVisibility.ForMember(list, Holding());

        Assert.Equal(["B", "A"], list.Select(i => i.Label));
    }
}
