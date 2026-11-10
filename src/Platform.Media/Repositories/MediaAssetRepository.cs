using Dapper;
using Npgsql;
using Platform.Helpers.Media;
using Platform.Infrastructure.Data;
using Platform.Media.Interfaces;
using Platform.Media.Models;

namespace Platform.Media.Repositories;

/// <summary>
/// Files over Dapper and plain SQL. Every method opens a connection for the organization and user it is given (row-level security) and
/// writes the organization into each WHERE as well. Every value, including the search words, is a parameter; the order of the list is
/// chosen from a fixed set in this class, never from text that was sent.
/// </summary>
public sealed class MediaAssetRepository(IDbConnectionFactory connections) : IMediaAssetRepository
{
    private sealed class TypeCountRow
    {
        public string MediaType { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    private sealed class SummaryRow
    {
        public Guid Id { get; set; }
        public Guid WorkspaceId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
    }

    private const string AssetColumns =
        """
        a.id, a.workspace_id, a.file_name, a.mime_type, a.media_type, a.file_size_bytes,
        (a.metadata->>'width')::int AS width, (a.metadata->>'height')::int AS height, a.created_at,
        (SELECT count(*)::int FROM media_asset_usages u WHERE u.media_asset_id = a.id AND u.agency_id = a.agency_id) AS usage_count
        """;

    // Everything the list is narrowed by except the type, so the same text counts the tabs and fills the page. The search matches a
    // part of the file name or of a tag name; strpos is a function, not a LIKE pattern, so a % or _ that was typed means itself.
    private const string Predicate =
        """
        a.agency_id = @agencyId AND a.workspace_id = @workspaceId
        AND (@folderId::uuid IS NULL OR EXISTS (
                SELECT 1 FROM media_folder_assets fa WHERE fa.media_asset_id = a.id AND fa.folder_id = @folderId))
        AND (@uncategorized = false OR NOT EXISTS (SELECT 1 FROM media_folder_assets fa WHERE fa.media_asset_id = a.id))
        AND (@tagId::uuid IS NULL OR EXISTS (
                SELECT 1 FROM media_asset_tags at WHERE at.media_asset_id = a.id AND at.tag_id = @tagId))
        AND (@q::text IS NULL
             OR strpos(lower(a.file_name), lower(@q)) > 0
             OR EXISTS (SELECT 1 FROM media_asset_tags at JOIN media_tags t ON t.id = at.tag_id AND t.agency_id = at.agency_id
                        WHERE at.media_asset_id = a.id AND strpos(lower(t.name), lower(@q)) > 0))
        AND (@minSize::bigint IS NULL OR a.file_size_bytes >= @minSize)
        AND (@maxSize::bigint IS NULL OR a.file_size_bytes < @maxSize)
        AND (@fromDate::timestamptz IS NULL OR a.created_at >= @fromDate)
        """;

    private static string OrderBy(MediaSort sort) => sort switch
    {
        MediaSort.Oldest => "a.created_at ASC, a.id ASC",
        MediaSort.Name => "lower(a.file_name) ASC, a.id ASC",
        MediaSort.Size => "a.file_size_bytes DESC, a.id ASC",
        _ => "a.created_at DESC, a.id DESC"
    };

    // --- Reading --------------------------------------------------------------------------------

    public async Task<MediaListResult> ListAsync(Guid agencyId, Guid userId, MediaListCriteria criteria, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);

        var filter = new
        {
            agencyId,
            workspaceId = criteria.WorkspaceId,
            folderId = criteria.FolderId,
            uncategorized = criteria.Uncategorized,
            tagId = criteria.TagId,
            q = criteria.Search,
            minSize = criteria.MinSizeBytes,
            maxSize = criteria.MaxSizeBytes,
            fromDate = criteria.From
        };

        // The counts per type ignore the type filter, so the tabs keep their numbers while one is open.
        var counts = (await connection.QueryAsync<TypeCountRow>(new CommandDefinition(
            $"SELECT a.media_type, count(*)::int AS count FROM media_assets a WHERE {Predicate} GROUP BY a.media_type",
            filter,
            cancellationToken: cancellationToken))).ToDictionary(row => row.MediaType, row => row.Count);

        var type = criteria.Type?.ToDb();
        var items = (await connection.QueryAsync<MediaAssetRow>(new CommandDefinition(
            $"""
            SELECT {AssetColumns}
            FROM media_assets a
            WHERE {Predicate} AND (@type::text IS NULL OR a.media_type = @type)
            ORDER BY {OrderBy(criteria.Sort)}
            LIMIT @limit OFFSET @offset
            """,
            new
            {
                filter.agencyId, filter.workspaceId, filter.folderId, filter.uncategorized, filter.tagId, filter.q, filter.minSize,
                filter.maxSize, filter.fromDate, type, limit = criteria.PageSize, offset = (criteria.Page - 1) * criteria.PageSize
            },
            cancellationToken: cancellationToken))).AsList();

        var (tags, folders) = await ChildrenAsync(connection, items.Select(item => item.Id), cancellationToken);
        return new MediaListResult { Items = items, Tags = tags, Folders = folders, TypeCounts = counts };
    }

