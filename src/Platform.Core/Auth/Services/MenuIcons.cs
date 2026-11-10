namespace Platform.Auth.Services;

/// <summary>
/// The icons a menu item may use. The database only checks the shape of the name, so a later script can
/// add one; this list is what the API accepts and what the web client has a drawing for (a test compares
/// the two). An unknown name in the database is still shown, with the plain circle.
/// </summary>
public static class MenuIcons
{
    public static readonly IReadOnlyList<string> All =
        ["home", "folder", "menu", "shield", "users", "mail", "chart", "settings", "list", "edit", "image", "sparkles", "calendar", "circle"];

    public static bool IsKnown(string? icon) => icon is not null && All.Contains(icon);
}
