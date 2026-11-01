using Dapper;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Repositories;

public sealed class SiteAdminRefreshTokenRepository(IDbConnectionFactory connections) : ISiteAdminRefreshTokenRepository
{
    private const string InsertSql =
        "INSERT INTO site_admin_refresh_tokens (id, user_id, token_hash, expires_at) " +
        "VALUES (@Id, @UserId, @TokenHash, @ExpiresAt)";

    public async Task<SiteRefreshTokenRecord?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        // No tenant and no user yet: the function returns the one row for this exact hash.
        await using var connection = await connections.OpenAsync(null, null, cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<SiteRefreshTokenRecord>(new CommandDefinition(
            "SELECT id, user_id, expires_at, revoked_at, replaced_by_id FROM auth_find_site_refresh_token(@tokenHash)",
            new { tokenHash },
            cancellationToken: cancellationToken));
    }

    public async Task InsertAsync(NewSiteRefreshToken token, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, token.UserId, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(InsertSql, token, cancellationToken: cancellationToken));
    }

    public async Task<bool> RotateAsync(
        Guid oldTokenId, NewSiteRefreshToken replacement, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, replacement.UserId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Same scheme as the agency tokens: the replacement goes in first (the old row points at it),
        // and only a token that is still live can be revoked, so of two simultaneous refreshes one wins.
        await connection.ExecuteAsync(new CommandDefinition(InsertSql, replacement, transaction, cancellationToken: cancellationToken));

        var revoked = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE site_admin_refresh_tokens
            SET revoked_at = @now, replaced_by_id = @replacementId
            WHERE id = @oldTokenId AND user_id = @UserId AND revoked_at IS NULL
            """,
            new { now, replacementId = replacement.Id, oldTokenId, replacement.UserId },
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

    public async Task RevokeAsync(Guid userId, Guid tokenId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE site_admin_refresh_tokens SET revoked_at = @now WHERE id = @tokenId AND user_id = @userId AND revoked_at IS NULL",
            new { now, tokenId, userId },
            cancellationToken: cancellationToken));
    }

    public async Task<int> RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(null, userId, cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE site_admin_refresh_tokens SET revoked_at = @now WHERE user_id = @userId AND revoked_at IS NULL",
            new { now, userId },
            cancellationToken: cancellationToken));
    }
}
