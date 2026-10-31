using Dapper;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Repositories;

public sealed class WorkspaceRepository(IDbConnectionFactory connections) : IWorkspaceRepository
{
    public async Task<IReadOnlyList<WorkspaceRecord>> ListAsync(Guid agencyId, Guid userId, CancellationToken cancellationToken)
    {
        // The agency filter is written out even though row-level security enforces it as well.
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        var rows = await connection.QueryAsync<WorkspaceRecord>(new CommandDefinition(
            "SELECT id, agency_id, name, created_at FROM workspaces WHERE agency_id = @agencyId ORDER BY created_at, id",
            new { agencyId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task InsertAsync(WorkspaceRecord workspace, Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(workspace.AgencyId, userId, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO workspaces (id, agency_id, name, created_at) VALUES (@Id, @AgencyId, @Name, @CreatedAt)",
            workspace,
            cancellationToken: cancellationToken));
    }
}
