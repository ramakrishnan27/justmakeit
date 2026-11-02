using Platform.Auth.DTOs;

namespace Platform.Auth.Interfaces;

/// <summary>What the Site admin does to the menus. Every method needs a site admin and answers 403 <c>not_site_admin</c> otherwise.</summary>
public interface IMenuAdminService
{
    Task<MenuConfigResponse> GetConfigAsync(string menuKey, CancellationToken cancellationToken);

    Task<MenuConfigItemResponse> CreateItemAsync(string menuKey, CreateMenuItemRequest request, CancellationToken cancellationToken);

    Task<MenuConfigItemResponse> UpdateItemAsync(
        string menuKey, Guid itemId, UpdateMenuItemRequest request, CancellationToken cancellationToken);

    Task DeleteItemAsync(string menuKey, Guid itemId, CancellationToken cancellationToken);

    Task<MenuConfigResponse> ReorderAsync(string menuKey, ReorderMenuRequest request, CancellationToken cancellationToken);
}
