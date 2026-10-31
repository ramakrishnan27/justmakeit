using Dapper;
using Npgsql;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Repositories;

public sealed class AccountRepository(IDbConnectionFactory connections) : IAccountRepository
{
    private const string UniqueEmailIndex = "ux_users_email";

    public async Task<UserLoginRecord?> FindLoginByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        // No tenant yet: the function is the one sanctioned way to look a user up by email.
        await using var connection = await connections.OpenAsync(null, null, cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<UserLoginRecord>(new CommandDefinition(
            "SELECT id, email, password_hash, full_name FROM auth_find_user_by_email(@email)",
            new { email = normalizedEmail },
            cancellationToken: cancellationToken));
    }

    public async Task<UserRecord?> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<UserRecord>(new CommandDefinition(
            "SELECT id, email, full_name FROM users WHERE id = @userId",
            new { userId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> CreateAccountAsync(NewAccount account, CancellationToken cancellationToken)
    {
        // The connection is opened for the agency and user being created, so each row passes its own
        // policy (tenant_isolation, self_access) with nothing bypassed.
        await using var connection = await connections.OpenAsync(account.AgencyId, account.UserId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO agencies (id, name, plan) VALUES (@AgencyId, @AgencyName, @plan)",
                new { account.AgencyId, account.AgencyName, plan = AgencyPlan.Trial.ToDb() },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO users (id, email, password_hash, full_name) VALUES (@UserId, @Email, @PasswordHash, @FullName)",
                new { account.UserId, account.Email, account.PasswordHash, account.FullName },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO agency_members (agency_id, user_id, role) VALUES (@AgencyId, @UserId, @role)",
                new { account.AgencyId, account.UserId, role = AgencyRole.Owner.ToDb() },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO workspaces (id, agency_id, name) VALUES (@WorkspaceId, @AgencyId, @WorkspaceName)",
                new { account.WorkspaceId, account.AgencyId, account.WorkspaceName },
                transaction,
                cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.UniqueViolation && exception.ConstraintName == UniqueEmailIndex)
        {
            // Disposing the transaction rolls it back, so the agency inserted above does not survive.
            return false;
        }
    }
}
