using Dapper;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Repositories;

public sealed class AgencyRepository(IDbConnectionFactory connections) : IAgencyRepository
{
    public async Task<AgencyRecord?> GetAsync(Guid agencyId, Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AgencyRecord>(new CommandDefinition(
            "SELECT id, name, plan FROM agencies WHERE id = @agencyId",
            new { agencyId },
            cancellationToken: cancellationToken));
    }
}
