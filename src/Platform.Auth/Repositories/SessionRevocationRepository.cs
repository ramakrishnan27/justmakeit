using Dapper;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Repositories;

public sealed class SessionRevocationRepository(IDbConnectionFactory connections) : ISessionRevocationRepository
{
    public async Task<SessionRevocationSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken)
    {
        // The function returns only recent rows and needs no user: the API reads it for itself.
        await using var connection = await connections.OpenAsync(null, null, cancellationToken);
        var rows = await connection.QueryAsync<SessionRevocationRecord>(new CommandDefinition(
            "SELECT agency_id, user_id, valid_after FROM session_revocations_snapshot()",
            cancellationToken: cancellationToken));
        return SessionRevocationSnapshot.From(rows);
    }
}
