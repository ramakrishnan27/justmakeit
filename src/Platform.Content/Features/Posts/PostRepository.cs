using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Npgsql;
using Platform.Content.Data;
using Platform.Content.Entities;
using Platform.Content.Interfaces;
using Platform.Helpers.Exceptions;
using Platform.Infrastructure.Data;

namespace Platform.Content.Features.Posts;

/// <summary>
/// Posts over Dapper and plain SQL. Every method opens a connection for the organization and user it is given (row-level security),
/// and writes the organization into each WHERE as well. A post is written with all its children in one transaction.
/// </summary>
public sealed partial class PostRepository(IDbConnectionFactory connections) : IContentPostRepository
{
    private sealed class TypeCount
    {
        public string ContentType { get; set; } = string.Empty;
        public long Count { get; set; }
    }

    // Letters, combining marks (so a word in Hindi or Tamil is not cut at its vowel signs), digits and underscore.
    [GeneratedRegex(@"[\p{L}\p{M}\p{N}_]+")]
    private static partial Regex SearchWord();

    // --- Reading --------------------------------------------------------------------------------

    public async Task<PostListResult> ListAsync(Guid agencyId, Guid userId, PostFilter filter, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);

        // The counts per type ignore the type filter, so the tabs keep their numbers while one is open.
        var (countWhere, countParameters) = BuildWhere(agencyId, filter, includeType: false);
        var counts = (await connection.QueryAsync<TypeCount>(new CommandDefinition(
            $"SELECT p.content_type, count(*) AS count FROM content_posts p WHERE {countWhere} GROUP BY p.content_type",
            countParameters,
            cancellationToken: cancellationToken))).ToDictionary(c => c.ContentType, c => c.Count);

        var (where, parameters) = BuildWhere(agencyId, filter, includeType: true);
        parameters.Add("limit", filter.PageSize);
        parameters.Add("offset", (filter.Page - 1) * filter.PageSize);
        var items = (await connection.QueryAsync<PostSummary>(new CommandDefinition(
            $"""
            SELECT p.id, p.workspace_id, p.content_type, p.title, p.excerpt, p.status, p.visibility,
                   p.scheduled_at, p.published_at, p.created_at, p.updated_at
            FROM content_posts p
            WHERE {where}
            ORDER BY p.created_at DESC, p.id DESC
            LIMIT @limit OFFSET @offset
            """,
            parameters,
            cancellationToken: cancellationToken))).AsList();

        IReadOnlyList<PostPlatformSummary> platforms = [];
        IReadOnlyList<PostTagSummary> tags = [];
        if (items.Count > 0)
        {
            var ids = new { ids = new PgUuidArray(items.Select(i => i.Id)) };
            platforms = (await connection.QueryAsync<PostPlatformSummary>(new CommandDefinition(
                "SELECT post_id, platform, publish_status FROM content_post_platforms WHERE post_id = ANY(@ids) ORDER BY platform, id",
                ids,
                cancellationToken: cancellationToken))).AsList();
            tags = (await connection.QueryAsync<PostTagSummary>(new CommandDefinition(
                """
                SELECT pt.post_id, t.id, t.name, t.color
                FROM content_post_tags pt
                JOIN content_tags t ON t.id = pt.tag_id AND t.agency_id = pt.agency_id
                WHERE pt.post_id = ANY(@ids)
                ORDER BY lower(t.name), t.id
                """,
                ids,
                cancellationToken: cancellationToken))).AsList();
        }

