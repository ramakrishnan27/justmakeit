using Dapper;
using Platform.Infrastructure.Data;
using Platform.Media.Interfaces;

namespace Platform.Media.Repositories;

/// <summary>
/// Writes <c>media_asset_usages</c> over Dapper and plain SQL, for the organization and user it is given (row-level security) with the
/// organization in each WHERE as well. The workspace of a usage is taken from the file, never from the caller, so a usage can never
/// say a file is in another workspace than it is.
/// </summary>
public sealed class MediaUsageRepository(IDbConnectionFactory connections) : IMediaUsageRepository
{
    public async Task ReplaceAsync(
        Guid agencyId, Guid userId, string moduleName, string entityType, Guid entityId, IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var parameters = new { agencyId, moduleName, entityType, entityId, ids = new UuidArray(assetIds) };

        // What the thing no longer uses goes first, then what it newly uses comes in. A file that is gone, or that is another
        // organization's, is not selected, so it gets no usage (which is also why the insert cannot break the foreign key).
        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM media_asset_usages
            WHERE agency_id = @agencyId AND module_name = @moduleName AND entity_type = @entityType AND entity_id = @entityId
              AND NOT (media_asset_id = ANY(@ids))
            """,
            parameters,
            transaction,
            cancellationToken: cancellationToken));

        if (assetIds.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO media_asset_usages (agency_id, workspace_id, media_asset_id, module_name, entity_type, entity_id)
                SELECT a.agency_id, a.workspace_id, a.id, @moduleName, @entityType, @entityId
                FROM media_assets a
                WHERE a.agency_id = @agencyId AND a.id = ANY(@ids)
                ON CONFLICT (media_asset_id, module_name, entity_type, entity_id) DO NOTHING
                """,
                parameters,
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
