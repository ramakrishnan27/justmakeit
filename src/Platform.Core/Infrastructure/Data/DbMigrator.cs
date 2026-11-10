using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Platform.Infrastructure.Settings;

namespace Platform.Infrastructure.Data;

/// <summary>
/// Applies the embedded <c>Migrations/*.sql</c> scripts in file-name order, as the database owner.
/// Scripts are idempotent, so every start runs all of them. They run in one transaction under an
/// advisory lock, so instances starting together take turns and a failure leaves nothing half-applied.
/// </summary>
public sealed class DbMigrator(IOptions<DatabaseSettings> options, ILogger<DbMigrator> logger)
{
    private const string ResourcePrefix = "Migrations.";
    private const string AppRoleName = "app_user";
    private const long AdvisoryLockKey = 0x504C41544D4947; // "PLATMIG"

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.MigratorConnectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Migrator is not configured. Migrations run as the database owner, not as app_user. " +
                "It belongs in appsettings.json, with its password in user-secrets (Database:MigratorPassword) " +
                "or the Database__MigratorPassword environment variable.");
        }

        var scripts = LoadScripts();

        await using var connection = new NpgsqlConnection(settings.MigratorConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)", connection, transaction))
        {
            lockCommand.Parameters.Add(new NpgsqlParameter<long> { TypedValue = AdvisoryLockKey });
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var script in scripts)
        {
            await using var command = new NpgsqlCommand(script.Sql, connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
            logger.LogInformation("Applied migration script {Script}", script.Name);
        }

        await SyncAppRolePasswordAsync(connection, transaction, settings.AppConnectionString, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // The scripts create app_user without a password so no secret lives in the repository.
    // The password comes from ConnectionStrings:App, which is the one place it is configured.
    private async Task SyncAppRolePasswordAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string appConnectionString,
        CancellationToken cancellationToken)
    {
        var app = new NpgsqlConnectionStringBuilder(appConnectionString);
        if (!string.Equals(app.Username, AppRoleName, StringComparison.Ordinal) || string.IsNullOrEmpty(app.Password))
        {
            logger.LogWarning(
                "ConnectionStrings:App does not log in as {Role} with a password, so the role password was left unchanged.",
                AppRoleName);
            return;
        }

        // ALTER ROLE cannot take bind parameters; format() with %L quotes the literal on the server.
        string alterStatement;
        await using (var formatCommand = new NpgsqlCommand("SELECT format('ALTER ROLE app_user WITH PASSWORD %L', $1)", connection, transaction))
        {
            formatCommand.Parameters.Add(new NpgsqlParameter<string> { TypedValue = app.Password });
            alterStatement = (string)(await formatCommand.ExecuteScalarAsync(cancellationToken))!;
        }

        await using var alterCommand = new NpgsqlCommand(alterStatement, connection, transaction);
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
        logger.LogInformation("Set the password of role {Role} from ConnectionStrings:App", AppRoleName);
    }

    private static List<MigrationScript> LoadScripts()
    {
        var assembly = typeof(DbMigrator).Assembly;
        var scripts = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                           && name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)
                                   ?? throw new InvalidOperationException($"Embedded migration '{name}' could not be opened.");
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return new MigrationScript(name[ResourcePrefix.Length..], reader.ReadToEnd());
            })
            .ToList();

        return scripts.Count > 0
            ? scripts
            : throw new InvalidOperationException("No embedded migration scripts were found in Platform.Infrastructure.");
    }

    private sealed record MigrationScript(string Name, string Sql);
}
