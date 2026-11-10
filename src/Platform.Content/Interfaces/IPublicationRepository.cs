using Platform.Content.Entities;

namespace Platform.Content.Interfaces;

/// <summary>A platform target that is due and has been claimed for this process: where to find it.</summary>
public sealed class ClaimedPublication
{
    public Guid AgencyId { get; set; }
    public Guid PostId { get; set; }
    public Guid PublicationId { get; set; }
}

/// <summary>What the publisher is given and what the result is recorded against: one target and the post text it comes from.</summary>
public sealed class PublicationWork
{
    public required Guid AgencyId { get; init; }
    public required Guid PostId { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required ContentPostPlatform Target { get; init; }
    public required IReadOnlyList<string> Hashtags { get; init; }
}

/// <summary>
/// The publishing worker's data access. Finding work spans organizations, which a request-scoped connection cannot do, so it goes
/// through the <c>content_claim_due_publications()</c> function of <c>015_content_studio_posts.sql</c>; everything after that runs on
/// a connection scoped to the target's own organization.
/// </summary>
public interface IPublicationRepository
{
    /// <summary>Claims up to <paramref name="limit"/> due targets (marking them publishing) and returns where they are.</summary>
    Task<IReadOnlyList<ClaimedPublication>> ClaimDueAsync(int limit, int staleMinutes, CancellationToken cancellationToken);

    /// <summary>The target and its post's text, and the post becomes publishing if it was scheduled. Null when it is gone or no longer claimed.</summary>
    Task<PublicationWork?> LoadAsync(Guid agencyId, Guid postId, Guid publicationId, CancellationToken cancellationToken);

    /// <summary>The target is published. The post becomes published when no other target is still open.</summary>
    Task RecordSuccessAsync(Guid agencyId, Guid publicationId, string externalPostId, string? externalPostUrl, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// The attempt failed. With <paramref name="retryAt"/> the target is scheduled again for then; without, it is failed. Either way
    /// the attempt is counted, and the post becomes failed when it was the last open target and none succeeded.
    /// </summary>
    Task RecordFailureAsync(Guid agencyId, Guid publicationId, string error, DateTimeOffset? retryAt, DateTimeOffset now, CancellationToken cancellationToken);
}
