namespace Platform.Auth.Models;

/// <summary>Every menu item of both menus, ordered, as held in the menu cache. Read-only by convention: callers filter it, never change it.</summary>
public sealed record MenuSnapshot(IReadOnlyList<MenuItemRecord> Items);

/// <summary>One menu as the Site admin edits it: all its items (hidden ones too) and the pages that are not in it yet.</summary>
public sealed record MenuConfig(IReadOnlyList<MenuItemRecord> Items, IReadOnlyList<MenuPageRecord> UnusedPages);
