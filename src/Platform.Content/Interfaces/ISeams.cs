using Platform.Helpers.Media;

namespace Platform.Content.Interfaces;

// Three things Content Studio will use that do not exist in the system yet. Each is an interface with a "not available"
// implementation, so the rest of the code asks the interface (and the page asks /v1/content/capabilities) instead of assuming. When the
// real module exists it registers its own implementation and nothing in Content changes.

/// <summary>The organization's media library (images and videos to attach to a post).</summary>
public interface IContentMediaLibrary
{
    /// <summary>Whether there is a Media Library to take files from.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// The files among <paramref name="assetIds"/> that exist for the organization (a file of another organization is not in the
    /// answer). Empty when there is no Media Library. Callers have already checked the person's permission.
    /// </summary>
    Task<IReadOnlyList<MediaAssetSummary>> FindAsync(
        Guid agencyId, Guid userId, IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken);
}

/// <summary>
/// Tells the Media Library which files a post uses, so the library can show "Used in 1 post" and not offer to delete a file that is in
/// use. A post's save does not wait for it and does not fail with it: a failure is logged and the next save sets it right.
/// </summary>
public interface IContentMediaUsage
{
    /// <summary>Makes the files in <paramref name="assetIds"/> the files this post uses (an empty list: none).</summary>
    Task ReplaceAsync(Guid agencyId, Guid userId, Guid postId, IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken);

    /// <summary>The post is gone: it uses no file any more.</summary>
    Task RemoveAllAsync(Guid agencyId, Guid userId, Guid postId, CancellationToken cancellationToken);
}

/// <summary>An AI assistant that drafts captions, hashtags and rewrites.</summary>
public interface IContentAiAssistant
{
    bool IsAvailable { get; }
}

/// <summary>The organization's campaigns, which a post can belong to.</summary>
public interface ICampaignLookup
{
    bool IsAvailable { get; }

    Task<bool> ExistsAsync(Guid agencyId, Guid userId, Guid campaignId, CancellationToken cancellationToken);
}
