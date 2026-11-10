namespace Platform.Content.Interfaces;

public enum ScheduleOutcome
{
    Done,
    NotFound,

    /// <summary>The post is not in a status this can be done from (somebody else changed it first).</summary>
    WrongStatus,

    /// <summary>A platform is already publishing or has published, so the schedule can no longer be taken back.</summary>
    InProgress,

    NoTargets
}

/// <summary>The changes to a post's status and its platform targets that scheduling makes, each in one transaction with the post locked.</summary>
public interface ISchedulingRepository
{
    /// <summary>
    /// Sets the schedule: the post becomes scheduled (or publishing, for <paramref name="publishNow"/>), every platform target becomes
    /// scheduled for <paramref name="when"/> with its attempts and error cleared. Only if the post's status is one of
    /// <paramref name="allowedFrom"/>.
    /// </summary>
    Task<ScheduleOutcome> ScheduleAsync(
        Guid agencyId, Guid userId, Guid postId, DateTimeOffset when, bool publishNow, IReadOnlyCollection<string> allowedFrom, CancellationToken cancellationToken);

    /// <summary>Takes the schedule back: the post becomes a draft and its targets pending, unless one has begun to publish.</summary>
    Task<ScheduleOutcome> UnscheduleAsync(Guid agencyId, Guid userId, Guid postId, CancellationToken cancellationToken);
}
