using Dapper;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Repositories;

public sealed class RefreshTokenRepository(IDbConnectionFactory connections) : IRefreshTokenRepository
{
    private const string InsertSql =
        "INSERT INTO refresh_tokens (id, agency_id, user_id, token_hash, expires_at) " +
        "VALUES (@Id, @AgencyId, @UserId, @TokenHash, @ExpiresAt)";

    public async Task<RefreshTokenRecord?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        // No tenant yet: the function returns the one row for this exact hash, including whose it is.
        await using var connection = await connections.OpenAsync(null, null, cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<RefreshTokenRecord>(new CommandDefinition(
            "SELECT id, user_id, agency_id, expires_at, revoked_at, replaced_by_id FROM auth_find_refresh_token(@tokenHash)",
            new { tokenHash },
            cancellationToken: cancellationToken));
    }

    public async Task InsertAsync(NewRefreshToken token, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(token.AgencyId, token.UserId, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(InsertSql, token, cancellationToken: cancellationToken));
    }

    public async Task<bool> RotateAsync(
        Guid oldTokenId, NewRefreshToken replacement, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(replacement.AgencyId, replacement.UserId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The replacement goes in first because the old row points at it. Revoking only a token that
        // is still live is what makes two simultaneous refreshes with the same token impossible: one
        // updates the row, the other finds nothing to update and is told so.
        await connection.ExecuteAsync(new CommandDefinition(InsertSql, replacement, transaction, cancellationToken: cancellationToken));

        var revoked = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE refresh_tokens
            SET revoked_at = @now, replaced_by_id = @replacementId
            WHERE id = @oldTokenId AND agency_id = @AgencyId AND user_id = @UserId AND revoked_at IS NULL
            """,
            new { now, replacementId = replacement.Id, oldTokenId, replacement.AgencyId, replacement.UserId },
            transaction,
            cancellationToken: cancellationToken));

        if (revoked != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task RevokeAsync(Guid agencyId, Guid userId, Guid tokenId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE refresh_tokens SET revoked_at = @now WHERE id = @tokenId AND agency_id = @agencyId AND user_id = @userId AND revoked_at IS NULL",
            new { now, tokenId, agencyId, userId },
            cancellationToken: cancellationToken));
    }

    public async Task<int> RevokeAllAsync(Guid agencyId, Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE refresh_tokens SET revoked_at = @now WHERE agency_id = @agencyId AND user_id = @userId AND revoked_at IS NULL",
            new { now, agencyId, userId },
            cancellationToken: cancellationToken));
    }
}
