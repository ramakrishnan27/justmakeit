using Platform.Content.Entities;

namespace Platform.Content.Interfaces;

/// <summary>
/// What Content needs to know about the rest of the system, read with its own narrow queries: the Auth folder's repositories are
/// off limits to this project (it must not use that namespace).
/// </summary>
public interface IContentLookupRepository
{
    /// <summary>Whether the workspace exists in the organization.</summary>
    Task<bool> WorkspaceExistsAsync(Guid agencyId, Guid userId, Guid workspaceId, CancellationToken cancellationToken);

    /// <summary>
    /// The platforms (stored text) that the Site admin has enabled for the organization: the channels of <c>member_list_platforms()</c>
    /// whose name is a platform. Empty when the caller may not see the platforms.
    /// </summary>
    Task<IReadOnlySet<string>> EnabledPlatformsAsync(Guid agencyId, Guid userId, CancellationToken cancellationToken);
}

public interface IHashtagRepository
{
    /// <summary>The hashtags the organization has used (optionally in one workspace, optionally starting with a prefix), most used first.</summary>
    Task<IReadOnlyList<(string Hashtag, int Uses)>> SuggestAsync(
        Guid agencyId, Guid userId, Guid? workspaceId, string? prefix, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContentTag>> ListTagsAsync(Guid agencyId, Guid userId, Guid? workspaceId, CancellationToken cancellationToken);

    /// <summary>Creates the tag. False when the workspace already has a tag of that name (any letter case).</summary>
    Task<bool> InsertTagAsync(Guid userId, ContentTag tag, CancellationToken cancellationToken);
}

public interface ITemplateRepository
{
    Task<IReadOnlyList<ContentTemplate>> ListAsync(Guid agencyId, Guid userId, Guid? workspaceId, string? contentType, CancellationToken cancellationToken);

    /// <summary>Creates the template. False when the workspace already has one of that name (any letter case).</summary>
    Task<bool> InsertAsync(ContentTemplate template, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid agencyId, Guid userId, Guid templateId, CancellationToken cancellationToken);
}
