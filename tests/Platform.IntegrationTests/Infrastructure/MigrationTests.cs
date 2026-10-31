using Npgsql;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class MigrationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await postgres.CreateDatabaseAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Theory]
    [InlineData("agencies")]
    [InlineData("users")]
    [InlineData("agency_members")]
    [InlineData("workspaces")]
    [InlineData("refresh_tokens")]
    public async Task CoreTable_ExistsWithRowLevelSecurityEnabledAndForced(string table)
    {
        await using var owner = await _db.OpenOwnerAsync();
        await using var command = new NpgsqlCommand(
            "SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE oid = to_regclass($1)", owner)
        {
            Parameters = { new NpgsqlParameter<string> { TypedValue = "public." + table } }
        };

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), $"Table {table} does not exist.");
        Assert.True(reader.GetBoolean(0), $"Row-level security is not enabled on {table}.");
        Assert.True(reader.GetBoolean(1), $"Row-level security is not forced on {table}.");
    }

    [Fact]
    public async Task EveryTableInPublic_HasRowLevelSecurityEnabledAndForced()
    {
        await using var owner = await _db.OpenOwnerAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT c.relname
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p')
              AND NOT (c.relrowsecurity AND c.relforcerowsecurity)
            """, owner);

        var unprotected = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                unprotected.Add(reader.GetString(0));
            }
        }

        Assert.Empty(unprotected);
    }

    [Fact]
    public async Task Migrate_RunTwice_Succeeds()
    {
        await TestDatabase.MigrateAsync(_db.OwnerConnectionString, _db.AppConnectionString);
        await TestDatabase.MigrateAsync(_db.OwnerConnectionString, _db.AppConnectionString);

        await using var owner = await _db.OpenOwnerAsync();
        Assert.Equal(5, await DbTestSupport.ScalarAsync<long>(
            owner,
            "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace " +
            "WHERE n.nspname = 'public' AND c.relkind = 'r'"));
    }

    [Fact]
    public async Task AppUser_IsNeitherSuperuserNorAllowedToBypassRowLevelSecurity()
    {
        await using var owner = await _db.OpenOwnerAsync();

        Assert.False(await DbTestSupport.ScalarAsync<bool>(owner, "SELECT rolsuper FROM pg_roles WHERE rolname = 'app_user'"));
        Assert.False(await DbTestSupport.ScalarAsync<bool>(owner, "SELECT rolbypassrls FROM pg_roles WHERE rolname = 'app_user'"));
        Assert.False(await DbTestSupport.ScalarAsync<bool>(owner, "SELECT rolcreaterole FROM pg_roles WHERE rolname = 'app_user'"));
        Assert.False(await DbTestSupport.ScalarAsync<bool>(owner, "SELECT rolcreatedb FROM pg_roles WHERE rolname = 'app_user'"));
    }

    [Fact]
    public async Task AppUser_CanLogInWithThePasswordFromTheConnectionString()
    {
        await using var app = await _db.OpenAppAsync();

        Assert.Equal("app_user", await DbTestSupport.ScalarAsync<string>(app, "SELECT current_user"));
    }

    [Fact]
    public async Task AppUser_CannotLogInWithAWrongPassword()
    {
        var wrong = new NpgsqlConnectionStringBuilder(_db.AppConnectionString) { Password = "definitely-wrong" };
        await using var connection = new NpgsqlConnection(wrong.ConnectionString);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => connection.OpenAsync());

        Assert.Equal(PostgresErrorCodes.InvalidPassword, exception.SqlState);
    }

    [Fact]
    public async Task AppUser_CannotCreateObjectsInThePublicSchema()
    {
        await using var app = await _db.OpenAppAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => DbTestSupport.ExecuteAsync(app, "CREATE TABLE sneaky (id int)"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task TenantFunctions_ReturnNull_WhenNoTenantIsSet()
    {
        await using var app = await _db.OpenAppAsync();

        Assert.Null(await DbTestSupport.ScalarAsync<Guid?>(app, "SELECT app_agency_id()"));
        Assert.Null(await DbTestSupport.ScalarAsync<Guid?>(app, "SELECT app_user_id()"));
    }

    [Fact]
    public async Task TenantFunctions_ReturnNull_WhenTheSettingIsAnEmptyString()
    {
        await using var app = await _db.OpenAppAsync();
        await DbTestSupport.ExecuteAsync(app, "SELECT set_config('app.agency_id', '', false), set_config('app.user_id', '', false)");

        Assert.Null(await DbTestSupport.ScalarAsync<Guid?>(app, "SELECT app_agency_id()"));
        Assert.Null(await DbTestSupport.ScalarAsync<Guid?>(app, "SELECT app_user_id()"));
    }

    [Fact]
    public async Task AuthFindUserByEmail_FindsTheUserIgnoringCase_WhileTheUsersTableStaysHidden()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var userId = await DbTestSupport.InsertUserAsync(owner, "Ann@Example.com", "Ann");

        await using var app = await _db.OpenAppAsync(); // no tenant: this is the state at login

        Assert.Equal(0, await DbTestSupport.CountAsync(app, "users"));

        await using var lookup = new NpgsqlCommand("SELECT id, email, password_hash, full_name FROM auth_find_user_by_email($1)", app)
        {
            Parameters = { new NpgsqlParameter<string> { TypedValue = "ann@example.COM" } }
        };
        await using var reader = await lookup.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(userId, reader.GetGuid(0));
        Assert.Equal("Ann@Example.com", reader.GetString(1));
        Assert.Equal("Ann", reader.GetString(3));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task AuthFindUserByEmail_ReturnsNothingForAnUnknownAddress()
    {
        await using var app = await _db.OpenAppAsync();
        await using var lookup = new NpgsqlCommand("SELECT count(*) FROM auth_find_user_by_email($1)", app)
        {
            Parameters = { new NpgsqlParameter<string> { TypedValue = "nobody@example.com" } }
        };

        Assert.Equal(0L, await lookup.ExecuteScalarAsync());
    }

    [Fact]
    public async Task AuthFindRefreshToken_FindsTheTokenByHash_WhileTheTableStaysHidden()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var agencyId = await DbTestSupport.InsertAgencyAsync(owner);
        var userId = await DbTestSupport.InsertUserAsync(owner, "ann@example.com");
        await DbTestSupport.ExecuteAsync(
            owner,
            "INSERT INTO refresh_tokens (agency_id, user_id, token_hash, expires_at) VALUES ($1, $2, $3, now() + interval '1 day')",
            agencyId, userId, "hash-1");

        await using var app = await _db.OpenAppAsync();

        Assert.Equal(0, await DbTestSupport.CountAsync(app, "refresh_tokens"));

        await using var lookup = new NpgsqlCommand("SELECT user_id, agency_id, revoked_at FROM auth_find_refresh_token($1)", app)
        {
            Parameters = { new NpgsqlParameter<string> { TypedValue = "hash-1" } }
        };
        await using var reader = await lookup.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(userId, reader.GetGuid(0));
        Assert.Equal(agencyId, reader.GetGuid(1));
        Assert.True(await reader.IsDBNullAsync(2));
    }

    [Fact]
    public async Task AuthLookupFunctions_AreSecurityDefiner_WithAFixedSearchPath()
    {
        await using var owner = await _db.OpenOwnerAsync();
        await using var command = new NpgsqlCommand(
            "SELECT proname, prosecdef, proconfig::text FROM pg_proc WHERE proname IN ('auth_find_user_by_email', 'auth_find_refresh_token') ORDER BY proname",
            owner);
        await using var reader = await command.ExecuteReaderAsync();

        var seen = 0;
        while (await reader.ReadAsync())
        {
            seen++;
            Assert.True(reader.GetBoolean(1), $"{reader.GetString(0)} is not SECURITY DEFINER.");
            Assert.Contains("search_path", reader.GetString(2));
        }

        Assert.Equal(2, seen);
    }

    [Fact]
    public async Task Migration_FailsClosed_WhenAppUserCouldBypassRowLevelSecurity()
    {
        await using (var owner = await _db.OpenOwnerAsync())
        {
            await DbTestSupport.ExecuteAsync(owner, "ALTER ROLE app_user BYPASSRLS");
        }

        try
        {
            await Assert.ThrowsAsync<PostgresException>(() =>
                TestDatabase.MigrateAsync(_db.OwnerConnectionString, _db.AppConnectionString));
        }
        finally
        {
            await using var owner = await _db.OpenOwnerAsync();
            await DbTestSupport.ExecuteAsync(owner, "ALTER ROLE app_user NOBYPASSRLS");
        }
    }
}
