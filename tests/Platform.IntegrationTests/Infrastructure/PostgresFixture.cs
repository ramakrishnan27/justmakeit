using Npgsql;
using Platform.Helpers.Utilities;
using Testcontainers.PostgreSql;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>
/// One PostgreSQL 16 container for the whole test run. Each test class asks it for its own database,
/// so classes never share tables or rows. Credentials are random per run; nothing is stored.
/// <para>
/// Without Docker, set <see cref="ExternalServerVariable"/> to a superuser connection string for a
/// throwaway PostgreSQL server (never one that holds real data: the migration creates the cluster-wide
/// role app_user and the tests create and drop databases). The container is then not started.
/// </para>
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string ExternalServerVariable = "PLATFORM_TEST_POSTGRES";

    private const string OwnerUser = "platform_owner";

    private readonly string _ownerPassword = TokenGenerator.Create();
    private readonly string _appPassword = TokenGenerator.Create();
    private readonly PostgreSqlContainer? _container;
    private readonly string? _externalConnectionString = Environment.GetEnvironmentVariable(ExternalServerVariable);

    public PostgresFixture()
    {
        if (!string.IsNullOrWhiteSpace(_externalConnectionString))
        {
            return;
        }

        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithUsername(OwnerUser)
            .WithPassword(_ownerPassword)
            .Build();
    }

    private string AdminConnectionString => _externalConnectionString ?? _container!.GetConnectionString();

    public Task InitializeAsync() => _container?.StartAsync() ?? Task.CompletedTask;

    public Task DisposeAsync() => _container?.DisposeAsync().AsTask() ?? Task.CompletedTask;

    /// <summary>Creates a database, applies every migration to it as the owner, and returns it.</summary>
    public async Task<TestDatabase> CreateDatabaseAsync()
    {
        var database = await CreateEmptyDatabaseAsync();
        await database.MigrateAsync();
        return database;
    }

    /// <summary>
    /// Like <see cref="CreateDatabaseAsync"/>, but the migrations run as an ordinary role that owns the
    /// database and is neither a superuser nor able to bypass row-level security. The other databases here
    /// are owned by a superuser, who skips every policy; this is how production should be set up, and the
    /// only way to prove that the SECURITY DEFINER functions still see the rows they report on.
    /// </summary>
    public async Task<TestDatabase> CreateDatabaseWithLimitedOwnerAsync()
    {
        var database = await CreateEmptyDatabaseWithLimitedOwnerAsync();
        await database.MigrateAsync();
        return database;
    }

    /// <summary>
    /// The limited-owner database of <see cref="CreateDatabaseWithLimitedOwnerAsync"/>, with nothing applied
    /// yet, for tests that start from an older schema and migrate it themselves.
    /// </summary>
    public async Task<TestDatabase> CreateEmptyDatabaseWithLimitedOwnerAsync()
    {
        const string role = "test_limited_owner";
        var name = "test_" + Guid.NewGuid().ToString("N");
        var password = TokenGenerator.Create();

        await using (var admin = new NpgsqlConnection(AdminConnectionString))
        {
            await admin.OpenAsync();

            // DDL cannot take bind parameters. Every name here is a constant or generated, never input.
            await using (var createRole = new NpgsqlCommand(
                           $"DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN " +
                           $"CREATE ROLE {role} LOGIN NOSUPERUSER NOBYPASSRLS CREATEROLE; END IF; END $$", admin))
            {
                await createRole.ExecuteNonQueryAsync();
            }

            await using (var setPassword = new NpgsqlCommand(
                           "SELECT format('ALTER ROLE test_limited_owner WITH PASSWORD %L', $1)", admin)
            {
                Parameters = { new NpgsqlParameter<string> { TypedValue = password } }
            })
            {
                var alter = (string)(await setPassword.ExecuteScalarAsync())!;
                await using var run = new NpgsqlCommand(alter, admin);
                await run.ExecuteNonQueryAsync();
            }

            // From PostgreSQL 16 a role with CREATEROLE may only change roles it administers. If app_user
            // exists already (an earlier test class made it), hand the limited owner the right to set its password.
            await using (var grantAdmin = new NpgsqlCommand(
                           $"DO $$ BEGIN IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_user') THEN " +
                           $"GRANT app_user TO {role} WITH ADMIN OPTION; END IF; END $$", admin))
            {
                await grantAdmin.ExecuteNonQueryAsync();
            }

            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\" OWNER {role}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var owner = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Database = name,
            Username = role,
            Password = password
        };
        var app = new NpgsqlConnectionStringBuilder(owner.ConnectionString) { Username = "app_user", Password = _appPassword };

        return new TestDatabase(name, AdminConnectionString, owner.ConnectionString, app.ConnectionString);
    }

    /// <summary>Creates a database with nothing in it, for tests that exercise the migrator itself.</summary>
    public async Task<TestDatabase> CreateEmptyDatabaseAsync()
    {
        var name = "test_" + Guid.NewGuid().ToString("N");

        await using (var admin = new NpgsqlConnection(AdminConnectionString))
        {
            await admin.OpenAsync();
            // DDL cannot take bind parameters. The name is generated above, never user input.
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var owner = new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = name };
        var app = new NpgsqlConnectionStringBuilder(owner.ConnectionString) { Username = "app_user", Password = _appPassword };

        return new TestDatabase(name, AdminConnectionString, owner.ConnectionString, app.ConnectionString);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
