using Npgsql;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>Small raw-SQL helpers for arranging and checking test data. All values are bound parameters.</summary>
public static class DbTestSupport
{
    /// <summary>Inserts an organization. <paramref name="businessModel"/> is the stored name: company, agency or creator.</summary>
    public static async Task<Guid> InsertAgencyAsync(
        NpgsqlConnection owner, string name = "Test agency", string businessModel = "agency")
    {
        await using var command = new NpgsqlCommand(
            "INSERT INTO agencies (name, business_model) VALUES ($1, $2) RETURNING id", owner)
        {
            Parameters =
            {
                new NpgsqlParameter<string> { TypedValue = name },
                new NpgsqlParameter<string> { TypedValue = businessModel }
            }
        };
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    public static async Task<Guid> InsertUserAsync(NpgsqlConnection owner, string email, string fullName = "Test User")
    {
        await using var command = new NpgsqlCommand(
            "INSERT INTO users (email, password_hash, full_name) VALUES ($1, 'not-a-real-hash', $2) RETURNING id", owner)
        {
            Parameters =
            {
                new NpgsqlParameter<string> { TypedValue = email },
                new NpgsqlParameter<string> { TypedValue = fullName }
            }
        };
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Adds a member. Their business model is read from the organization, so a role the model does not
    /// allow is refused by the database, exactly as it would be in production.
    /// </summary>
    public static Task<int> InsertMemberAsync(
        NpgsqlConnection owner, Guid agencyId, Guid userId, string role, DateTimeOffset? lastActiveAt = null) =>
        ExecuteAsync(
            owner,
            """
            INSERT INTO agency_members (agency_id, user_id, business_model, role, last_active_at)
            SELECT a.id, $2, a.business_model, $3, $4 FROM agencies a WHERE a.id = $1
            """,
            agencyId, userId, role, (lastActiveAt ?? DateTimeOffset.UtcNow).UtcDateTime);

    public static Task<int> InsertSiteAdminAsync(NpgsqlConnection owner, Guid userId) =>
        ExecuteAsync(owner, "INSERT INTO site_admins (user_id) VALUES ($1)", userId);

    public static async Task<Guid> InsertWorkspaceAsync(NpgsqlConnection owner, Guid agencyId, string name = "Test workspace")
    {
        await using var command = new NpgsqlCommand("INSERT INTO workspaces (agency_id, name) VALUES ($1, $2) RETURNING id", owner)
        {
            Parameters =
            {
                new NpgsqlParameter<Guid> { TypedValue = agencyId },
                new NpgsqlParameter<string> { TypedValue = name }
            }
        };
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    public static async Task<Guid> InsertRefreshTokenAsync(NpgsqlConnection owner, Guid agencyId, Guid userId, string tokenHash)
    {
        await using var command = new NpgsqlCommand(
            "INSERT INTO refresh_tokens (agency_id, user_id, token_hash, expires_at) VALUES ($1, $2, $3, now() + interval '1 day') RETURNING id", owner)
        {
            Parameters =
            {
                new NpgsqlParameter<Guid> { TypedValue = agencyId },
                new NpgsqlParameter<Guid> { TypedValue = userId },
                new NpgsqlParameter<string> { TypedValue = tokenHash }
            }
        };
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Runs a statement with positional parameters ($1, $2, ...) and returns the rows affected.</summary>
    public static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }

        return await command.ExecuteNonQueryAsync();
    }

    public static async Task<long> CountAsync(NpgsqlConnection connection, string table)
    {
        // Table names come from test code, never from input.
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public static async Task<T?> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }
}
