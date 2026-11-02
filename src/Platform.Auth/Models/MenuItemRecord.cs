namespace Platform.Auth.Models;

/// <summary>A menu item joined to the page it links to: one row of <c>menu_snapshot()</c>, or of the Site admin's view of a menu.</summary>
public sealed class MenuItemRecord
{
    public string MenuKey { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public string PageKey { get; set; } = string.Empty;
    public string PageTitle { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    /// <summary>The permission a person needs to see this item, or null when everyone who is signed in sees it.</summary>
    public string? PermissionKey { get; set; }

    public string Label { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsVisible { get; set; }
    public bool IsLocked { get; set; }
}
