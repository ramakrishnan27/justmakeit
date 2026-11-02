using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Settings;
using Platform.Helpers.Constants;

namespace Platform.Auth.Controllers;

/// <summary>
/// The Site admin's editor for the two menus (<c>member</c> and <c>admin</c>): add a built-in page, rename an
/// item, change its icon, show or hide it, delete it and put the items in order. The locked items (the Site admin's
/// own pages and the members' dashboard) can be renamed and moved but never hidden or deleted.
/// </summary>
[ApiController]
[Route("v1/site/menus")]
[Produces("application/json")]
[Authorize(Policy = PolicyNames.SiteAdmin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(AuthRateLimitPolicies.Session)]
public sealed class SiteMenusController(IMenuAdminService menus) : ControllerBase
{
    /// <summary>One menu with all its items (hidden ones too), the pages that could still be added, and the icons to choose from.</summary>
    [HttpGet("{menuKey}")]
    [ProducesResponseType<MenuConfigResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MenuConfigResponse>> Get(string menuKey, CancellationToken cancellationToken) =>
        await menus.GetConfigAsync(menuKey, cancellationToken);

    /// <summary>Adds a built-in page to the end of a menu.</summary>
    [HttpPost("{menuKey}/items")]
    [ProducesResponseType<MenuConfigItemResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(string menuKey, CreateMenuItemRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        var created = await menus.CreateItemAsync(menuKey, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>Renames an item, changes its icon, and shows or hides it.</summary>
    [HttpPut("{menuKey}/items/{itemId:guid}")]
    [ProducesResponseType<MenuConfigItemResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MenuConfigItemResponse>> Update(
        string menuKey, Guid itemId, UpdateMenuItemRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        return await menus.UpdateItemAsync(menuKey, itemId, request, cancellationToken);
    }

    /// <summary>Removes an item from the menu. The page can be added again.</summary>
    [HttpDelete("{menuKey}/items/{itemId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(string menuKey, Guid itemId, CancellationToken cancellationToken)
    {
        await menus.DeleteItemAsync(menuKey, itemId, cancellationToken);
        return NoContent();
    }

    /// <summary>Puts the items in the given order. The list must be every item of the menu, once each.</summary>
    [HttpPut("{menuKey}/order")]
    [ProducesResponseType<MenuConfigResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MenuConfigResponse>> Reorder(
        string menuKey, ReorderMenuRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();
        return await menus.ReorderAsync(menuKey, request, cancellationToken);
    }
}
