using Microsoft.Extensions.Logging;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;

namespace Platform.Auth.Services;

public sealed class MenuAdminService(
    IMenuRepository menus,
    IMenuCache cache,
    ITenantContext tenant,
    ILogger<MenuAdminService> logger) : IMenuAdminService
{
    public async Task<MenuConfigResponse> GetConfigAsync(string menuKey, CancellationToken cancellationToken)
    {
        var userId = RequireSiteAdmin();
        RequireMenu(menuKey);

        return ToResponse(menuKey, await menus.GetConfigAsync(userId, menuKey, cancellationToken));
    }

    public async Task<MenuConfigItemResponse> CreateItemAsync(
        string menuKey, CreateMenuItemRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        var userId = RequireSiteAdmin();
        RequireMenu(menuKey);

        Guid id;
        try
        {
            id = await menus.CreateItemAsync(
                userId, menuKey, request.PageKey!, request.Label!.Trim(), request.Icon!, request.IsVisible ?? true, cancellationToken);
        }
        finally
        {
            // Whatever happened, the next read must not be served from before this call.
            cache.Invalidate();
        }

        logger.LogInformation("Site admin {UserId} added page {PageKey} to the {Menu} menu as item {ItemId}", userId, request.PageKey, menuKey, id);

        var item = await menus.FindItemAsync(userId, id, cancellationToken)
                   ?? throw new NotFoundException("The menu item was not found.", "menu_item_not_found");
        return ToResponse(item);
    }

    public async Task<MenuConfigItemResponse> UpdateItemAsync(
        string menuKey, Guid itemId, UpdateMenuItemRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        var userId = RequireSiteAdmin();
        RequireMenu(menuKey);
        await RequireItemInMenuAsync(userId, menuKey, itemId, cancellationToken);

        bool found;
        try
        {
            found = await menus.UpdateItemAsync(userId, itemId, request.Label!.Trim(), request.Icon!, request.IsVisible, cancellationToken);
        }
        finally
        {
            cache.Invalidate();
        }

        if (!found)
        {
            throw ItemNotFound();
        }

        logger.LogInformation("Site admin {UserId} changed item {ItemId} of the {Menu} menu", userId, itemId, menuKey);
        return ToResponse(await RequireItemInMenuAsync(userId, menuKey, itemId, cancellationToken));
    }

    public async Task DeleteItemAsync(string menuKey, Guid itemId, CancellationToken cancellationToken)
    {
        var userId = RequireSiteAdmin();
        RequireMenu(menuKey);
        await RequireItemInMenuAsync(userId, menuKey, itemId, cancellationToken);

        bool found;
        try
        {
            found = await menus.DeleteItemAsync(userId, itemId, cancellationToken);
        }
        finally
        {
            cache.Invalidate();
        }

        if (!found)
        {
            throw ItemNotFound();
        }

        logger.LogInformation("Site admin {UserId} removed item {ItemId} from the {Menu} menu", userId, itemId, menuKey);
    }

    public async Task<MenuConfigResponse> ReorderAsync(string menuKey, ReorderMenuRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        var userId = RequireSiteAdmin();
        RequireMenu(menuKey);

        try
        {
            await menus.ReorderAsync(userId, menuKey, request.ItemIds!, cancellationToken);
        }
        finally
        {
            cache.Invalidate();
        }

        logger.LogInformation("Site admin {UserId} reordered the {Menu} menu", userId, menuKey);
        return ToResponse(menuKey, await menus.GetConfigAsync(userId, menuKey, cancellationToken));
    }

    private Guid RequireSiteAdmin()
    {
        var userId = tenant.RequireUserId();
        return tenant.Role == RoleNames.SiteAdmin
            ? userId
            : throw new ForbiddenException("Site admin access is required.", "not_site_admin");
    }

    private static void RequireMenu(string menuKey)
    {
        if (!MenuKeys.IsKnown(menuKey))
        {
            throw new NotFoundException("There is no such menu.", "menu_not_found");
        }
    }

    /// <summary>An item addressed through the wrong menu is as missing as one that does not exist.</summary>
    private async Task<MenuItemRecord> RequireItemInMenuAsync(
        Guid userId, string menuKey, Guid itemId, CancellationToken cancellationToken)
    {
        var item = await menus.FindItemAsync(userId, itemId, cancellationToken);
        return item is not null && item.MenuKey == menuKey ? item : throw ItemNotFound();
    }

    private static NotFoundException ItemNotFound() => new("The menu item was not found.", "menu_item_not_found");

    private static MenuConfigResponse ToResponse(string menuKey, MenuConfig config) =>
        new(
            menuKey,
            config.Items.Select(ToResponse).ToList(),
            config.UnusedPages.Select(page => new MenuPageResponse(page.Key, page.Title, page.Path, page.PermissionKey)).ToList(),
            MenuIcons.All);

    private static MenuConfigItemResponse ToResponse(MenuItemRecord item) =>
        new(
            item.Id, item.PageKey, item.PageTitle, item.Path, item.Label, item.Icon, item.SortOrder,
            item.IsVisible, item.IsLocked, item.PermissionKey);
}
