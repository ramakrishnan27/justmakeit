using Dapper;
using Npgsql;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>
/// <c>014_content_studio.sql</c>: the Content Studio tables. Rows are arranged as the database owner (a superuser in the test
/// server, so row-level security does not apply to it); everything under test runs as app_user, like the API.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Database")]
public sealed class ContentStudioMigrationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly string[] Tables =
    [
        "content_posts", "content_post_media", "content_tags", "content_post_tags", "content_hashtags", "content_post_platforms",
        "content_polls", "content_poll_options", "content_ctas", "content_mentions", "content_locations", "content_templates",
        "content_ai_generations", "content_revisions"
    ];

    public static TheoryData<string> TableNames()
    {
        var data = new TheoryData<string>();
        foreach (var table in Tables)
        {
            data.Add(table);
        }

        return data;
    }

    private TestDatabase _db = null!;
    private Guid _agencyA, _agencyB, _user, _workspaceA, _workspaceB;
    private Guid _postA, _postA2, _postB, _tagA, _tagB, _poll, _account, _asset;

    public async Task InitializeAsync()
    {
        _db = await postgres.CreateDatabaseAsync();
        await using var owner = await _db.OpenOwnerAsync();

        _agencyA = await DbTestSupport.InsertAgencyAsync(owner, "Agency A", "company");
        _agencyB = await DbTestSupport.InsertAgencyAsync(owner, "Agency B", "company");
        _user = await DbTestSupport.InsertUserAsync(owner, "author@example.com");
        _workspaceA = await DbTestSupport.InsertWorkspaceAsync(owner, _agencyA, "Workspace A");
        _workspaceB = await DbTestSupport.InsertWorkspaceAsync(owner, _agencyB, "Workspace B");
        _account = Guid.NewGuid();
        _asset = Guid.NewGuid();
        // A file of the Media Library: since 016 the key of content_post_media is real, so a post can only use a file that exists.
        await owner.ExecuteAsync(
            "INSERT INTO media_assets (id, agency_id, workspace_id, uploaded_by, file_name, mime_type, media_type, file_size_bytes, storage_key) " +
            "VALUES (@asset, @agency, @workspace, @user, 'cover.png', 'image/png', 'image', 1000, @key)",
            new { asset = _asset, agency = _agencyA, workspace = _workspaceA, user = _user, key = $"{_agencyA:N}/{_asset:N}.png" });

        const string post =
            "INSERT INTO content_posts (agency_id, workspace_id, created_by, title, content_type, body, excerpt, seo_slug) " +
            "VALUES (@a, @w, @u, @title, @type, @body, 'An excerpt', @slug) RETURNING id";
        _postA = await owner.ExecuteScalarAsync<Guid>(post, new { a = _agencyA, w = _workspaceA, u = _user, title = "Dream Bigger", type = "social_post", body = "Create. Inspire.", slug = "dream-bigger" });
        _postA2 = await owner.ExecuteScalarAsync<Guid>(post, new { a = _agencyA, w = _workspaceA, u = _user, title = "Second", type = "blog_post", body = "Body", slug = (string?)null });
        _postB = await owner.ExecuteScalarAsync<Guid>(post, new { a = _agencyB, w = _workspaceB, u = _user, title = "Org B secret", type = "social_post", body = "Hidden", slug = (string?)null });

        var p = new { a = _agencyA, b = _agencyB, w = _workspaceA, wb = _workspaceB, p = _postA, pb = _postB, acct = _account, asset = _asset };
        await owner.ExecuteAsync("INSERT INTO content_post_media (agency_id, post_id, media_asset_id, media_role) VALUES (@a, @p, @asset, 'cover')", p);
        _tagA = await owner.ExecuteScalarAsync<Guid>("INSERT INTO content_tags (agency_id, workspace_id, name, color) VALUES (@a, @w, 'Motivation', '#1F7A55') RETURNING id", p);
        _tagB = await owner.ExecuteScalarAsync<Guid>("INSERT INTO content_tags (agency_id, workspace_id, name) VALUES (@b, @wb, 'B tag') RETURNING id", p);
        await owner.ExecuteAsync("INSERT INTO content_post_tags (post_id, tag_id, agency_id) VALUES (@p, @tag, @a)", new { p = _postA, tag = _tagA, a = _agencyA });
        await owner.ExecuteAsync("INSERT INTO content_hashtags (agency_id, post_id, hashtag) VALUES (@a, @p, 'GrowTogether')", p);
        await owner.ExecuteAsync(
            "INSERT INTO content_post_platforms (agency_id, post_id, social_account_id, platform, external_post_id) VALUES (@a, @p, @acct, 'instagram', 'ext-1')", p);
        _poll = await owner.ExecuteScalarAsync<Guid>("INSERT INTO content_polls (agency_id, post_id, question) VALUES (@a, @p, 'Pick one?') RETURNING id", p);
        await owner.ExecuteAsync("INSERT INTO content_poll_options (agency_id, poll_id, option_text) VALUES (@a, @poll, 'Yes')", new { a = _agencyA, poll = _poll });
        await owner.ExecuteAsync("INSERT INTO content_ctas (agency_id, post_id, button_text, target_url) VALUES (@a, @p, 'Learn more', 'https://example.com')", p);
        await owner.ExecuteAsync("INSERT INTO content_mentions (agency_id, post_id, platform, mentioned_name, platform_user_id) VALUES (@a, @p, 'x', 'Krishna', '123')", p);
        await owner.ExecuteAsync("INSERT INTO content_locations (agency_id, post_id, location_name, latitude, longitude) VALUES (@a, @p, 'Chennai', 13.0827, 80.2707)", p);
        await owner.ExecuteAsync("INSERT INTO content_templates (agency_id, workspace_id, name, content_type) VALUES (@a, @w, 'Dream Bigger', 'social_post')", p);
        await owner.ExecuteAsync(
            "INSERT INTO content_ai_generations (agency_id, workspace_id, post_id, generation_type, provider, model_name, prompt) VALUES (@a, @w, @p, 'caption', 'test', 'test-model', 'Write a caption')", p);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private object Ids => new
    {
        a = _agencyA, b = _agencyB, w = _workspaceA, wb = _workspaceB, p = _postA, p2 = _postA2, pb = _postB,
        tag = _tagA, tagb = _tagB, poll = _poll, acct = _account, asset = _asset, u = _user
    };

    private static async Task<string?> SqlStateAsync(NpgsqlConnection connection, string sql, object parameters)
    {
        try
        {
            await connection.ExecuteAsync(sql, parameters);
            return null;
        }
        catch (PostgresException exception)
        {
            return exception.SqlState;
        }
    }

    // --- Shape ---------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(TableNames))]
    public async Task EveryContentTable_HasRowLevelSecurityEnabledAndForced_AndTheOneTenantPolicy(string table)
    {
        await using var owner = await _db.OpenOwnerAsync();

        var (enabled, forced) = await owner.QuerySingleAsync<(bool, bool)>(
            "SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE oid = to_regclass(@t)", new { t = "public." + table });
        var policies = (await owner.QueryAsync<string>("SELECT policyname FROM pg_policies WHERE tablename = @table", new { table })).ToList();

        Assert.True(enabled);
        Assert.True(forced);
        // The one tenant policy, and possibly policies for the migrating role (015: the publishing claim needs two tables), never for app_user.
        Assert.Contains("tenant_isolation", policies);
        Assert.All(policies.Where(p => p != "tenant_isolation"), p => Assert.StartsWith("owner_", p));
    }

    [Fact]
    public async Task TheScriptRunsAgain_WithoutChangingOrDuplicatingAnything()
    {
        await _db.MigrateAsync();
        await _db.MigrateAsync();

        await using var owner = await _db.OpenOwnerAsync();
        Assert.Equal(3, await owner.QuerySingleAsync<long>("SELECT count(*) FROM content_posts"));
        Assert.Equal(1, await owner.QuerySingleAsync<long>("SELECT count(*) FROM content_revisions WHERE post_id = @p", new { p = _postA }));
        Assert.Equal(4, await owner.QuerySingleAsync<long>(
            "SELECT count(*) FROM pg_trigger WHERE tgrelid IN ('content_posts'::regclass, 'content_post_platforms'::regclass, 'content_templates'::regclass) AND NOT tgisinternal"));
    }

    // --- Tenant isolation ----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(TableNames))]
    public async Task WithoutATenant_NoRowsAreVisible(string table)
    {
        await using var app = await _db.OpenAppAsync();

        Assert.Equal(0, await DbTestSupport.CountAsync(app, table));
    }

    [Theory]
    [MemberData(nameof(TableNames))]
    public async Task EachOrganization_SeesItsOwnRows_AndNeverTheOthers(string table)
    {
        await using var asA = await _db.OpenAppAsync(_agencyA, _user);
        await using var asB = await _db.OpenAppAsync(_agencyB, _user);

        // Table names come from this class, never from input.
        Assert.True(await asA.QuerySingleAsync<long>($"SELECT count(*) FROM {table} WHERE agency_id = @a", new { a = _agencyA }) >= 1);
        Assert.Equal(0, await asA.QuerySingleAsync<long>($"SELECT count(*) FROM {table} WHERE agency_id = @b", new { b = _agencyB }));
        Assert.Equal(0, await asB.QuerySingleAsync<long>($"SELECT count(*) FROM {table} WHERE agency_id = @a", new { a = _agencyA }));
    }

    [Fact]
    public async Task ARowOfAnotherOrganization_CannotBeWritten()
    {
        await using var app = await _db.OpenAppAsync(_agencyA, _user);

        var state = await SqlStateAsync(
            app, "INSERT INTO content_posts (agency_id, workspace_id, content_type) VALUES (@b, @wb, 'blog_post')", Ids);

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, state);
    }

    [Theory]
    [InlineData("INSERT INTO content_post_media (agency_id, post_id, media_asset_id) VALUES (@a, @pb, gen_random_uuid())")]
    [InlineData("INSERT INTO content_hashtags (agency_id, post_id, hashtag) VALUES (@a, @pb, 'x')")]
    [InlineData("INSERT INTO content_polls (agency_id, post_id, question) VALUES (@a, @pb, 'q')")]
    [InlineData("INSERT INTO content_ai_generations (agency_id, workspace_id, post_id, generation_type, provider, model_name, prompt) VALUES (@a, @w, @pb, 'caption', 'p', 'm', 'x')")]
    [InlineData("INSERT INTO content_post_tags (post_id, tag_id, agency_id) VALUES (@p, @tagb, @a)")]
    public async Task AChild_CannotPointAtAnotherOrganizationsPostOrTag(string sql)
    {
        await using var app = await _db.OpenAppAsync(_agencyA, _user);

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, await SqlStateAsync(app, sql, Ids));
    }

    // --- Revisions -----------------------------------------------------------------------------

    [Fact]
    public async Task EveryPost_StartsWithRevisionOne_AttributedToItsAuthor_AndTheSnapshotDoesNotCopyTitleOrBody()
    {
        await using var app = await _db.OpenAppAsync(_agencyA, _user);

        var revision = await app.QuerySingleAsync<(int Number, Guid? ChangedBy, bool HasType, bool HasBody, bool HasTitle, bool HasVector)>(
            "SELECT revision_number, changed_by, jsonb_exists(snapshot, 'content_type'), jsonb_exists(snapshot, 'body'), " +
            "jsonb_exists(snapshot, 'title'), jsonb_exists(snapshot, 'search_vector') " +
            "FROM content_revisions WHERE post_id = @p", new { p = _postA });

        Assert.Equal((1, (Guid?)_user, true, false, false, false), revision);
    }

    [Fact]
    public async Task EditingContent_AppendsARevision_ButChangingStatusScheduleOrVisibility_DoesNot()
    {
        await using var app = await _db.OpenAppAsync(_agencyA, _user);

        await app.ExecuteAsync(
            "UPDATE content_posts SET status = 'scheduled', scheduled_at = now() + interval '1 day', visibility = 'public' WHERE id = @p", new { p = _postA });
        Assert.Equal(1, await app.QuerySingleAsync<long>("SELECT count(*) FROM content_revisions WHERE post_id = @p", new { p = _postA }));

        await app.ExecuteAsync("UPDATE content_posts SET body = 'Edited body', updated_by = @u WHERE id = @p", new { p = _postA, u = _user });
        await app.ExecuteAsync("UPDATE content_posts SET seo_title = 'SEO', updated_by = @u WHERE id = @p", new { p = _postA, u = _user });
        await app.ExecuteAsync("UPDATE content_posts SET title = title WHERE id = @p", new { p = _postA });

        var numbers = (await app.QueryAsync<int>(
            "SELECT revision_number FROM content_revisions WHERE post_id = @p ORDER BY revision_number", new { p = _postA })).ToList();
        Assert.Equal([1, 2, 3], numbers);
        Assert.Equal("Edited body", await app.QuerySingleAsync<string>(
            "SELECT body FROM content_revisions WHERE post_id = @p AND revision_number = 2", new { p = _postA }));
    }

    [Theory]
    [InlineData("UPDATE content_revisions SET body = 'changed' WHERE post_id = @p")]
    [InlineData("DELETE FROM content_revisions WHERE post_id = @p")]
    public async Task TheApplicationRole_CannotChangeOrDeleteHistory(string sql)
    {
        await using var app = await _db.OpenAppAsync(_agencyA, _user);

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await SqlStateAsync(app, sql, Ids));
    }

    // --- Rules the database holds --------------------------------------------------------------

    [Fact]
    public async Task TheDatabaseRefusesWhatTheRulesForbid()
    {
        (string Label, string Sql, string State)[] cases =
        [
            ("content type", "INSERT INTO content_posts (agency_id, workspace_id, content_type) VALUES (@a, @w, 'podcast')", "23514"),
            ("status", "INSERT INTO content_posts (agency_id, workspace_id, content_type, status) VALUES (@a, @w, 'blog_post', 'live')", "23514"),
            ("visibility", "INSERT INTO content_posts (agency_id, workspace_id, content_type, visibility) VALUES (@a, @w, 'blog_post', 'secret')", "23514"),
            ("scheduled needs a time", "INSERT INTO content_posts (agency_id, workspace_id, content_type, status) VALUES (@a, @w, 'blog_post', 'scheduled')", "23514"),
            ("published needs a time", "INSERT INTO content_posts (agency_id, workspace_id, content_type, status) VALUES (@a, @w, 'blog_post', 'published')", "23514"),
            ("archived needs a time", "INSERT INTO content_posts (agency_id, workspace_id, content_type, status) VALUES (@a, @w, 'blog_post', 'archived')", "23514"),
            ("a time needs archived", "INSERT INTO content_posts (agency_id, workspace_id, content_type, archived_at) VALUES (@a, @w, 'blog_post', now())", "23514"),
            ("slug shape", "INSERT INTO content_posts (agency_id, workspace_id, content_type, seo_slug) VALUES (@a, @w, 'blog_post', 'Not A Slug')", "23514"),
            ("slug unique per workspace", "INSERT INTO content_posts (agency_id, workspace_id, content_type, seo_slug) VALUES (@a, @w, 'blog_post', 'dream-bigger')", "23505"),
            ("language code", "INSERT INTO content_posts (agency_id, workspace_id, content_type, language_code) VALUES (@a, @w, 'blog_post', 'English')", "23514"),
            ("settings is an object", "INSERT INTO content_posts (agency_id, workspace_id, content_type, settings) VALUES (@a, @w, 'blog_post', '[]')", "23514"),
            ("media role", "INSERT INTO content_post_media (agency_id, post_id, media_asset_id, media_role) VALUES (@a, @p, gen_random_uuid(), 'banner')", "23514"),
            ("second cover", "INSERT INTO content_post_media (agency_id, post_id, media_asset_id, media_role) VALUES (@a, @p, gen_random_uuid(), 'cover')", "23505"),
            ("same asset twice in a role", "INSERT INTO content_post_media (agency_id, post_id, media_asset_id, media_role) VALUES (@a, @p, @asset, 'cover')", "23505"),
            ("tag name in another letter case", "INSERT INTO content_tags (agency_id, workspace_id, name) VALUES (@a, @w, 'motivation')", "23505"),
            ("tag colour", "INSERT INTO content_tags (agency_id, workspace_id, name, color) VALUES (@a, @w, 'Other', 'green')", "23514"),
            ("hashtag with a #", "INSERT INTO content_hashtags (agency_id, post_id, hashtag) VALUES (@a, @p, '#oops')", "23514"),
            ("hashtag twice", "INSERT INTO content_hashtags (agency_id, post_id, hashtag) VALUES (@a, @p, 'growtogether')", "23505"),
            ("platform name", "INSERT INTO content_post_platforms (agency_id, post_id, social_account_id, platform) VALUES (@a, @p, gen_random_uuid(), 'myspace')", "23514"),
            ("account twice on a post", "INSERT INTO content_post_platforms (agency_id, post_id, social_account_id, platform) VALUES (@a, @p, @acct, 'instagram')", "23505"),
            ("publish status", "INSERT INTO content_post_platforms (agency_id, post_id, social_account_id, platform, publish_status) VALUES (@a, @p, gen_random_uuid(), 'x', 'done')", "23514"),
            ("platform scheduled needs a time", "INSERT INTO content_post_platforms (agency_id, post_id, social_account_id, platform, publish_status) VALUES (@a, @p, gen_random_uuid(), 'x', 'scheduled')", "23514"),
            ("external url scheme", "INSERT INTO content_post_platforms (agency_id, post_id, social_account_id, platform, external_post_url) VALUES (@a, @p, gen_random_uuid(), 'x', 'javascript:alert(1)')", "23514"),
            ("external id twice in an account", "INSERT INTO content_post_platforms (agency_id, post_id, social_account_id, platform, external_post_id) VALUES (@a, @p2, @acct, 'instagram', 'ext-1')", "23505"),
            ("negative retry count", "INSERT INTO content_post_platforms (agency_id, post_id, social_account_id, platform, retry_count) VALUES (@a, @p, gen_random_uuid(), 'x', -1)", "23514"),
            ("second poll on a post", "INSERT INTO content_polls (agency_id, post_id, question) VALUES (@a, @p, 'again?')", "23505"),
            ("poll option twice", "INSERT INTO content_poll_options (agency_id, poll_id, option_text) VALUES (@a, @poll, 'YES')", "23505"),
            ("call to action url scheme", "INSERT INTO content_ctas (agency_id, post_id, button_text, target_url) VALUES (@a, @p, 'Go', 'javascript:alert(1)')", "23514"),
            ("mention platform", "INSERT INTO content_mentions (agency_id, post_id, platform, mentioned_name) VALUES (@a, @p, 'myspace', 'x')", "23514"),
            ("mention twice", "INSERT INTO content_mentions (agency_id, post_id, platform, mentioned_name, platform_user_id) VALUES (@a, @p, 'x', 'Other name', '123')", "23505"),
            ("location needs both coordinates", "INSERT INTO content_locations (agency_id, post_id, location_name, latitude) VALUES (@a, @p2, 'Somewhere', 10)", "23514"),
            ("latitude range", "INSERT INTO content_locations (agency_id, post_id, location_name, latitude, longitude) VALUES (@a, @p2, 'Somewhere', 91, 10)", "23514"),
            ("second location on a post", "INSERT INTO content_locations (agency_id, post_id, location_name) VALUES (@a, @p, 'Elsewhere')", "23505"),
            ("template type", "INSERT INTO content_templates (agency_id, workspace_id, name, content_type) VALUES (@a, @w, 'T2', 'podcast')", "23514"),
            ("template name per workspace", "INSERT INTO content_templates (agency_id, workspace_id, name, content_type) VALUES (@a, @w, 'dream bigger', 'blog_post')", "23505"),
            ("generation type", "INSERT INTO content_ai_generations (agency_id, workspace_id, generation_type, provider, model_name, prompt) VALUES (@a, @w, 'poem', 'p', 'm', 'x')", "23514"),
            ("generation status", "INSERT INTO content_ai_generations (agency_id, workspace_id, generation_type, provider, model_name, prompt, status) VALUES (@a, @w, 'caption', 'p', 'm', 'x', 'done')", "23514"),
            ("a revision number used twice", "INSERT INTO content_revisions (agency_id, post_id, revision_number, title, body, snapshot) VALUES (@a, @p, 1, 't', 'b', '{}')", "23505")
        ];

        await using var app = await _db.OpenAppAsync(_agencyA, _user);
        var mismatches = new List<string>();
        foreach (var (label, sql, expected) in cases)
        {
            var actual = await SqlStateAsync(app, sql, Ids);
            if (actual != expected)
            {
                mismatches.Add($"{label}: expected {expected}, got {actual ?? "no error"}");
            }
        }

        Assert.Empty(mismatches);
    }

    [Fact]
    public async Task OneAssetMayHaveTwoRolesOnAPost_AndAPostMayGoToSeveralAccounts()
    {
        await using var app = await _db.OpenAppAsync(_agencyA, _user);

        await app.ExecuteAsync("INSERT INTO content_post_media (agency_id, post_id, media_asset_id, media_role) VALUES (@a, @p, @asset, 'attachment')", Ids);
        await app.ExecuteAsync(
            "INSERT INTO content_post_platforms (agency_id, post_id, social_account_id, platform, platform_caption, publish_status, scheduled_at) " +
            "VALUES (@a, @p, gen_random_uuid(), 'linkedin', 'A caption just for LinkedIn', 'scheduled', now() + interval '1 day')", Ids);

        Assert.Equal(2, await app.QuerySingleAsync<long>("SELECT count(*) FROM content_post_media WHERE post_id = @p", Ids));
        Assert.Equal(2, await app.QuerySingleAsync<long>("SELECT count(*) FROM content_post_platforms WHERE post_id = @p", Ids));
    }

    [Fact]
    public async Task SearchFindsAPostByTitleOrByPrefixOfItsBody_InItsOwnOrganizationOnly()
    {
        await using var asA = await _db.OpenAppAsync(_agencyA, _user);
        await using var asB = await _db.OpenAppAsync(_agencyB, _user);

        Assert.Equal(1, await asA.QuerySingleAsync<long>("SELECT count(*) FROM content_posts WHERE search_vector @@ plainto_tsquery('simple', 'dream')"));
        Assert.Equal(1, await asA.QuerySingleAsync<long>("SELECT count(*) FROM content_posts WHERE search_vector @@ to_tsquery('simple', 'ins:*')"));
        Assert.Equal(0, await asB.QuerySingleAsync<long>("SELECT count(*) FROM content_posts WHERE search_vector @@ plainto_tsquery('simple', 'dream')"));
    }

    // --- Deleting things -----------------------------------------------------------------------

    [Fact]
    public async Task DeletingAPost_RemovesWhatHangsOffIt_ButKeepsTheAiGenerationAndTheTag()
    {
        await using var app = await _db.OpenAppAsync(_agencyA, _user);

        await app.ExecuteAsync("DELETE FROM content_posts WHERE id = @p", Ids);

        foreach (var table in new[] { "content_revisions", "content_post_media", "content_post_tags", "content_hashtags", "content_post_platforms", "content_polls", "content_ctas", "content_mentions", "content_locations" })
        {
            Assert.Equal(0, await app.QuerySingleAsync<long>($"SELECT count(*) FROM {table} WHERE post_id = @p", Ids));
        }

        Assert.Equal(0, await app.QuerySingleAsync<long>("SELECT count(*) FROM content_poll_options WHERE poll_id = @poll", Ids));
        var generation = await app.QuerySingleAsync<(Guid? PostId, Guid AgencyId)>("SELECT post_id, agency_id FROM content_ai_generations WHERE workspace_id = @w", Ids);
        Assert.Equal((null, _agencyA), generation);
        Assert.Equal(1, await app.QuerySingleAsync<long>("SELECT count(*) FROM content_tags WHERE id = @tag", Ids));
    }

    [Fact]
    public async Task DeletingTheAuthor_KeepsTheirPostsAndRevisions_AndForgetsWhoWroteThem()
    {
        // What removing a team member's last membership does (team_remove_member_v2 deletes the users row).
        await using (var owner = await _db.OpenOwnerAsync())
        {
            await owner.ExecuteAsync("DELETE FROM users WHERE id = @u", Ids);
        }

        await using var app = await _db.OpenAppAsync(_agencyA, null);
        Assert.Equal(2, await app.QuerySingleAsync<long>("SELECT count(*) FROM content_posts WHERE created_by IS NULL"));
        Assert.Equal(2, await app.QuerySingleAsync<long>("SELECT count(*) FROM content_revisions WHERE changed_by IS NULL"));
    }

    [Fact]
    public async Task DeletingAWorkspace_RemovesItsContent_AndDeletingTheOrganizationRemovesEverything()
    {
        await using var owner = await _db.OpenOwnerAsync();

        await owner.ExecuteAsync("DELETE FROM workspaces WHERE id = @w", Ids);
        Assert.Equal(0, await owner.QuerySingleAsync<long>("SELECT count(*) FROM content_posts WHERE agency_id = @a", Ids));
        Assert.Equal(0, await owner.QuerySingleAsync<long>("SELECT count(*) FROM content_templates WHERE agency_id = @a", Ids));

        await owner.ExecuteAsync("DELETE FROM agencies WHERE id = @b", Ids);
        foreach (var table in Tables)
        {
            Assert.Equal(0, await owner.QuerySingleAsync<long>($"SELECT count(*) FROM {table}"));
        }
    }
}
