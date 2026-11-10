using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Platform.Helpers.Constants;
using Platform.Media.DTOs;
using Platform.Media.Interfaces;

namespace Platform.Media.Controllers;

/// <summary>The files of the Media Library. Reading needs <c>medialibrary.view</c>, adding and organizing <c>medialibrary.create</c>, deleting <c>medialibrary.delete</c>.</summary>
[ApiController]
[Route("v1/media/assets")]
[Produces("application/json")]
public sealed class MediaAssetsController(IMediaAssetService assets) : ControllerBase
{
    /// <summary>A page of files, narrowed by type, folder, tag, words, size and date, with the count of each type for the tabs.</summary>
    [HttpGet]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.MediaLibraryView)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<MediaPageResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MediaPageResponse>> List([FromQuery] MediaListQuery query, CancellationToken cancellationToken) =>
        Ok(await assets.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.MediaLibraryView)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<MediaAssetResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MediaAssetResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await assets.GetAsync(id, cancellationToken));

    /// <summary>Adds one file from a form: <c>workspaceId</c>, the <c>file</c>, and optionally a <c>folderId</c> and <c>tags</c> (one field each).</summary>
    [HttpPost]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.MediaLibraryCreate)]
    [Consumes("multipart/form-data")]
    [TypeFilter(typeof(MediaUploadLimitFilter))]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<MediaAssetResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<MediaAssetResponse>> Upload([FromForm] UploadMediaForm form, CancellationToken cancellationToken)
    {
        await using var content = form.File?.OpenReadStream();
        var request = new MediaUploadRequest(form.WorkspaceId, form.FolderId, form.Tags, form.File?.FileName, form.File?.Length ?? 0, content);

        var created = await assets.UploadAsync(request, cancellationToken);
        return Created($"/v1/media/assets/{created.Id}", created);
    }

    /// <summary>
    /// The stored bytes. An image, a video or a sound is sent to be shown (with support for seeking, which a video needs); a document is
    /// always a download, and <c>?download=true</c> makes any file one. Served as the type that was found in the file, never guessed.
    /// </summary>
    [HttpGet("{id:guid}/file")]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.MediaLibraryView)]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> File(Guid id, [FromQuery] bool download, CancellationToken cancellationToken)
    {
        var file = await assets.OpenFileAsync(id, cancellationToken);

        Response.Headers.XContentTypeOptions = "nosniff";
        // A file opened by itself is a page of its own: this keeps anything in it from running as part of this site.
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";

        var attachment = download || file.MediaType == "document";
        return File(file.Content, file.MimeType, attachment ? file.FileName : null, enableRangeProcessing: true);
    }

    /// <summary>Puts files in a folder (copy), or in a folder and nowhere else (move); a move with no folder makes them Uncategorized.</summary>
    [HttpPost("organize")]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.MediaLibraryCreate)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Organize([FromBody] OrganizeAssetsRequest request, CancellationToken cancellationToken)
    {
        await assets.OrganizeAsync(request, cancellationToken);
        return NoContent();
    }

    /// <summary>Deletes a file and its stored bytes. A file that content uses is refused with 409 <c>asset_in_use</c>.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.MediaLibraryDelete)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await assets.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}

/// <summary>The upload form. <c>Tags</c> is one form field per tag.</summary>
public sealed class UploadMediaForm
{
    public Guid? WorkspaceId { get; init; }
    public Guid? FolderId { get; init; }
    public string[]? Tags { get; init; }
    public IFormFile? File { get; init; }
}
