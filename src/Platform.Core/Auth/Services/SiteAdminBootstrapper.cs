using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Platform.Auth.Interfaces;
using Platform.Auth.Settings;
using Platform.Helpers.Utilities;
using Platform.Infrastructure.Settings;

namespace Platform.Auth.Services;

/// <summary>
/// Creates the first site admin. It runs as the database owner, over the migrator connection, right
/// after the migrations: the application role cannot write site_admins at all, which is what keeps
/// the API from ever being able to promote anybody. It is idempotent, so running it on every start is safe.
/// </summary>
public sealed class SiteAdminBootstrapper(
    IOptions<DatabaseSettings> database,
    IOptions<SiteAdminSettings> siteAdmin,
    IPasswordHasher passwordHasher,
    ILogger<SiteAdminBootstrapper> logger) : ISiteAdminBootstrapper
{
    public async Task<SiteAdminBootstrapResult> EnsureAsync(CancellationToken cancellationToken)
    {
        var settings = siteAdmin.Value;
        if (string.IsNullOrWhiteSpace(settings.Email))
        {
            return SiteAdminBootstrapResult.NotConfigured;
        }

        var email = EmailAddressHelper.Normalize(settings.Email);

        await using var connection = new NpgsqlConnection(database.Value.MigratorConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var existingId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM users WHERE lower(email) = @email",
            new { email },
            transaction,
            cancellationToken: cancellationToken));

        SiteAdminBootstrapResult result;
        Guid userId;

        if (existingId is { } id)
        {
            // An account that already exists keeps its own password; it only gains the role.
            userId = id;
            result = SiteAdminBootstrapResult.Promoted;
        }
        else if (string.IsNullOrEmpty(settings.Password))
        {
            logger.LogWarning(
                "SiteAdmin:Email is set but no such account exists and SiteAdmin:Password is empty, so no site admin was created.");
            return SiteAdminBootstrapResult.MissingPassword;
        }
        else
        {
            userId = Guid.NewGuid();

            // The users policy lets a connection write only the row whose id it acts as, even for the
            // owner, so say who this connection is before inserting.
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT set_config('app.user_id', @userId, true)",
                new { userId = userId.ToString() },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO users (id, email, password_hash, full_name) VALUES (@userId, @email, @passwordHash, 'Site Admin')",
                new { userId, email, passwordHash = passwordHasher.Hash(settings.Password) },
                transaction,
                cancellationToken: cancellationToken));

            result = SiteAdminBootstrapResult.Created;
        }

        var added = await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO site_admins (user_id) VALUES (@userId) ON CONFLICT (user_id) DO NOTHING",
            new { userId },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);

        if (added == 0)
        {
            // Already a site admin: nothing changed.
            return SiteAdminBootstrapResult.AlreadySiteAdmin;
        }

        logger.LogInformation(
            result == SiteAdminBootstrapResult.Created
                ? "Created site admin {UserId} from SiteAdmin:Email"
                : "Made existing user {UserId} a site admin from SiteAdmin:Email",
            userId);
        return result;
    }
}
