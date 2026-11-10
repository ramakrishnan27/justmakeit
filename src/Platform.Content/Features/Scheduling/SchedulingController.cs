using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Platform.Content.DTOs;
using Platform.Content.Interfaces;
using Platform.Helpers.Constants;

namespace Platform.Content.Features.Scheduling;

/// <summary>
/// Scheduling a post, taking the schedule back, and publishing now. Every one needs <c>contentstudio.publish</c>: a writer in a company
/// or an agency saves drafts and someone else publishes them.
/// </summary>
[ApiController]
[Route("v1/content/posts/{id:guid}")]
[Produces("application/json")]
[Authorize(Policy = PolicyNames.PermissionPrefix + PermissionKeys.ContentStudioPublish)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SchedulingController(ISchedulingService scheduling) : ControllerBase
{
    /// <summary>Sets (or changes) when the post goes out, to every platform it names. At least a minute from now, at most a year.</summary>
    [HttpPost("schedule")]
    [ProducesResponseType<PostResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PostResponse>> Schedule(Guid id, [FromBody] ScheduleRequest request, CancellationToken cancellationToken) =>
        Ok(await scheduling.ScheduleAsync(id, request, cancellationToken));

    /// <summary>Takes a scheduled post back to a draft, unless a platform has already started publishing it.</summary>
    [HttpPost("unschedule")]
    [ProducesResponseType<PostResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PostResponse>> Unschedule(Guid id, CancellationToken cancellationToken) =>
        Ok(await scheduling.UnscheduleAsync(id, cancellationToken));

    /// <summary>Asks for the post to be published as soon as the worker next looks (within seconds).</summary>
    [HttpPost("publish-now")]
    [ProducesResponseType<PostResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PostResponse>> PublishNow(Guid id, CancellationToken cancellationToken) =>
        Ok(await scheduling.PublishNowAsync(id, cancellationToken));
}
