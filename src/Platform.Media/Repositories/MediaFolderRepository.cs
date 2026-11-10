using Dapper;
using Platform.Infrastructure.Data;
using Platform.Media.Interfaces;
using Platform.Media.Models;

namespace Platform.Media.Repositories;

/// <summary>Folders and tags of a workspace, with how many files each holds.</summary>
public sealed class MediaFolderRepository(IDbConnectionFactory connections) : IMediaFolderRepository
{
    private sealed class CountsRow
    {
        public int Total { get; set; }
        public int Uncategorized { get; set; }
    }

    public async Task<IReadOnlyList<MediaFolderRow>> ListAsync(Guid agencyId, Guid userId, Guid workspaceId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        return (await connection.QueryAsync<MediaFolderRow>(new CommandDefinition(
            """
            SELECT f.id, f.name, f.parent_folder_id, count(fa.media_asset_id)::int AS asset_count
            FROM media_folders f
            LEFT JOIN media_folder_assets fa ON fa.folder_id = f.id AND fa.agency_id = f.agency_id
            WHERE f.agency_id = @agencyId AND f.workspace_id = @workspaceId
            GROUP BY f.id
            ORDER BY lower(f.name), f.id
            """,
            new { agencyId, workspaceId },
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<MediaTagCountRow>> ListTagsAsync(Guid agencyId, Guid userId, Guid workspaceId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        return (await connection.QueryAsync<MediaTagCountRow>(new CommandDefinition(
            """
            SELECT t.id, t.name, count(at.media_asset_id)::int AS asset_count
            FROM media_tags t
            LEFT JOIN media_asset_tags at ON at.tag_id = t.id AND at.agency_id = t.agency_id
            WHERE t.agency_id = @agencyId AND t.workspace_id = @workspaceId
            GROUP BY t.id
            ORDER BY count(at.media_asset_id) DESC, lower(t.name), t.id
            """,
            new { agencyId, workspaceId },
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<MediaLibraryCounts> CountsAsync(Guid agencyId, Guid userId, Guid workspaceId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        var row = await connection.QuerySingleAsync<CountsRow>(new CommandDefinition(
            """
            SELECT count(*)::int AS total,
                   (count(*) FILTER (WHERE NOT EXISTS (SELECT 1 FROM media_folder_assets fa WHERE fa.media_asset_id = a.id)))::int AS uncategorized
            FROM media_assets a
            WHERE a.agency_id = @agencyId AND a.workspace_id = @workspaceId
            """,
            new { agencyId, workspaceId },
            cancellationToken: cancellationToken));
        return new MediaLibraryCounts(row.Total, row.Uncategorized);
    }

    public async Task<bool> InsertAsync(Guid userId, MediaFolder folder, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(folder.AgencyId, userId, cancellationToken);
        var inserted = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO media_folders (id, agency_id, workspace_id, parent_folder_id, name, created_by)
            VALUES (@Id, @AgencyId, @WorkspaceId, @ParentFolderId, @Name, @userId)
            ON CONFLICT DO NOTHING
            """,
            new { folder.Id, folder.AgencyId, folder.WorkspaceId, folder.ParentFolderId, folder.Name, userId },
            cancellationToken: cancellationToken));
        return inserted == 1;
    }

    public async Task<bool> DeleteAsync(Guid agencyId, Guid userId, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        var deleted = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM media_folders WHERE id = @id AND agency_id = @agencyId",
            new { id, agencyId },
            cancellationToken: cancellationToken));
        return deleted > 0;
    }

    public async Task<bool> ExistsInWorkspaceAsync(Guid agencyId, Guid userId, Guid folderId, Guid workspaceId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM media_folders WHERE id = @folderId AND workspace_id = @workspaceId AND agency_id = @agencyId)",
            new { folderId, workspaceId, agencyId },
            cancellationToken: cancellationToken));
    }
}
