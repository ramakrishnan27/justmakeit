using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Platform.Helpers.Constants;
using Platform.Media.DTOs;
using Platform.Media.Interfaces;

namespace Platform.Media.Controllers;

/// <summary>What the Media Library page needs besides the files: the limits, and the folders and tags of a workspace.</summary>
[ApiController]
[Route("v1/media")]
[Produces("application/json")]
public sealed class MediaLibraryController(IMediaAssetService assets, IMediaFolderService folders) : ControllerBase
{
    [HttpGet("config")]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.MediaLibraryView)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<MediaConfigResponse>(StatusCodes.Status200OK)]
    public ActionResult<MediaConfigResponse> Config() => Ok(assets.GetConfig());

    /// <summary>The folders and tags of a workspace with their counts, and the counts of "All Media" and "Uncategorized".</summary>
    [HttpGet("overview")]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.MediaLibraryView)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<MediaOverviewResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MediaOverviewResponse>> Overview([FromQuery] Guid? workspaceId, CancellationToken cancellationToken) =>
        Ok(await folders.GetOverviewAsync(workspaceId, cancellationToken));
}
