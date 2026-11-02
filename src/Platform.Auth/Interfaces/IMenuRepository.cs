using Platform.Auth.Models;

namespace Platform.Auth.Interfaces;

public interface IMenuRepository
{
    /// <summary>Every item of both menus with its page, in one statement. Configuration, so it needs no signed-in user.</summary>
    Task<MenuSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken);

    /// <summary>One menu as the Site admin edits it. Throws <c>not_site_admin</c> (403) for anybody else.</summary>
    Task<MenuConfig> GetConfigAsync(Guid userId, string menuKey, CancellationToken cancellationToken);

    /// <summary>One item with its page, or null. Throws <c>not_site_admin</c> (403) for anybody else.</summary>
    Task<MenuItemRecord?> FindItemAsync(Guid userId, Guid itemId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds an item at the end of the menu and returns its id. Throws <c>page_not_found</c> (404) for a page that
    /// is not in this menu's app and <c>page_already_in_menu</c> (409) for one that is already there.
    /// </summary>
    Task<Guid> CreateItemAsync(
        Guid userId, string menuKey, string pageKey, string label, string icon, bool isVisible, CancellationToken cancellationToken);

    /// <summary>
    /// Changes an item. A null <paramref name="isVisible"/> leaves it as it is. Returns false when there is no such item;
    /// throws <c>menu_item_locked</c> (409) for an attempt to hide a locked one.
    /// </summary>
    Task<bool> UpdateItemAsync(
        Guid userId, Guid itemId, string label, string icon, bool? isVisible, CancellationToken cancellationToken);

    /// <summary>Returns false when there is no such item; throws <c>menu_item_locked</c> (409) for a locked one.</summary>
    Task<bool> DeleteItemAsync(Guid userId, Guid itemId, CancellationToken cancellationToken);

    /// <summary>Sets the order to exactly this list of ids. Throws <c>menu_changed</c> (409) when it is not the menu's current set.</summary>
    Task ReorderAsync(Guid userId, string menuKey, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken);
}
