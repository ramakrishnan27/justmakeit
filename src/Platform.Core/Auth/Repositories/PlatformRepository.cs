using Dapper;
using Npgsql;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Helpers.Exceptions;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Repositories;

/// <summary>
/// The platforms for a member. The connection acts for the member's organization and user; each function of
/// <c>012_member_platforms.sql</c> refuses (42501) unless that user is an active member of an active organization and holds
/// platforms.view, and reads the assignment of that organization only.
/// </summary>
public sealed class PlatformRepository(IDbConnectionFactory connections) : IPlatformRepository
{
    public async Task<IReadOnlyList<OrganizationChannelRecord>> ListAsync(Guid agencyId, Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        try
        {
            return (await connection.QueryAsync<OrganizationChannelRecord>(new CommandDefinition(
                """
                SELECT id, name, description, icon_key, has_image, is_enabled
                FROM member_list_platforms()
                """,
                cancellationToken: cancellationToken))).AsList();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            throw NotAllowed();
        }
    }

    public async Task<ChannelIconRecord?> GetIconAsync(Guid agencyId, Guid userId, Guid channelId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        try
        {
            return await connection.QuerySingleOrDefaultAsync<ChannelIconRecord>(new CommandDefinition(
                "SELECT content_type, image FROM member_platform_icon(@channelId)",
                new { channelId },
                cancellationToken: cancellationToken));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            throw NotAllowed();
        }
    }

    private static ForbiddenException NotAllowed() =>
        new("Your role is not allowed to see the platforms.", "insufficient_permission");
}
