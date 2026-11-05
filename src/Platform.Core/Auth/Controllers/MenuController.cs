using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Settings;

namespace Platform.Auth.Controllers;

/// <summary>The sidebar of the signed-in organization member. Any member may ask; what comes back depends on their permissions.</summary>
[ApiController]
[Route("v1/menu")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting(AuthRateLimitPolicies.Session)]
public sealed class MenuController(IMenuService menus) : ControllerBase
{
    /// <summary>
    /// The items this person sees and the permissions their access token carries. Read from memory: no
    /// database call unless the cache has expired.
    /// </summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType<MenuResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MenuResponse>> Get(CancellationToken cancellationToken) =>
        await menus.GetMemberMenuAsync(cancellationToken);
}
