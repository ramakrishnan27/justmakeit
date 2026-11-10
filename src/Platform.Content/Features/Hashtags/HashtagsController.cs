using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Platform.Content.DTOs;
using Platform.Content.Interfaces;
using Platform.Helpers.Constants;

namespace Platform.Content.Features.Hashtags;

/// <summary>Hashtag suggestions (from what the organization already used) and the tags of a workspace.</summary>
[ApiController]
[Route("v1/content")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class HashtagsController(IHashtagService hashtags) : ControllerBase
{
    [HttpGet("hashtags/suggestions")]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.ContentStudioView)]
    [ProducesResponseType<IReadOnlyList<HashtagSuggestionResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HashtagSuggestionResponse>>> Suggest(
        [FromQuery] HashtagSuggestionQuery query, CancellationToken cancellationToken) =>
        Ok(await hashtags.SuggestAsync(query, cancellationToken));

    [HttpGet("tags")]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.ContentStudioView)]
    [ProducesResponseType<IReadOnlyList<TagResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TagResponse>>> ListTags([FromQuery] Guid? workspaceId, CancellationToken cancellationToken) =>
        Ok(await hashtags.ListTagsAsync(workspaceId, cancellationToken));

    [HttpPost("tags")]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.ContentStudioCreate)]
    [ProducesResponseType<TagResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTag([FromBody] CreateTagRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await hashtags.CreateTagAsync(request, cancellationToken));
}
