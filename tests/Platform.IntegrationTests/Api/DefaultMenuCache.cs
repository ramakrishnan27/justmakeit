using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Services;
using Platform.Helpers.Constants;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// A menu cache for hosts that have no database: the menus a fresh installation seeds, and nothing to
/// reload. <see cref="ApiHostFactory"/> installs it whenever the host's database address is the unreachable
/// one, next to <see cref="DefaultGrantsPermissionService"/>.
/// </summary>
public sealed class DefaultMenuCache : IMenuCache
{
    public static readonly MenuSnapshot Seeded = new(
    [
        Item(MenuKeys.Admin, "00000000-0000-0000-0000-0000000000a1", "dashboard", "Dashboard", "/admin", null, "home", 10, locked: true),
        Item(MenuKeys.Admin, "00000000-0000-0000-0000-0000000000a2", "menus", "Menus", "/admin/menus", null, "menu", 20, locked: true),
        Item(MenuKeys.Admin, "00000000-0000-0000-0000-0000000000a3", "permissions", "Permissions", "/admin/permissions", null, "shield", 30, locked: true),
        Item(MenuKeys.Member, "00000000-0000-0000-0000-0000000000b1", "dashboard", "Dashboard", "/dashboard", PermissionKeys.DashboardView, "home", 10, locked: true),
        Item(MenuKeys.Member, "00000000-0000-0000-0000-0000000000b2", "workspaces", "Workspaces", "/workspaces", PermissionKeys.WorkspacesView, "folder", 20, locked: false)
    ]);

    public Task<MenuSnapshot> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Seeded);

    public void Invalidate()
    {
    }

    private static MenuItemRecord Item(
        string menu, string id, string page, string title, string path, string? permission, string icon, int order, bool locked) =>
        new()
        {
            MenuKey = menu,
            Id = Guid.Parse(id),
            PageKey = page,
            PageTitle = title,
            Path = path,
            PermissionKey = permission,
            Label = title,
            Icon = icon,
            SortOrder = order,
            IsVisible = true,
            IsLocked = locked
        };
}
