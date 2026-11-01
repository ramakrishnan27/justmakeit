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
    private const string UniqueIdentityConstraint = "uq_user_identities_provider_subject";

    public async Task<UserLoginRecord?> FindLoginByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        // No tenant yet: the function is the one sanctioned way to look a user up by email.
        await using var connection = await connections.OpenAsync(null, null, cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<UserLoginRecord>(new CommandDefinition(
            "SELECT id, email, password_hash, full_name FROM auth_find_user_by_email(@email)",
            new { email = normalizedEmail },
            cancellationToken: cancellationToken));
    }

    public async Task<Guid?> FindUserIdByIdentityAsync(string provider, string subject, CancellationToken cancellationToken)
    {
        // No user yet: the function matches one exact (provider, subject) and returns only the user id.
        await using var connection = await connections.OpenAsync(null, null, cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT user_id FROM auth_find_user_by_identity(@provider, @subject)",
            new { provider, subject },
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
                "INSERT INTO agencies (id, name, business_model, plan) VALUES (@AgencyId, @AgencyName, @businessModel, @plan)",
                new { account.AgencyId, account.AgencyName, businessModel = account.BusinessModel.ToDb(), plan = AgencyPlan.Trial.ToDb() },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO users (id, email, password_hash, full_name) VALUES (@UserId, @Email, @PasswordHash, @FullName)",
                new { account.UserId, account.Email, account.PasswordHash, account.FullName },
                transaction,
                cancellationToken: cancellationToken));

            if (account.Identity is { } identity)
            {
                // The connection acts as the new user, so own_insert lets this row in and nobody else's.
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO user_identities (user_id, provider, subject, email) VALUES (@UserId, @Provider, @Subject, @Email)",
                    new { account.UserId, identity.Provider, identity.Subject, identity.Email },
                    transaction,
                    cancellationToken: cancellationToken));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO agency_members (agency_id, user_id, business_model, role) VALUES (@AgencyId, @UserId, @businessModel, @role)",
                new { account.AgencyId, account.UserId, businessModel = account.BusinessModel.ToDb(), role = account.Role.ToDb() },
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
            when (exception.SqlState == PostgresErrorCodes.UniqueViolation
                  && exception.ConstraintName is UniqueEmailIndex or UniqueIdentityConstraint)
        {
            // Disposing the transaction rolls it back, so the agency inserted above does not survive.
            // The same outside identity registering twice at once is "already registered" too.
            return false;
        }
    }
}
