using Dapper;
using Npgsql;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Helpers.Exceptions;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Repositories;

/// <summary>
/// Site admin queries. No agency is involved: the connection carries only the user, and the
/// database decides from that user alone whether the report may be produced.
/// </summary>
public sealed class SiteAdminRepository(IDbConnectionFactory connections) : ISiteAdminRepository
{
    public async Task<bool> IsSiteAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        // The own_select policy lets a user see their own row only, so this is true only for a site admin.
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM site_admins WHERE user_id = @userId)",
            new { userId },
            cancellationToken: cancellationToken));
    }

    public async Task<SiteOverviewRecord> GetOverviewAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        try
        {
            return await connection.QuerySingleAsync<SiteOverviewRecord>(new CommandDefinition(
                """
                SELECT agency_count, trial_agency_count, paid_agency_count, user_count, workspace_count, member_count
                FROM site_admin_overview()
                """,
                cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            throw NotASiteAdmin();
        }
    }

    public async Task<SiteAgencyPage> ListAgenciesAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        try
        {
            var rows = (await connection.QueryAsync<SiteAgencyRecord>(new CommandDefinition(
                """
                SELECT id, name, plan, created_at, member_count, workspace_count, total_count
                FROM site_admin_list_agencies(@limit, @offset)
                """,
                new { limit = pageSize, offset = (page - 1) * pageSize },
                cancellationToken: cancellationToken))).AsList();

            // A page past the end returns no rows, and so no total. Ask again for the count alone.
            var total = rows.Count > 0 ? rows[0].TotalCount : await CountAgenciesAsync(connection, cancellationToken);
            return new SiteAgencyPage(rows, total);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            throw NotASiteAdmin();
        }
    }

    private static async Task<long> CountAgenciesAsync(NpgsqlConnection connection, CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT agency_count FROM site_admin_overview()", cancellationToken: cancellationToken));

    private static ForbiddenException NotASiteAdmin() =>
        new("Site admin access is required.", "not_site_admin");
}
