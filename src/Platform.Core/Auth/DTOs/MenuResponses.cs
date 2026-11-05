namespace Platform.Auth.DTOs;

/// <summary>One link in a sidebar: where it goes, what it says and which icon it wears.</summary>
public sealed record MenuItemResponse(Guid Id, string PageKey, string Label, string Icon, string Path);

/// <summary>
/// The sidebar for the signed-in person, and the permissions their token carries (so the web client can guard
/// its pages without asking again). A site admin gets the admin menu and no permissions.
/// </summary>
public sealed record MenuResponse(IReadOnlyList<MenuItemResponse> Items, IReadOnlyList<string> Permissions);

/// <summary>An item as the Site admin edits it, hidden ones included.</summary>
public sealed record MenuConfigItemResponse(
    Guid Id,
    string PageKey,
    string PageTitle,
    string Path,
    string Label,
    string Icon,
    int SortOrder,
    bool IsVisible,
    bool IsLocked,
    string? RequiredPermission);

/// <summary>A built-in page that is not in the menu yet and may be added.</summary>
public sealed record MenuPageResponse(string Key, string Title, string Path, string? RequiredPermission);

public sealed record MenuConfigResponse(
    string MenuKey,
    IReadOnlyList<MenuConfigItemResponse> Items,
    IReadOnlyList<MenuPageResponse> AvailablePages,
    IReadOnlyList<string> Icons);
