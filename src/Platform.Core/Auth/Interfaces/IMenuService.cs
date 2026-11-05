using Platform.Auth.DTOs;

namespace Platform.Auth.Interfaces;

/// <summary>The sidebars. Both read the in-memory menu cache and the caller's token, never the database.</summary>
public interface IMenuService
{
    /// <summary>The member sidebar: the visible items whose permission the caller's token carries.</summary>
    Task<MenuResponse> GetMemberMenuAsync(CancellationToken cancellationToken);

    /// <summary>The site admin sidebar: every visible item of the admin menu.</summary>
    Task<MenuResponse> GetSiteAdminMenuAsync(CancellationToken cancellationToken);
}
