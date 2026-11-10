using System.Data.Common;
using Dapper;
using Npgsql;
using Platform.Content.Data;
using Platform.Content.Entities;
using Platform.Content.Interfaces;
using Platform.Infrastructure.Data;

namespace Platform.Content.Features.Publishing;

/// <summary>
/// The publishing worker's data access. Finding what is due spans organizations, so it goes through the
/// <c>content_claim_due_publications()</c> function; everything after that runs on a connection scoped to the target's organization.
/// </summary>
public sealed class PublicationRepository(IDbConnectionFactory connections) : IPublicationRepository
{
    private sealed class PostText
    {
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
    }

    public async Task<IReadOnlyList<ClaimedPublication>> ClaimDueAsync(int limit, int staleMinutes, CancellationToken cancellationToken)
    {
        // No organization and no user: the function is the only thing allowed to see across organizations, and it returns ids only.
        await using var connection = await connections.OpenAsync(null, null, cancellationToken);
        return (await connection.QueryAsync<ClaimedPublication>(new CommandDefinition(
            "SELECT agency_id, post_id, publication_id FROM content_claim_due_publications(@limit, @staleMinutes)",
            new { limit, staleMinutes },
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<PublicationWork?> LoadAsync(Guid agencyId, Guid postId, Guid publicationId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, null, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The first target of a scheduled post to be taken makes the post "publishing".
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE content_posts SET status = 'publishing' WHERE id = @postId AND agency_id = @agencyId AND status = 'scheduled'",
            new { postId, agencyId },
            transaction,
            cancellationToken: cancellationToken));

        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            $"""
            SELECT title, body FROM content_posts WHERE id = @postId AND agency_id = @agencyId;
            SELECT {ContentColumns.PostPlatform} FROM content_post_platforms
                WHERE id = @publicationId AND post_id = @postId AND agency_id = @agencyId AND publish_status = 'publishing';
            SELECT hashtag FROM content_hashtags WHERE post_id = @postId AND agency_id = @agencyId ORDER BY display_order, id;
            """,
            new { postId, agencyId, publicationId },
            transaction,
            cancellationToken: cancellationToken));
        var post = await grid.ReadSingleOrDefaultAsync<PostText>();
        var target = await grid.ReadSingleOrDefaultAsync<ContentPostPlatform>();
        var hashtags = (await grid.ReadAsync<string>()).AsList();

        await transaction.CommitAsync(cancellationToken);
        return post is null || target is null
            ? null
            : new PublicationWork { AgencyId = agencyId, PostId = postId, Title = post.Title, Body = post.Body, Target = target, Hashtags = hashtags };
    }

    public async Task RecordSuccessAsync(
        Guid agencyId, Guid publicationId, string externalPostId, string? externalPostUrl, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, null, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var postId = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            """
            UPDATE content_post_platforms
            SET publish_status = 'published', published_at = @now, external_post_id = @externalPostId,
                external_post_url = @externalPostUrl, last_error = NULL
            WHERE id = @publicationId AND agency_id = @agencyId AND publish_status = 'publishing'
            RETURNING post_id
            """,
            new { publicationId, agencyId, now, externalPostId, externalPostUrl },
            transaction,
            cancellationToken: cancellationToken));
        if (postId is not null)
        {
            await SettlePostAsync(connection, transaction, agencyId, postId.Value, now, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordFailureAsync(
        Guid agencyId, Guid publicationId, string error, DateTimeOffset? retryAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, null, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The attempt counts either way. With a retry time the target goes back to scheduled for then; without, it is failed.
        // The casts matter: with no retry time the parameter is NULL, and PostgreSQL cannot infer the type of "$1 IS NULL" (42P08).
        var postId = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            """
            UPDATE content_post_platforms
            SET publish_status = CASE WHEN @retryAt::timestamptz IS NULL THEN 'failed' ELSE 'scheduled' END,
                scheduled_at = CASE WHEN @retryAt::timestamptz IS NULL THEN scheduled_at ELSE @retryAt::timestamptz END,
                retry_count = retry_count + 1,
                last_error = @error
            WHERE id = @publicationId AND agency_id = @agencyId AND publish_status = 'publishing'
            RETURNING post_id
            """,
            new { publicationId, agencyId, error, retryAt },
            transaction,
            cancellationToken: cancellationToken));
        if (postId is not null)
        {
            await SettlePostAsync(connection, transaction, agencyId, postId.Value, now, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Once no target of the post is open (pending, scheduled or publishing), the post is published if at least one target
    /// succeeded and failed if none did. Only a post that is scheduled or publishing is touched.
    /// </summary>
    private static Task<int> SettlePostAsync(
        NpgsqlConnection connection, DbTransaction transaction, Guid agencyId, Guid postId, DateTimeOffset now, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE content_posts p
            SET status = CASE WHEN s.done > 0 THEN 'published' ELSE 'failed' END,
                published_at = CASE WHEN s.done > 0 THEN @now ELSE p.published_at END
            FROM (SELECT count(*) FILTER (WHERE publish_status IN ('pending', 'scheduled', 'publishing')) AS open,
                         count(*) FILTER (WHERE publish_status = 'published') AS done
                  FROM content_post_platforms WHERE post_id = @postId AND agency_id = @agencyId) s
            WHERE p.id = @postId AND p.agency_id = @agencyId AND p.status IN ('scheduled', 'publishing') AND s.open = 0
            """,
            new { postId, agencyId, now },
            transaction,
            cancellationToken: cancellationToken));
}
