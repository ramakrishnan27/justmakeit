namespace Platform.Auth.Models;

/// <summary>A built-in page a menu item may link to (a row of <c>menu_pages</c>).</summary>
public sealed class MenuPageRecord
{
    public string App { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? PermissionKey { get; set; }
}