    public async Task<MediaAssetDetail?> GetAsync(Guid agencyId, Guid userId, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<MediaAssetRow>(new CommandDefinition(
            $"SELECT {AssetColumns} FROM media_assets a WHERE a.id = @id AND a.agency_id = @agencyId",
            new { id, agencyId },
            cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        var (tags, folders) = await ChildrenAsync(connection, [id], cancellationToken);
        return new MediaAssetDetail
        {
            Asset = row,
            Tags = tags.GetValueOrDefault(id) ?? [],
            FolderIds = folders.GetValueOrDefault(id) ?? []
        };
    }

    // The tags and the folders of some files, two queries for the whole page rather than two per file.
    private static async Task<(IReadOnlyDictionary<Guid, IReadOnlyList<MediaAssetTagRow>> Tags, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> Folders)>
        ChildrenAsync(NpgsqlConnection connection, IEnumerable<Guid> assetIds, CancellationToken cancellationToken)
    {
        var ids = assetIds.ToArray();
        if (ids.Length == 0)
        {
            return (new Dictionary<Guid, IReadOnlyList<MediaAssetTagRow>>(), new Dictionary<Guid, IReadOnlyList<Guid>>());
        }

        var tags = await connection.QueryAsync<MediaAssetTagRow>(new CommandDefinition(
            """
            SELECT at.media_asset_id AS asset_id, t.id AS tag_id, t.name
            FROM media_asset_tags at
            JOIN media_tags t ON t.id = at.tag_id AND t.agency_id = at.agency_id
            WHERE at.media_asset_id = ANY(@ids)
            ORDER BY lower(t.name), t.id
            """,
            new { ids = new UuidArray(ids) },
            cancellationToken: cancellationToken));

        var folders = await connection.QueryAsync<MediaAssetFolderRow>(new CommandDefinition(
            "SELECT media_asset_id AS asset_id, folder_id FROM media_folder_assets WHERE media_asset_id = ANY(@ids) ORDER BY created_at, folder_id",
            new { ids = new UuidArray(ids) },
            cancellationToken: cancellationToken));

        return (
            tags.GroupBy(row => row.AssetId).ToDictionary(group => group.Key, group => (IReadOnlyList<MediaAssetTagRow>)group.ToList()),
            folders.GroupBy(row => row.AssetId).ToDictionary(group => group.Key, group => (IReadOnlyList<Guid>)group.Select(row => row.FolderId).ToList()));
    }

    public async Task<MediaFileRecord?> GetFileAsync(Guid agencyId, Guid userId, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<MediaFileRecord>(new CommandDefinition(
            "SELECT id, storage_key, mime_type, file_name, media_type FROM media_assets WHERE id = @id AND agency_id = @agencyId",
            new { id, agencyId },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<MediaAssetSummary>> FindAsync(
        Guid agencyId, Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        var rows = await connection.QueryAsync<SummaryRow>(new CommandDefinition(
            """
            SELECT a.id, a.workspace_id, a.file_name, a.media_type, a.mime_type, a.file_size_bytes AS size_bytes,
                   (a.metadata->>'width')::int AS width, (a.metadata->>'height')::int AS height
            FROM media_assets a
            WHERE a.agency_id = @agencyId AND a.id = ANY(@ids)
            ORDER BY a.created_at, a.id
            """,
            new { agencyId, ids = new UuidArray(ids) },
            cancellationToken: cancellationToken));

        return rows
            .Select(row => new MediaAssetSummary(row.Id, row.WorkspaceId, row.FileName, row.MediaType, row.MimeType, row.SizeBytes, row.Width, row.Height))
            .ToList();
    }

    // --- Writing --------------------------------------------------------------------------------

    public async Task InsertAsync(Guid userId, MediaAsset asset, IReadOnlyList<string> tags, Guid? folderId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(asset.AgencyId, userId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var metadata = new Dictionary<string, int>();
        if (asset.Width is { } width && asset.Height is { } height)
        {
            metadata["width"] = width;
            metadata["height"] = height;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO media_assets
                (id, agency_id, workspace_id, uploaded_by, file_name, mime_type, media_type, file_size_bytes, storage_key, metadata)
            VALUES
                (@Id, @AgencyId, @WorkspaceId, @UploadedBy, @FileName, @MimeType, @MediaType, @FileSizeBytes, @StorageKey, @Metadata)
            """,
            new
            {
                asset.Id, asset.AgencyId, asset.WorkspaceId, asset.UploadedBy, asset.FileName, asset.MimeType, asset.MediaType,
                asset.FileSizeBytes, asset.StorageKey, Metadata = JsonbParameter.From(metadata)
            },
            transaction,
            cancellationToken: cancellationToken));

        foreach (var name in tags)
        {
            // A tag is made the first time it is used; the unique index on (workspace, lower(name)) makes two uploads that
            // make it at once agree, and the select finds the one that won.
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO media_tags (agency_id, workspace_id, name) VALUES (@AgencyId, @WorkspaceId, @name) ON CONFLICT DO NOTHING",
                new { asset.AgencyId, asset.WorkspaceId, name },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO media_asset_tags (agency_id, media_asset_id, tag_id)
                SELECT @AgencyId, @Id, t.id FROM media_tags t
                WHERE t.agency_id = @AgencyId AND t.workspace_id = @WorkspaceId AND lower(t.name) = lower(@name)
                ON CONFLICT DO NOTHING
                """,
                new { asset.AgencyId, asset.Id, asset.WorkspaceId, name },
                transaction,
                cancellationToken: cancellationToken));
        }

        if (folderId is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO media_folder_assets (agency_id, folder_id, media_asset_id) VALUES (@AgencyId, @folderId, @Id)",
                new { asset.AgencyId, asset.Id, folderId },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<MediaDeleteResult> DeleteAsync(Guid agencyId, Guid userId, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var key = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT storage_key FROM media_assets WHERE id = @id AND agency_id = @agencyId",
            new { id, agencyId },
            transaction,
            cancellationToken: cancellationToken));
        if (key is null)
        {
            return new MediaDeleteResult(DeleteOutcome.NotFound, []);
        }

        var variants = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT storage_key FROM media_asset_variants WHERE media_asset_id = @id AND agency_id = @agencyId",
            new { id, agencyId },
            transaction,
            cancellationToken: cancellationToken))).ToList();

        try
        {
            // A file that something uses (a usage row, a post) is refused by the foreign keys, at the end of this statement: the
            // database is the one that knows, however many features come to use files.
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM media_assets WHERE id = @id AND agency_id = @agencyId",
                new { id, agencyId },
                transaction,
                cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return new MediaDeleteResult(DeleteOutcome.InUse, []);
        }

        await transaction.CommitAsync(cancellationToken);
        return new MediaDeleteResult(DeleteOutcome.Deleted, [key, .. variants]);
    }

    public async Task<OrganizeOutcome> OrganizeAsync(
        Guid agencyId, Guid userId, IReadOnlyCollection<Guid> ids, Guid? folderId, OrganizeMode mode, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Row-level security shows only this organization's files, so a file of another organization counts as missing.
        var workspaces = (await connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT DISTINCT workspace_id FROM media_assets WHERE agency_id = @agencyId AND id = ANY(@ids)",
            new { agencyId, ids = new UuidArray(ids) },
            transaction,
            cancellationToken: cancellationToken))).ToList();
        var found = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM media_assets WHERE agency_id = @agencyId AND id = ANY(@ids)",
            new { agencyId, ids = new UuidArray(ids) },
            transaction,
            cancellationToken: cancellationToken));
        if (found != ids.Count)
        {
            return OrganizeOutcome.AssetNotFound;
        }

        if (folderId is not null)
        {
            var folderWorkspace = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                "SELECT workspace_id FROM media_folders WHERE id = @folderId AND agency_id = @agencyId",
                new { folderId, agencyId },
                transaction,
                cancellationToken: cancellationToken));
            if (folderWorkspace is null)
            {
                return OrganizeOutcome.FolderNotFound;
            }

            // A folder belongs to one workspace, and so do the files in it.
            if (workspaces.Count != 1 || workspaces[0] != folderWorkspace.Value)
            {
                return OrganizeOutcome.WorkspaceMismatch;
            }
        }

        try
        {
            if (mode == OrganizeMode.Move)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM media_folder_assets WHERE agency_id = @agencyId AND media_asset_id = ANY(@ids)",
                    new { agencyId, ids = new UuidArray(ids) },
                    transaction,
                    cancellationToken: cancellationToken));
            }

            if (folderId is not null)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO media_folder_assets (agency_id, folder_id, media_asset_id)
                    SELECT @agencyId, @folderId, a.id FROM media_assets a WHERE a.agency_id = @agencyId AND a.id = ANY(@ids)
                    ON CONFLICT DO NOTHING
                    """,
                    new { agencyId, folderId, ids = new UuidArray(ids) },
                    transaction,
                    cancellationToken: cancellationToken));
            }
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            // The file or the folder was deleted a moment ago, between the checks above and here.
            return OrganizeOutcome.AssetNotFound;
        }

        await transaction.CommitAsync(cancellationToken);
        return OrganizeOutcome.Done;
    }
}
