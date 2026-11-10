using Platform.Content.Enums;

namespace Platform.Content.Features.Scheduling;

/// <summary>
/// Which status a post may move to from which. Pure: no database, no clock. The services ask it before they change anything, and
/// the repositories repeat the "from" side in their UPDATE (WHERE status = ...), so two people acting at once cannot both succeed.
/// </summary>
public static class ContentStatusMachine
{
    private static readonly IReadOnlyDictionary<ContentStatus, ContentStatus[]> Moves = new Dictionary<ContentStatus, ContentStatus[]>
    {
        [ContentStatus.Draft] = [ContentStatus.Scheduled, ContentStatus.Publishing, ContentStatus.Archived],
        [ContentStatus.Scheduled] = [ContentStatus.Draft, ContentStatus.Publishing],
        [ContentStatus.Publishing] = [ContentStatus.Published, ContentStatus.Failed],
        [ContentStatus.Failed] = [ContentStatus.Scheduled, ContentStatus.Draft, ContentStatus.Publishing, ContentStatus.Archived],
        [ContentStatus.Published] = [ContentStatus.Archived],
        [ContentStatus.Archived] = [ContentStatus.Draft, ContentStatus.Published]
    };

    public static bool CanMove(ContentStatus from, ContentStatus to) => Moves[from].Contains(to);

    /// <summary>The statuses from which <paramref name="to"/> can be reached: what an UPDATE may expect to find.</summary>
    public static IReadOnlyList<ContentStatus> Sources(ContentStatus to) =>
        [.. Moves.Where(move => move.Value.Contains(to)).Select(move => move.Key)];

    /// <summary>The content of a post can be changed only in these. Once it is going out or has gone, it is a record.</summary>
    public static bool IsEditable(ContentStatus status) => status is ContentStatus.Draft or ContentStatus.Scheduled or ContentStatus.Failed;

    /// <summary>A post can be deleted for good only in these; a published one is archived instead, so its history stays.</summary>
    public static bool IsDeletable(ContentStatus status) => status is ContentStatus.Draft or ContentStatus.Failed or ContentStatus.Archived;

    /// <summary>
    /// Whether a schedule can be set. A scheduled post can be scheduled again (rescheduled), which is not a move to another status, so
    /// it is not in <see cref="CanMove"/>.
    /// </summary>
    public static bool CanSchedule(ContentStatus status) => status is ContentStatus.Draft or ContentStatus.Failed or ContentStatus.Scheduled;

    public static bool CanPublishNow(ContentStatus status) => CanMove(status, ContentStatus.Publishing);

    /// <summary>Where a restored post goes: back to published when it had been, otherwise to a draft.</summary>
    public static ContentStatus RestoreTarget(DateTimeOffset? publishedAt) =>
        publishedAt is null ? ContentStatus.Draft : ContentStatus.Published;
}
