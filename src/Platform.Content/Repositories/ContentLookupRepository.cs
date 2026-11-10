using Dapper;
using Npgsql;
using Platform.Content.Data;
using Platform.Content.Enums;
using Platform.Content.Interfaces;
using Platform.Infrastructure.Data;

namespace Platform.Content.Repositories;

/// <summary>
/// What Content needs to know about the workspaces and the channels of the organization. Read with this project's own queries:
/// the Auth folder's repositories are off limits here.
/// </summary>
public sealed class ContentLookupRepository(IDbConnectionFactory connections) : IContentLookupRepository
{
    public async Task<bool> WorkspaceExistsAsync(Guid agencyId, Guid userId, Guid workspaceId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM workspaces WHERE id = @workspaceId AND agency_id = @agencyId)",
            new { workspaceId, agencyId },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlySet<string>> EnabledPlatformsAsync(Guid agencyId, Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        try
        {
            var names = await connection.QueryAsync<string>(new CommandDefinition(
                "SELECT name FROM member_list_platforms() WHERE is_enabled",
                cancellationToken: cancellationToken));

            // A channel is a platform when its name, in lower case, is one: Instagram is instagram, LinkedIn is linkedin.
            return names
                .Select(name => name.Trim().ToLowerInvariant())
                .Where(name => DbEnum<SocialPlatform>.TryParse(name, out _))
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            // The caller may not see the platforms (platforms.view), so as far as they are concerned none is enabled.
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
