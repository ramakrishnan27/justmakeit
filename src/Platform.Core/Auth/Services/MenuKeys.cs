namespace Platform.Auth.Services;

/// <summary>The two menus: the one organization members see and the one site admins see. Equal to <c>menu_pages.app</c>.</summary>
public static class MenuKeys
{
    public const string Member = "member";
    public const string Admin = "admin";

    public static readonly IReadOnlyList<string> All = [Member, Admin];

    public static bool IsKnown(string? key) => key is Member or Admin;
}
