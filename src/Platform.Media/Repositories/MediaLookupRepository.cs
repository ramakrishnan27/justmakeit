using Dapper;
using Platform.Infrastructure.Data;
using Platform.Media.Interfaces;

namespace Platform.Media.Repositories;

/// <summary>What Media needs to know about the organization's workspaces. Read with this project's own query: the Auth folder's repositories are off limits here.</summary>
public sealed class MediaLookupRepository(IDbConnectionFactory connections) : IMediaLookupRepository
{
    public async Task<bool> WorkspaceExistsAsync(Guid agencyId, Guid userId, Guid workspaceId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM workspaces WHERE id = @workspaceId AND agency_id = @agencyId)",
            new { workspaceId, agencyId },
            cancellationToken: cancellationToken));
    }
}
