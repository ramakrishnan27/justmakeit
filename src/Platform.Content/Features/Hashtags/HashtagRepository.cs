using Dapper;
using Platform.Content.Data;
using Platform.Content.Entities;
using Platform.Content.Interfaces;
using Platform.Infrastructure.Data;

namespace Platform.Content.Features.Hashtags;

public sealed class HashtagRepository(IDbConnectionFactory connections) : IHashtagRepository
{
    private sealed class SuggestionRow
    {
        public string Hashtag { get; set; } = string.Empty;
        public int Uses { get; set; }
    }

    public async Task<IReadOnlyList<(string Hashtag, int Uses)>> SuggestAsync(
        Guid agencyId, Guid userId, Guid? workspaceId, string? prefix, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);

        // starts_with is a function, not a LIKE pattern, so a % or _ the person typed means itself.
        var rows = await connection.QueryAsync<SuggestionRow>(new CommandDefinition(
            """
            SELECT min(h.hashtag) AS hashtag, count(*)::int AS uses
            FROM content_hashtags h
            JOIN content_posts p ON p.id = h.post_id AND p.agency_id = h.agency_id
            WHERE h.agency_id = @agencyId
              AND (@workspaceId::uuid IS NULL OR p.workspace_id = @workspaceId)
              AND (@prefix::text IS NULL OR starts_with(lower(h.hashtag), lower(@prefix)))
            GROUP BY lower(h.hashtag)
            ORDER BY count(*) DESC, lower(h.hashtag)
            LIMIT @limit
            """,
            new { agencyId, workspaceId, prefix, limit },
            cancellationToken: cancellationToken));
        return rows.Select(row => (row.Hashtag, row.Uses)).ToList();
    }

    public async Task<IReadOnlyList<ContentTag>> ListTagsAsync(Guid agencyId, Guid userId, Guid? workspaceId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        return (await connection.QueryAsync<ContentTag>(new CommandDefinition(
            $"""
            SELECT {ContentColumns.Tag} FROM content_tags
            WHERE agency_id = @agencyId AND (@workspaceId::uuid IS NULL OR workspace_id = @workspaceId)
            ORDER BY lower(name), id
            """,
            new { agencyId, workspaceId },
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<bool> InsertTagAsync(Guid userId, ContentTag tag, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(tag.AgencyId, userId, cancellationToken);
        var inserted = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO content_tags (id, agency_id, workspace_id, name, color)
            VALUES (@Id, @AgencyId, @WorkspaceId, @Name, @Color)
            ON CONFLICT (workspace_id, lower(name)) DO NOTHING
            """,
            tag,
            cancellationToken: cancellationToken));
        return inserted == 1;
    }
}
