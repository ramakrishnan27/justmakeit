using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Platform.Content.DTOs;
using Platform.Content.Interfaces;
using Platform.Helpers.Constants;

namespace Platform.Content.Features.Posts;

/// <summary>
/// The posts of Content Studio: the list, one post, saving, duplicating, archiving, deleting and the history of its text. Reading
/// needs <c>contentstudio.view</c>; making and editing drafts <c>contentstudio.create</c>; archiving and restoring
/// <c>contentstudio.publish</c>. Deleting names the weakest of the three and the service asks for the one the post's status needs
/// (a draft is the writer's, anything that went through publishing is the publisher's). Nothing is cached.
/// </summary>
[ApiController]
[Route("v1/content/posts")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PostsController(IPostService posts) : ControllerBase
{
    /// <summary>A page of posts, newest first, with the per-type counts of the list's tabs.</summary>
    [HttpGet]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.ContentStudioView)]
    [ProducesResponseType<PostPageResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PostPageResponse>> List([FromQuery] PostListQuery query, CancellationToken cancellationToken) =>
        Ok(await posts.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.ContentStudioView)]
    [ProducesResponseType<PostResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PostResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await posts.GetAsync(id, cancellationToken));

    /// <summary>Saves a new draft.</summary>
    [HttpPost]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.ContentStudioCreate)]
    [ProducesResponseType<PostResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] SavePostRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await posts.CreateAsync(request, cancellationToken));

    /// <summary>
    /// Saves the whole post again. <c>expectedUpdatedAt</c> is the version the page loaded: if someone saved since, the answer is 409
    /// <c>content_changed</c>. Only a draft, scheduled or failed post can be edited.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.ContentStudioCreate)]
    [ProducesResponseType<PostResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PostResponse>> Update(Guid id, [FromBody] SavePostRequest request, CancellationToken cancellationToken) =>
        Ok(await posts.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.ContentStudioView)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await posts.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>A new draft that says what this post says.</summary>
    [HttpPost("{id:guid}/duplicate")]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.ContentStudioCreate)]
    [ProducesResponseType<PostResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Duplicate(Guid id, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await posts.DuplicateAsync(id, cancellationToken));

    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.ContentStudioPublish)]
    [ProducesResponseType<PostResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PostResponse>> Archive(Guid id, CancellationToken cancellationToken) =>
        Ok(await posts.ArchiveAsync(id, cancellationToken));

    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.ContentStudioPublish)]
    [ProducesResponseType<PostResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PostResponse>> Restore(Guid id, CancellationToken cancellationToken) =>
        Ok(await posts.RestoreAsync(id, cancellationToken));

    /// <summary>Who saved which version of the post's text, newest first.</summary>
    [HttpGet("{id:guid}/revisions")]
    [Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.ContentStudioView)]
    [ProducesResponseType<IReadOnlyList<RevisionResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RevisionResponse>>> Revisions(Guid id, CancellationToken cancellationToken) =>
        Ok(await posts.ListRevisionsAsync(id, cancellationToken));
}
