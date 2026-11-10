using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Helpers.Constants;

namespace Platform.Auth.Controllers;

/// <summary>
/// The platforms (channels) a member can see on the Platform page, and whether each is enabled for their organization. Read only:
/// the Site admin decides which are enabled. Needs the platforms.view permission, which every role holds by default. Not rate
/// limited like the other member routes: a page of uploaded icons makes one image request each, and the browser keeps them.
/// </summary>
[ApiController]
[Route("v1/platforms")]
[Produces("application/json")]
[Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.PlatformsView)]
public sealed class PlatformsController(IPlatformService platforms) : ControllerBase
{
    /// <summary>Every platform, oldest first, with whether it is enabled for the current organization.</summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<IReadOnlyList<PlatformResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlatformResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await platforms.ListAsync(cancellationToken));

    /// <summary>The uploaded icon of a platform, as the image that was stored. 404 for a platform with a built-in icon or that does not exist.</summary>
    [HttpGet("{id:guid}/icon")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Icon(Guid id, CancellationToken cancellationToken)
    {
        var icon = await platforms.GetIconAsync(id, cancellationToken);

        // Served as the image it was checked to be when it was uploaded, and never guessed at.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(icon.Image, icon.ContentType);
    }
}
