using Platform.Auth.Models;

namespace Platform.Auth.Services;

/// <summary>
/// Who sees which menu item. Pure, so it is tested without a database or a web host. A member sees an item
/// of the member menu when it is visible and either needs no permission or their access token carries it;
/// a site admin sees every visible item of the admin menu. The server decides what the client is sent, but
/// hiding an item is not access control: the endpoints check the same permissions on every call.
/// </summary>
public static class MenuVisibility
{
    public static IReadOnlyList<MenuItemRecord> ForMember(IEnumerable<MenuItemRecord> items, IReadOnlySet<string> permissions) =>
        Ordered(items.Where(item =>
            item.MenuKey == MenuKeys.Member
            && item.IsVisible
            && (item.PermissionKey is null || permissions.Contains(item.PermissionKey))));

    public static IReadOnlyList<MenuItemRecord> ForSiteAdmin(IEnumerable<MenuItemRecord> items) =>
        Ordered(items.Where(item => item.MenuKey == MenuKeys.Admin && item.IsVisible));

    private static IReadOnlyList<MenuItemRecord> Ordered(IEnumerable<MenuItemRecord> items) =>
        items.OrderBy(item => item.SortOrder).ThenBy(item => item.Id).ToList();
}
