using Npgsql;
using Platform.Helpers.Tenancy;

namespace Platform.Infrastructure.Data;

public sealed class NpgsqlConnectionFactory(NpgsqlDataSource dataSource, ITenantContext tenant) : IDbConnectionFactory
{
    // Session-level (not transaction-local) so the values hold across several transactions on one connection.
    private const string SetTenantSql =
        "SELECT set_config('app.agency_id', $1, false), set_config('app.user_id', $2, false)";

    public Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken) =>
        OpenAsync(tenant.AgencyId, tenant.UserId, cancellationToken);

    public async Task<NpgsqlConnection> OpenAsync(Guid? agencyId, Guid? userId, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            // Always set both values, empty when absent, so nothing from a previous use of a pooled
            // connection can carry over. The policies read an empty value as NULL and match no rows.
            await using var command = new NpgsqlCommand(SetTenantSql, connection)
            {
                Parameters =
                {
                    new NpgsqlParameter<string> { TypedValue = agencyId?.ToString() ?? string.Empty },
                    new NpgsqlParameter<string> { TypedValue = userId?.ToString() ?? string.Empty }
                }
            };
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
