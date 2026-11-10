using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Platform.Helpers.Constants;
using Platform.Media.DTOs;
using Platform.Media.Interfaces;

namespace Platform.Media.Controllers;

/// <summary>The folders of a workspace. Making one needs <c>medialibrary.create</c>, deleting one <c>medialibrary.delete</c>.</summary>
[ApiController]
[Route("v1/media/folders")]
[Produces("application/json")]
public sealed class MediaFoldersController(IMediaFolderService folders) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.MediaLibraryCreate)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<MediaFolderResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateFolderRequest request, CancellationToken cancellationToken)
    {
        var created = await folders.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>Deletes a folder and the folders in it. The files in it stay, and become Uncategorized unless another folder holds them.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.MediaLibraryDelete)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await folders.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
