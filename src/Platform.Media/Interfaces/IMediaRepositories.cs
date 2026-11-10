using Platform.Helpers.Media;
using Platform.Media.Models;

namespace Platform.Media.Interfaces;

// Every method opens a connection for the organization and user it is given, so row-level security applies, and writes the organization
// into each WHERE as well.

public interface IMediaAssetRepository
{
    Task<MediaListResult> ListAsync(Guid agencyId, Guid userId, MediaListCriteria criteria, CancellationToken cancellationToken);

    Task<MediaAssetDetail?> GetAsync(Guid agencyId, Guid userId, Guid id, CancellationToken cancellationToken);

    Task<MediaFileRecord?> GetFileAsync(Guid agencyId, Guid userId, Guid id, CancellationToken cancellationToken);

    /// <summary>The files among <paramref name="ids"/> that exist for the organization (the contract Content Studio uses).</summary>
    Task<IReadOnlyList<MediaAssetSummary>> FindAsync(
        Guid agencyId, Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Writes the file's row, its tags (made when they are new) and its folder, in one transaction.</summary>
    Task InsertAsync(Guid userId, MediaAsset asset, IReadOnlyList<string> tags, Guid? folderId, CancellationToken cancellationToken);

    /// <summary>Deletes the row and what hangs on it. A file that something uses is refused (<see cref="DeleteOutcome.InUse"/>).</summary>
    Task<MediaDeleteResult> DeleteAsync(Guid agencyId, Guid userId, Guid id, CancellationToken cancellationToken);

    /// <summary>Puts the files in the folder (or, for a move to no folder, in none), all or nothing.</summary>
    Task<OrganizeOutcome> OrganizeAsync(
        Guid agencyId, Guid userId, IReadOnlyCollection<Guid> ids, Guid? folderId, OrganizeMode mode, CancellationToken cancellationToken);
}

public interface IMediaFolderRepository
{
    Task<IReadOnlyList<MediaFolderRow>> ListAsync(Guid agencyId, Guid userId, Guid workspaceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MediaTagCountRow>> ListTagsAsync(Guid agencyId, Guid userId, Guid workspaceId, CancellationToken cancellationToken);

    Task<MediaLibraryCounts> CountsAsync(Guid agencyId, Guid userId, Guid workspaceId, CancellationToken cancellationToken);

    /// <summary>False when the workspace already has a folder with that name.</summary>
    Task<bool> InsertAsync(Guid userId, MediaFolder folder, CancellationToken cancellationToken);

    /// <summary>Deletes the folder and the folders in it; the files stay. False when there was no such folder.</summary>
    Task<bool> DeleteAsync(Guid agencyId, Guid userId, Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsInWorkspaceAsync(Guid agencyId, Guid userId, Guid folderId, Guid workspaceId, CancellationToken cancellationToken);
}

public interface IMediaLookupRepository
{
    Task<bool> WorkspaceExistsAsync(Guid agencyId, Guid userId, Guid workspaceId, CancellationToken cancellationToken);
}

/// <summary>Where files are used (the table <c>media_asset_usages</c>), written by the owner of the usage through the contract in Core.</summary>
public interface IMediaUsageRepository
{
    /// <summary>Makes the usages of this thing exactly those of the files in <paramref name="assetIds"/> that exist, in one transaction.</summary>
    Task ReplaceAsync(
        Guid agencyId, Guid userId, string moduleName, string entityType, Guid entityId, IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken);
}
