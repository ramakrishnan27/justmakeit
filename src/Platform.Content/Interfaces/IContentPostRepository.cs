using Platform.Content.Entities;

namespace Platform.Content.Interfaces;

/// <summary>What a save found.</summary>
public enum SaveOutcome
{
    Saved,

    /// <summary>The post is not there with the <c>updated_at</c> the page loaded: someone saved first, or it was deleted.</summary>
    Changed,

    /// <summary>A platform is publishing or has published: the post's children cannot be replaced now.</summary>
    Busy
}

/// <summary>The list page's filters as the repository applies them (already validated, the search already the user's words).</summary>
public sealed record PostFilter(
    Guid? WorkspaceId,
    string? Type,
    string? Status,
    string? Platform,
    Guid? CampaignId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Search,
    int Page,
    int PageSize);

/// <summary>
/// Posts and everything that belongs to them. Every method opens a connection for the given organization and user, so row-level
/// security applies; the organization is also in each WHERE. A post is read and written whole (<see cref="ContentPostAggregate"/>).
/// </summary>
public interface IContentPostRepository
{
    Task<PostListResult> ListAsync(Guid agencyId, Guid userId, PostFilter filter, CancellationToken cancellationToken);

    Task<ContentPostAggregate?> GetAsync(Guid agencyId, Guid userId, Guid postId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid agencyId, Guid userId, Guid postId, CancellationToken cancellationToken);

    /// <summary>Writes the post and all its children in one transaction. The revision trigger writes revision 1.</summary>
    Task InsertAsync(Guid userId, ContentPostAggregate post, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the post's columns and children in one transaction, if its <c>updated_at</c> is still
    /// <paramref name="expectedUpdatedAt"/> and nothing is publishing.
    /// </summary>
    Task<SaveOutcome> UpdateAsync(Guid userId, ContentPostAggregate post, DateTimeOffset expectedUpdatedAt, CancellationToken cancellationToken);

    /// <summary>Deletes the post, with its children, only if its status is one of <paramref name="allowedStatuses"/>. False when it was not.</summary>
    Task<bool> DeleteAsync(Guid agencyId, Guid userId, Guid postId, IReadOnlyCollection<string> allowedStatuses, CancellationToken cancellationToken);

    /// <summary>Archives the post if its status is one of <paramref name="allowedFrom"/>. False when it was not.</summary>
    Task<bool> ArchiveAsync(Guid agencyId, Guid userId, Guid postId, IReadOnlyCollection<string> allowedFrom, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Restores an archived post to published (if it had been) or draft. The new status, or null when it was not archived.</summary>
    Task<string?> RestoreAsync(Guid agencyId, Guid userId, Guid postId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContentRevision>> ListRevisionsAsync(Guid agencyId, Guid userId, Guid postId, CancellationToken cancellationToken);
}