        return new PostListResult { Items = items, Platforms = platforms, Tags = tags, TypeCounts = counts };
    }

    /// <summary>The WHERE of the list and of its counts. Values are always parameters; only fixed text is put into the SQL.</summary>
    private static (string Where, DynamicParameters Parameters) BuildWhere(Guid agencyId, PostFilter filter, bool includeType)
    {
        var conditions = new List<string> { "p.agency_id = @agencyId" };
        var parameters = new DynamicParameters();
        parameters.Add("agencyId", agencyId);

        if (filter.WorkspaceId is { } workspaceId)
        {
            conditions.Add("p.workspace_id = @workspaceId");
            parameters.Add("workspaceId", workspaceId);
        }

        if (includeType && filter.Type is not null)
        {
            conditions.Add("p.content_type = @type");
            parameters.Add("type", filter.Type);
        }

        if (filter.Status is not null)
        {
            conditions.Add("p.status = @status");
            parameters.Add("status", filter.Status);
        }

        if (filter.Platform is not null)
        {
            conditions.Add("EXISTS (SELECT 1 FROM content_post_platforms x WHERE x.post_id = p.id AND x.platform = @platform)");
            parameters.Add("platform", filter.Platform);
        }

        if (filter.CampaignId is { } campaignId)
        {
            conditions.Add("p.campaign_id = @campaignId");
            parameters.Add("campaignId", campaignId);
        }

        if (filter.From is { } from)
        {
            conditions.Add("p.created_at >= @from");
            parameters.Add("from", from);
        }

        if (filter.To is { } to)
        {
            conditions.Add("p.created_at < @to");
            parameters.Add("to", to);
        }

        var tsQuery = ToTsQuery(filter.Search);
        if (tsQuery is not null)
        {
            conditions.Add("p.search_vector @@ to_tsquery('simple', @tsQuery)");
            parameters.Add("tsQuery", tsQuery);
        }

        return (string.Join(" AND ", conditions), parameters);
    }

    /// <summary>
    /// What the person typed as a prefix search: each word (letters, digits, underscore; at most eight) becomes <c>word:*</c> and they
    /// must all match. Nothing else is passed on, so the text cannot add tsquery syntax. Null when there is no word.
    /// </summary>
    public static string? ToTsQuery(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var words = SearchWord().Matches(search).Select(m => m.Value).Take(8).Select(w => (w.Length > 50 ? w[..50] : w) + ":*").ToList();
        return words.Count == 0 ? null : string.Join(" & ", words);
    }

    public async Task<ContentPostAggregate?> GetAsync(Guid agencyId, Guid userId, Guid postId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        return await ReadAsync(connection, null, agencyId, postId, cancellationToken);
    }

    private static async Task<ContentPostAggregate?> ReadAsync(
        NpgsqlConnection connection, DbTransaction? transaction, Guid agencyId, Guid postId, CancellationToken cancellationToken)
    {
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            $"""
            SELECT {ContentColumns.Post} FROM content_posts WHERE id = @postId AND agency_id = @agencyId;
            SELECT id, agency_id, post_id, hashtag, display_order FROM content_hashtags
                WHERE post_id = @postId AND agency_id = @agencyId ORDER BY display_order, id;
            SELECT t.id, t.agency_id, t.workspace_id, t.name, t.color, t.created_at
                FROM content_post_tags pt JOIN content_tags t ON t.id = pt.tag_id AND t.agency_id = pt.agency_id
                WHERE pt.post_id = @postId AND pt.agency_id = @agencyId ORDER BY lower(t.name), t.id;
            SELECT {ContentColumns.PostPlatform} FROM content_post_platforms
                WHERE post_id = @postId AND agency_id = @agencyId ORDER BY platform, created_at, id;
            SELECT id, agency_id, post_id, question, ends_at FROM content_polls WHERE post_id = @postId AND agency_id = @agencyId;
            SELECT o.id, o.agency_id, o.poll_id, o.option_text, o.display_order
                FROM content_poll_options o JOIN content_polls p ON p.id = o.poll_id AND p.agency_id = o.agency_id
                WHERE p.post_id = @postId AND p.agency_id = @agencyId ORDER BY o.display_order, o.id;
            SELECT id, agency_id, post_id, button_text, target_url, display_order FROM content_ctas
                WHERE post_id = @postId AND agency_id = @agencyId ORDER BY display_order, id;
            SELECT id, agency_id, post_id, platform, mentioned_name, platform_user_id FROM content_mentions
                WHERE post_id = @postId AND agency_id = @agencyId ORDER BY created_at, id;
            SELECT id, agency_id, post_id, location_name, latitude, longitude, external_place_id FROM content_locations
                WHERE post_id = @postId AND agency_id = @agencyId;
            SELECT id, agency_id, post_id, media_asset_id, display_order, media_role, caption FROM content_post_media
                WHERE post_id = @postId AND agency_id = @agencyId ORDER BY display_order, id;
            """,
            new { postId, agencyId },
            transaction,
            cancellationToken: cancellationToken));

        var post = await grid.ReadSingleOrDefaultAsync<ContentPost>();
        if (post is null)
        {
            return null;
        }

        var aggregate = new ContentPostAggregate
        {
            Post = post,
            Hashtags = (await grid.ReadAsync<ContentHashtag>()).AsList(),
            Tags = (await grid.ReadAsync<ContentTag>()).AsList(),
            Platforms = (await grid.ReadAsync<ContentPostPlatform>()).AsList(),
            Poll = await grid.ReadSingleOrDefaultAsync<ContentPoll>(),
            PollOptions = (await grid.ReadAsync<ContentPollOption>()).AsList(),
            Ctas = (await grid.ReadAsync<ContentCta>()).AsList(),
            Mentions = (await grid.ReadAsync<ContentMention>()).AsList()
        };
        aggregate.Location = await grid.ReadSingleOrDefaultAsync<ContentLocation>();
        aggregate.Media.AddRange(await grid.ReadAsync<ContentPostMedia>());
        return aggregate;
    }

    public async Task<bool> ExistsAsync(Guid agencyId, Guid userId, Guid postId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM content_posts WHERE id = @postId AND agency_id = @agencyId)",
            new { postId, agencyId },
            cancellationToken: cancellationToken));
    }

    // --- Writing --------------------------------------------------------------------------------

    public async Task InsertAsync(Guid userId, ContentPostAggregate post, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(post.Post.AgencyId, userId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO content_posts
                (id, agency_id, workspace_id, brand_id, campaign_id, created_by, updated_by, title, content_type, body, body_doc, excerpt,
                 status, visibility, language_code, timezone, scheduled_at, published_at, archived_at, seo_title, seo_description,
                 seo_slug, seo_keywords, settings)
            VALUES
                (@Id, @AgencyId, @WorkspaceId, @BrandId, @CampaignId, @CreatedBy, @UpdatedBy, @Title, @ContentType, @Body, @BodyDoc, @Excerpt,
                 @Status, @Visibility, @LanguageCode, @Timezone, @ScheduledAt, @PublishedAt, @ArchivedAt, @SeoTitle, @SeoDescription,
                 @SeoSlug, @SeoKeywords, @Settings)
            """,
            PostParameters(post.Post),
            transaction,
            cancellationToken: cancellationToken));
        await WriteChildrenAsync(connection, transaction, post, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SaveOutcome> UpdateAsync(Guid userId, ContentPostAggregate post, DateTimeOffset expectedUpdatedAt, CancellationToken cancellationToken)
    {
        var row = post.Post;
        await using var connection = await connections.OpenAsync(row.AgencyId, userId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Lock the post only if nobody saved since the page loaded it, and lock its targets so the worker cannot claim one while
        // they are replaced (it skips rows that are locked).
        var locked = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM content_posts WHERE id = @Id AND agency_id = @AgencyId AND updated_at = @expectedUpdatedAt FOR UPDATE",
            new { row.Id, row.AgencyId, expectedUpdatedAt },
            transaction,
            cancellationToken: cancellationToken));
        if (locked is null)
        {
            return SaveOutcome.Changed;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT id FROM content_post_platforms WHERE post_id = @Id AND agency_id = @AgencyId FOR UPDATE",
            new { row.Id, row.AgencyId },
            transaction,
            cancellationToken: cancellationToken));
        var busy = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (SELECT 1 FROM content_post_platforms
                           WHERE post_id = @Id AND agency_id = @AgencyId AND publish_status IN ('publishing', 'published'))
            """,
            new { row.Id, row.AgencyId },
            transaction,
            cancellationToken: cancellationToken));
        if (busy)
        {
            return SaveOutcome.Busy;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE content_posts SET
                campaign_id = @CampaignId, updated_by = @UpdatedBy, title = @Title, content_type = @ContentType, body = @Body,
                body_doc = @BodyDoc, excerpt = @Excerpt, visibility = @Visibility, language_code = @LanguageCode, timezone = @Timezone,
                scheduled_at = @ScheduledAt, seo_title = @SeoTitle, seo_description = @SeoDescription, seo_slug = @SeoSlug,
                seo_keywords = @SeoKeywords, settings = @Settings
            WHERE id = @Id AND agency_id = @AgencyId
            """,
            PostParameters(row),
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM content_hashtags WHERE post_id = @Id AND agency_id = @AgencyId;
            DELETE FROM content_post_tags WHERE post_id = @Id AND agency_id = @AgencyId;
            DELETE FROM content_post_platforms WHERE post_id = @Id AND agency_id = @AgencyId;
            DELETE FROM content_polls WHERE post_id = @Id AND agency_id = @AgencyId;
            DELETE FROM content_ctas WHERE post_id = @Id AND agency_id = @AgencyId;
            DELETE FROM content_mentions WHERE post_id = @Id AND agency_id = @AgencyId;
            DELETE FROM content_locations WHERE post_id = @Id AND agency_id = @AgencyId;
            DELETE FROM content_post_media WHERE post_id = @Id AND agency_id = @AgencyId;
            """,
            new { row.Id, row.AgencyId },
            transaction,
            cancellationToken: cancellationToken));
        await WriteChildrenAsync(connection, transaction, post, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return SaveOutcome.Saved;
    }

    private static object PostParameters(ContentPost post) => new
    {
        post.Id,
        post.AgencyId,
        post.WorkspaceId,
        post.BrandId,
        post.CampaignId,
        post.CreatedBy,
        post.UpdatedBy,
        post.Title,
        post.ContentType,
        post.Body,
        BodyDoc = new JsonbParameter(post.BodyDoc),
        post.Excerpt,
        post.Status,
        post.Visibility,
        post.LanguageCode,
        post.Timezone,
        post.ScheduledAt,
        post.PublishedAt,
        post.ArchivedAt,
        post.SeoTitle,
        post.SeoDescription,
        post.SeoSlug,
        SeoKeywords = new PgTextArray(post.SeoKeywords),
        Settings = new JsonbParameter(post.Settings)
    };

    private static async Task WriteChildrenAsync(
        NpgsqlConnection connection, DbTransaction transaction, ContentPostAggregate post, CancellationToken cancellationToken)
    {
        var row = post.Post;

        if (post.Hashtags.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO content_hashtags (id, agency_id, post_id, hashtag, display_order) VALUES (@Id, @AgencyId, @PostId, @Hashtag, @DisplayOrder)",
                post.Hashtags,
                transaction,
                cancellationToken: cancellationToken));
        }

        if (post.Tags.Count > 0)
        {
            var names = post.Tags.Select(t => t.Name).ToList();
            var colors = post.Tags.Select(t => t.Color).ToList();
            // A name the workspace already has (any letter case) keeps its tag and its colour; the link is made either way.
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO content_tags (agency_id, workspace_id, name, color)
                SELECT @agencyId, @workspaceId, n.name, n.color FROM unnest(@names, @colors) AS n (name, color)
                ON CONFLICT (workspace_id, lower(name)) DO NOTHING
                """,
                new { agencyId = row.AgencyId, workspaceId = row.WorkspaceId, names = new PgTextArray(names), colors = new PgTextArray(colors) },
                transaction,
                cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO content_post_tags (post_id, tag_id, agency_id)
                SELECT @postId, t.id, @agencyId FROM content_tags t
                WHERE t.agency_id = @agencyId AND t.workspace_id = @workspaceId AND lower(t.name) IN (SELECT lower(x) FROM unnest(@names) AS x)
                """,
                new { postId = row.Id, agencyId = row.AgencyId, workspaceId = row.WorkspaceId, names = new PgTextArray(names) },
                transaction,
                cancellationToken: cancellationToken));
        }

        if (post.Platforms.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO content_post_platforms
                    (id, agency_id, post_id, social_account_id, platform, platform_caption, platform_title, platform_settings, publish_status, scheduled_at)
                VALUES
                    (@Id, @AgencyId, @PostId, @SocialAccountId, @Platform, @PlatformCaption, @PlatformTitle, @PlatformSettings, @PublishStatus, @ScheduledAt)
                """,
                post.Platforms.Select(p => new
                {
                    p.Id, p.AgencyId, p.PostId, p.SocialAccountId, p.Platform, p.PlatformCaption, p.PlatformTitle,
                    PlatformSettings = new JsonbParameter(p.PlatformSettings), p.PublishStatus, p.ScheduledAt
                }).ToList(),
                transaction,
                cancellationToken: cancellationToken));
        }

        if (post.Poll is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO content_polls (id, agency_id, post_id, question, ends_at) VALUES (@Id, @AgencyId, @PostId, @Question, @EndsAt)",
                post.Poll,
                transaction,
                cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO content_poll_options (id, agency_id, poll_id, option_text, display_order) VALUES (@Id, @AgencyId, @PollId, @OptionText, @DisplayOrder)",
                post.PollOptions,
                transaction,
                cancellationToken: cancellationToken));
        }

        if (post.Ctas.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO content_ctas (id, agency_id, post_id, button_text, target_url, display_order) VALUES (@Id, @AgencyId, @PostId, @ButtonText, @TargetUrl, @DisplayOrder)",
                post.Ctas,
                transaction,
                cancellationToken: cancellationToken));
        }

        if (post.Mentions.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO content_mentions (id, agency_id, post_id, platform, mentioned_name, platform_user_id) VALUES (@Id, @AgencyId, @PostId, @Platform, @MentionedName, @PlatformUserId)",
                post.Mentions,
                transaction,
                cancellationToken: cancellationToken));
        }

        if (post.Location is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO content_locations (id, agency_id, post_id, location_name, latitude, longitude, external_place_id)
                VALUES (@Id, @AgencyId, @PostId, @LocationName, @Latitude, @Longitude, @ExternalPlaceId)
                """,
                post.Location,
                transaction,
                cancellationToken: cancellationToken));
        }

        if (post.Media.Count > 0)
        {
            try
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO content_post_media (id, agency_id, post_id, media_asset_id, display_order, media_role, caption) VALUES (@Id, @AgencyId, @PostId, @MediaAssetId, @DisplayOrder, @MediaRole, @Caption)",
                    post.Media,
                    transaction,
                    cancellationToken: cancellationToken));
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
            {
                // The file was deleted from the library between the service's check and this write. The whole save is rolled back.
                throw new BadRequestException(
                    "A file was not found in the Media Library.",
                    "media_asset_not_found",
                    new Dictionary<string, string[]> { ["media"] = ["A file you chose is no longer in the Media Library."] });
            }
        }
    }

    // --- Status changes and deletion --------------------------------------------------------------

    public async Task<bool> DeleteAsync(Guid agencyId, Guid userId, Guid postId, IReadOnlyCollection<string> allowedStatuses, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        var deleted = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM content_posts WHERE id = @postId AND agency_id = @agencyId AND status = ANY(@allowed)",
            new { postId, agencyId, allowed = new PgTextArray(allowedStatuses) },
            cancellationToken: cancellationToken));
        return deleted == 1;
    }

    public async Task<bool> ArchiveAsync(
        Guid agencyId, Guid userId, Guid postId, IReadOnlyCollection<string> allowedFrom, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        var changed = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE content_posts SET status = 'archived', archived_at = @now, updated_by = @userId
            WHERE id = @postId AND agency_id = @agencyId AND status = ANY(@allowed)
            """,
            new { postId, agencyId, userId, now, allowed = new PgTextArray(allowedFrom) },
            cancellationToken: cancellationToken));
        return changed == 1;
    }

    public async Task<string?> RestoreAsync(Guid agencyId, Guid userId, Guid postId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            UPDATE content_posts
            SET status = CASE WHEN published_at IS NOT NULL THEN 'published' ELSE 'draft' END, archived_at = NULL, updated_by = @userId
            WHERE id = @postId AND agency_id = @agencyId AND status = 'archived'
            RETURNING status
            """,
            new { postId, agencyId, userId },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ContentRevision>> ListRevisionsAsync(Guid agencyId, Guid userId, Guid postId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(agencyId, userId, cancellationToken);
        return (await connection.QueryAsync<ContentRevision>(new CommandDefinition(
            """
            SELECT id, revision_number, title, char_length(body) AS body_length, changed_by, created_at
            FROM content_revisions
            WHERE post_id = @postId AND agency_id = @agencyId
            ORDER BY revision_number DESC
            """,
            new { postId, agencyId },
            cancellationToken: cancellationToken))).AsList();
    }
}
