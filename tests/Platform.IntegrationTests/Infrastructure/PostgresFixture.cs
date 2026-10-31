using Npgsql;
using Platform.Helpers.Utilities;
using Testcontainers.PostgreSql;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>
/// One PostgreSQL 16 container for the whole test run. Each test class asks it for its own database,
/// so classes never share tables or rows. Credentials are random per run; nothing is stored.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string OwnerUser = "platform_owner";

    private readonly string _ownerPassword = TokenGenerator.Create();
    private readonly string _appPassword = TokenGenerator.Create();
    private readonly PostgreSqlContainer _container;

    public PostgresFixture()
    {
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithUsername(OwnerUser)
            .WithPassword(_ownerPassword)
            .Build();
    }

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

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

        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            // DDL cannot take bind parameters. The name is generated above, never user input.
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var owner = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name };
        var app = new NpgsqlConnectionStringBuilder(owner.ConnectionString) { Username = "app_user", Password = _appPassword };

        return new TestDatabase(name, _container.GetConnectionString(), owner.ConnectionString, app.ConnectionString);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
