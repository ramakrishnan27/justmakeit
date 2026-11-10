using Dapper;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Repositories;

public sealed class MembershipRepository(IDbConnectionFactory connections) : IMembershipRepository
{
    public async Task<IReadOnlyList<MembershipRecord>> ListForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Only the user is known here, not an agency. The own_select policy on agency_members and the
        // member_select policy on agencies both key on the user, so exactly their own memberships show.
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        var rows = await connection.QueryAsync<MembershipRecord>(new CommandDefinition(
            """
            SELECT m.agency_id, a.name AS agency_name, m.business_model, m.role, m.is_active, a.is_active AS organization_is_active
            FROM agency_members m
            JOIN agencies a ON a.id = m.agency_id
            WHERE m.user_id = @userId
            ORDER BY m.last_active_at DESC, a.name, m.agency_id
            """,
            new { userId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<MembershipRecord?> FindAsync(Guid userId, Guid agencyId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<MembershipRecord>(new CommandDefinition(
            """
            SELECT m.agency_id, a.name AS agency_name, m.business_model, m.role, m.is_active, a.is_active AS organization_is_active
            FROM agency_members m
            JOIN agencies a ON a.id = m.agency_id
            WHERE m.user_id = @userId AND m.agency_id = @agencyId
            """,
            new { userId, agencyId },
            cancellationToken: cancellationToken));
    }

    public async Task TouchAsync(Guid agencyId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE agency_members SET last_active_at = @now WHERE agency_id = @agencyId AND user_id = @userId",
            new { now, agencyId, userId },
            cancellationToken: cancellationToken));
    }
}
