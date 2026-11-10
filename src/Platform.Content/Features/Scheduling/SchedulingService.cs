using Microsoft.Extensions.Logging;
using Platform.Content.Data;
using Platform.Content.DTOs;
using Platform.Content.Entities;
using Platform.Content.Enums;
using Platform.Content.Features.Media;
using Platform.Content.Features.Posts;
using Platform.Content.Interfaces;
using Platform.Content.Services;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;
using Platform.Helpers.Validation;

namespace Platform.Content.Features.Scheduling;

/// <summary>
/// Scheduling, taking a schedule back, and publishing now. All need <c>contentstudio.publish</c>. A post must be fit to go out
/// (<see cref="ScheduleReadiness"/>) and every platform it names must be enabled for the organization; the status rules are
/// <see cref="ContentStatusMachine"/>'s, repeated in the repository's UPDATE so two people acting at once cannot both succeed.
/// </summary>
public sealed class SchedulingService(
    IContentPostRepository posts,
    ISchedulingRepository scheduling,
    IContentLookupRepository lookups,
    IContentMediaLibrary media,
    ITenantContext tenant,
    TimeProvider time,
    ILogger<SchedulingService> logger) : ISchedulingService
{
    /// <summary>The soonest a schedule can be set: far enough ahead that the person, the clock and the worker agree it is in the future.</summary>
    public static readonly TimeSpan MinimumLead = TimeSpan.FromMinutes(1);

    /// <summary>The furthest ahead a schedule can be set.</summary>
    public static readonly TimeSpan MaximumLead = TimeSpan.FromDays(365);

    public async Task<PostResponse> ScheduleAsync(Guid postId, ScheduleRequest request, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = ContentAccess.Require(tenant, PermissionKeys.ContentStudioPublish, "schedule content");

        var now = time.GetUtcNow();
        var errors = new ValidationErrors();
        if (request.ScheduledAt is null)
        {
            errors.Add("scheduledAt", "Choose when to publish.");
        }
        else if (request.ScheduledAt < now + MinimumLead)
        {
            errors.Add("scheduledAt", "Choose a time at least a minute from now.");
        }
        else if (request.ScheduledAt > now + MaximumLead)
        {
            errors.Add("scheduledAt", "Choose a time within the next year.");
        }

        errors.ThrowIfAny();

        var post = await LoadAsync(agencyId, userId, postId, cancellationToken);
        var status = DbEnum<ContentStatus>.Parse(post.Post.Status);
        if (!ContentStatusMachine.CanSchedule(status))
        {
            throw new ConflictException($"A post that is {post.Post.Status} cannot be scheduled.", "invalid_transition");
        }

        await EnsureReadyAsync(post, agencyId, userId, cancellationToken);
        var outcome = await scheduling.ScheduleAsync(
            agencyId, userId, postId, request.ScheduledAt!.Value, publishNow: false, Sources(ContentStatus.Scheduled), cancellationToken);
        Throw(outcome);

        logger.LogInformation("User {UserId} scheduled post {PostId}", userId, postId);
        return await MediaHydration.ToResponseAsync(media, await LoadAsync(agencyId, userId, postId, cancellationToken), agencyId, userId, cancellationToken);
    }

    public async Task<PostResponse> UnscheduleAsync(Guid postId, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = ContentAccess.Require(tenant, PermissionKeys.ContentStudioPublish, "unschedule content");
        var post = await LoadAsync(agencyId, userId, postId, cancellationToken);
        if (DbEnum<ContentStatus>.Parse(post.Post.Status) != ContentStatus.Scheduled)
        {
            throw new ConflictException("Only a scheduled post can be unscheduled.", "invalid_transition");
        }

        Throw(await scheduling.UnscheduleAsync(agencyId, userId, postId, cancellationToken));

        logger.LogInformation("User {UserId} unscheduled post {PostId}", userId, postId);
        return await MediaHydration.ToResponseAsync(media, await LoadAsync(agencyId, userId, postId, cancellationToken), agencyId, userId, cancellationToken);
    }

    public async Task<PostResponse> PublishNowAsync(Guid postId, CancellationToken cancellationToken)
    {
        var (agencyId, userId) = ContentAccess.Require(tenant, PermissionKeys.ContentStudioPublish, "publish content");
        var post = await LoadAsync(agencyId, userId, postId, cancellationToken);
        var status = DbEnum<ContentStatus>.Parse(post.Post.Status);
        if (!ContentStatusMachine.CanPublishNow(status))
        {
            throw new ConflictException($"A post that is {post.Post.Status} cannot be published now.", "invalid_transition");
        }

        await EnsureReadyAsync(post, agencyId, userId, cancellationToken);
        Throw(await scheduling.ScheduleAsync(
            agencyId, userId, postId, time.GetUtcNow(), publishNow: true, Sources(ContentStatus.Publishing), cancellationToken));

        logger.LogInformation("User {UserId} asked to publish post {PostId} now", userId, postId);
        return await MediaHydration.ToResponseAsync(media, await LoadAsync(agencyId, userId, postId, cancellationToken), agencyId, userId, cancellationToken);
    }

    private async Task EnsureReadyAsync(ContentPostAggregate post, Guid agencyId, Guid userId, CancellationToken cancellationToken)
    {
        ScheduleReadiness.Check(post).ThrowIfAny();
        await PlatformAccess.EnsureEnabledAsync(lookups, agencyId, userId, post.Platforms, cancellationToken);
    }

    /// <summary>Statuses a post may be scheduled from, as the repository's UPDATE expects them: a reschedule starts from scheduled itself.</summary>
    private static List<string> Sources(ContentStatus to) =>
        [.. ContentStatusMachine.Sources(to).Select(s => s.ToWire()), ContentStatus.Scheduled.ToWire()];

    private async Task<ContentPostAggregate> LoadAsync(Guid agencyId, Guid userId, Guid postId, CancellationToken cancellationToken) =>
        await posts.GetAsync(agencyId, userId, postId, cancellationToken)
        ?? throw new NotFoundException("That post was not found.", "post_not_found");

    private static void Throw(ScheduleOutcome outcome)
    {
        switch (outcome)
        {
            case ScheduleOutcome.Done:
                return;
            case ScheduleOutcome.NotFound:
                throw new NotFoundException("That post was not found.", "post_not_found");
            case ScheduleOutcome.InProgress:
                throw new ConflictException("Some platforms have already started publishing, so the schedule cannot be changed.", "post_publishing");
            case ScheduleOutcome.NoTargets:
                throw new BadRequestException("Choose at least one platform.", ValidationErrors.Code,
                    new Dictionary<string, string[]> { ["platforms"] = ["Choose at least one platform."] });
            default:
                throw new ConflictException("This post changed while you were working on it. Reload it and try again.", "invalid_transition");
        }
    }
}
