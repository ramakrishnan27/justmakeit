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
