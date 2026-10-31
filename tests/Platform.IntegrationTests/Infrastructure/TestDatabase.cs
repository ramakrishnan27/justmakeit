using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Platform.Helpers.Tenancy;
using Platform.Infrastructure.Data;
using Platform.Infrastructure.Settings;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>A migrated database owned by one test class. Dropped, with any open sessions, on dispose.</summary>
public sealed class TestDatabase(string name, string adminConnectionString, string ownerConnectionString, string appConnectionString)
    : IAsyncDisposable
{
    private readonly List<NpgsqlDataSource> _dataSources = [];

    /// <summary>Connects as the database owner, a superuser in the test container, so row-level security does not apply.</summary>
    public string OwnerConnectionString { get; } = ownerConnectionString;

    /// <summary>Connects as app_user, exactly like the API does.</summary>
    public string AppConnectionString { get; } = appConnectionString;

    public Task MigrateAsync() => MigrateAsync(OwnerConnectionString, AppConnectionString);

    public static Task MigrateAsync(string migratorConnectionString, string appConnectionString)
    {
        var settings = new DatabaseSettings
        {
            MigratorConnectionString = migratorConnectionString,
            AppConnectionString = appConnectionString
        };
        var migrator = new DbMigrator(Options.Create(settings), NullLogger<DbMigrator>.Instance);
        return migrator.MigrateAsync(CancellationToken.None);
    }

    public async Task<NpgsqlConnection> OpenOwnerAsync()
    {
        var connection = new NpgsqlConnection(OwnerConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>A connection factory over app_user, the same type the API uses, for the given tenant.</summary>
    public IDbConnectionFactory CreateFactory(Guid? agencyId = null, Guid? userId = null, string? connectionString = null)
    {
        var dataSource = NpgsqlDataSource.Create(connectionString ?? AppConnectionString);
        _dataSources.Add(dataSource);

        var tenant = new TenantContext();
        tenant.Set(agencyId, userId);
        return new NpgsqlConnectionFactory(dataSource, tenant);
    }

    /// <summary>Opens an app_user connection acting for the given tenant.</summary>
    public Task<NpgsqlConnection> OpenAppAsync(Guid? agencyId = null, Guid? userId = null) =>
        CreateFactory().OpenAsync(agencyId, userId, CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        foreach (var dataSource in _dataSources)
        {
            await dataSource.DisposeAsync();
        }

        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        // Same as creation: the name is ours, and WITH (FORCE) ends sessions still held by pooled connections.
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
