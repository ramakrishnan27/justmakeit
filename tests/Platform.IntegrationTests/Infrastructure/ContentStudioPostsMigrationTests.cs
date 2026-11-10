using Dapper;
using Npgsql;
using Platform.Auth.Services;
using Platform.Helpers.Constants;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>
/// <c>015_content_studio_posts.sql</c>: the two permissions and their defaults per business model, the editor's document, publishing
/// targets without an account, revisions that see a change to the document, and the function the publishing worker claims work with.
/// Rows are arranged as the database owner (a superuser in the test server); everything under test runs as app_user.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Database")]
public sealed class ContentStudioPostsMigrationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private TestDatabase _db = null!;
    private Guid _agencyA, _agencyB, _user, _workspaceA, _workspaceB;

    public async Task InitializeAsync()
    {
        _db = await postgres.CreateDatabaseAsync();
        await using var owner = await _db.OpenOwnerAsync();
        _agencyA = await DbTestSupport.InsertAgencyAsync(owner, "Agency A", "company");
        _agencyB = await DbTestSupport.InsertAgencyAsync(owner, "Agency B", "company");
        _user = await DbTestSupport.InsertUserAsync(owner, "author@example.com");
        _workspaceA = await DbTestSupport.InsertWorkspaceAsync(owner, _agencyA);
        _workspaceB = await DbTestSupport.InsertWorkspaceAsync(owner, _agencyB);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<Guid> PostAsync(Guid agencyId, Guid workspaceId, string status = "draft", DateTimeOffset? scheduledAt = null, string? bodyDoc = null)
    {
        await using var owner = await _db.OpenOwnerAsync();
        return await owner.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO content_posts
                (agency_id, workspace_id, created_by, title, content_type, body, body_doc, status, scheduled_at, published_at, archived_at)
            VALUES
                (@agencyId, @workspaceId, @user, 'T', 'social_post', 'Body', @bodyDoc::jsonb, @status, @scheduledAt,
                 CASE WHEN @status = 'published' THEN now() END, CASE WHEN @status = 'archived' THEN now() END)
            RETURNING id
            """,
            new { agencyId, workspaceId, user = _user, status, scheduledAt, bodyDoc });
    }

    private async Task<Guid> TargetAsync(
        Guid agencyId, Guid postId, string platform, string status, DateTimeOffset? scheduledAt, TimeSpan? updatedAgo = null)
    {
        await using var owner = await _db.OpenOwnerAsync();
        return await owner.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO content_post_platforms (agency_id, post_id, social_account_id, platform, publish_status, scheduled_at, updated_at)
            VALUES (@agencyId, @postId, NULL, @platform, @status, @scheduledAt, now() - make_interval(secs => @agoSeconds)) RETURNING id
            """,
            new { agencyId, postId, platform, status, scheduledAt, agoSeconds = (updatedAgo ?? TimeSpan.Zero).TotalSeconds });
    }

    private async Task<List<(Guid AgencyId, Guid PostId, Guid PublicationId)>> ClaimAsync(int limit = 10, int staleMinutes = 10)
    {
        await using var app = await _db.OpenAppAsync();
        return (await app.QueryAsync<(Guid, Guid, Guid)>(
            "SELECT agency_id, post_id, publication_id FROM content_claim_due_publications(@limit, @staleMinutes)", new { limit, staleMinutes })).ToList();
    }

    // --- Permissions -----------------------------------------------------------------------------

    [Fact]
    public async Task TheTwoPermissions_AreInTheCatalog_NotProtected_AndSeededForEverySlotLikeTheOracleSays()
    {
        await using var owner = await _db.OpenOwnerAsync();

        var catalog = (await owner.QueryAsync<(string Key, bool IsProtected)>(
            "SELECT key, is_protected FROM permissions WHERE key IN ('contentstudio.create', 'contentstudio.publish')")).ToList();
        var grants = (await owner.QueryAsync<(string Model, string Role, string Key, bool Granted)>(
            "SELECT business_model, role, permission_key, is_granted FROM role_permissions WHERE permission_key IN ('contentstudio.create', 'contentstudio.publish')")).ToList();

        Assert.Equal(2, catalog.Count);
        Assert.All(catalog, c => Assert.False(c.IsProtected));
        Assert.Equal(16, grants.Count); // 8 slots by 2 keys
        foreach (var grant in grants)
        {
            BusinessModelRules.TryParse(grant.Model, out var model);
            RolePermissions.TryParse(grant.Role, out var role);
            Assert.True(role.GrantedByDefault(model, grant.Key) == grant.Granted, $"{grant.Model}/{grant.Role} {grant.Key}");
        }
    }

    [Fact]
    public async Task OnlyACreatorsContentWriter_HoldsPublishAmongTheContentWriters()
    {
        await using var owner = await _db.OpenOwnerAsync();

        var publishers = (await owner.QueryAsync<string>(
            "SELECT business_model || '/' || role FROM role_permissions WHERE permission_key = 'contentstudio.publish' AND is_granted ORDER BY 1")).ToList();

        Assert.Equal(
            ["agency/marketingmanager", "agency/organizationadmin", "company/marketingmanager", "company/organizationadmin", "creator/contentwriter"],
            publishers);
    }

    [Fact]
    public async Task RunningTheScriptAgain_DoesNotUndoWhatTheSiteAdminSet()
    {
        await using var owner = await _db.OpenOwnerAsync();
        await owner.ExecuteAsync(
            "UPDATE role_permissions SET is_granted = false WHERE business_model = 'creator' AND role = 'contentwriter' AND permission_key = 'contentstudio.publish'");

        await _db.MigrateAsync();

        Assert.False(await owner.ExecuteScalarAsync<bool>(
            "SELECT is_granted FROM role_permissions WHERE business_model = 'creator' AND role = 'contentwriter' AND permission_key = 'contentstudio.publish'"));
    }

    // --- The document -----------------------------------------------------------------------------

    [Fact]
    public async Task ADocument_MustBeAnObject_AndOfAtMostAMegabyte()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var post = await PostAsync(_agencyA, _workspaceA);

        var array = await Assert.ThrowsAsync<PostgresException>(() => owner.ExecuteAsync("UPDATE content_posts SET body_doc = '[]' WHERE id = @post", new { post }));
        var large = await Assert.ThrowsAsync<PostgresException>(() => owner.ExecuteAsync(
            "UPDATE content_posts SET body_doc = jsonb_build_object('t', repeat('a', 1000001)) WHERE id = @post", new { post }));

        Assert.Equal(PostgresErrorCodes.CheckViolation, array.SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, large.SqlState);
        await owner.ExecuteAsync("""UPDATE content_posts SET body_doc = '{"type":"doc"}' WHERE id = @post""", new { post });
    }

    [Fact]
    public async Task AChangeToTheDocumentAlone_AppendsARevision_AndThereIsExactlyOneRevisionTrigger()
    {
        await using var app = await _db.OpenAppAsync(_agencyA, _user);
        var post = await PostAsync(_agencyA, _workspaceA, bodyDoc: """{"type":"doc"}""");
        Assert.Equal(1, await app.QuerySingleAsync<long>("SELECT count(*) FROM content_revisions WHERE post_id = @post", new { post }));

        await app.ExecuteAsync("""UPDATE content_posts SET body_doc = '{"type":"doc","content":[]}', updated_by = @user WHERE id = @post""", new { post, user = _user });
        await app.ExecuteAsync("UPDATE content_posts SET status = 'archived', archived_at = now() WHERE id = @post", new { post });

        Assert.Equal(2, await app.QuerySingleAsync<long>("SELECT count(*) FROM content_revisions WHERE post_id = @post", new { post }));
        Assert.Equal(2, await app.QuerySingleAsync<long>("SELECT count(*) FROM content_revisions WHERE post_id = @post AND jsonb_exists(snapshot, 'body_doc')", new { post }));
        await using var owner = await _db.OpenOwnerAsync();
        Assert.Equal(["trg_content_posts_revision_v2", "trg_content_posts_updated_at"], (await owner.QueryAsync<string>(
            "SELECT tgname FROM pg_trigger WHERE tgrelid = 'content_posts'::regclass AND NOT tgisinternal ORDER BY tgname")).ToList());
    }

    // --- Targets without an account -----------------------------------------------------------------

    [Fact]
    public async Task ATarget_NeedsNoAccountYet_ButOnlyOnePerPlatformPerPost()
    {
        var post = await PostAsync(_agencyA, _workspaceA);
        await TargetAsync(_agencyA, post, "instagram", "pending", null);
        await TargetAsync(_agencyA, post, "linkedin", "pending", null);

        var twice = await Assert.ThrowsAsync<PostgresException>(() => TargetAsync(_agencyA, post, "instagram", "pending", null));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, twice.SqlState);
    }

    // --- Claiming what is due --------------------------------------------------------------------------

    [Fact]
    public async Task OnlyADueTargetOfAScheduledOrPublishingPost_IsClaimed_AndMarkedPublishing()
    {
        var scheduled = await PostAsync(_agencyA, _workspaceA, "scheduled", DateTimeOffset.UtcNow.AddHours(1));
        var due = await TargetAsync(_agencyA, scheduled, "instagram", "scheduled", DateTimeOffset.UtcNow.AddMinutes(-5));
        var later = await TargetAsync(_agencyA, scheduled, "linkedin", "scheduled", DateTimeOffset.UtcNow.AddHours(1));
        var draft = await PostAsync(_agencyA, _workspaceA);
        await TargetAsync(_agencyA, draft, "instagram", "scheduled", DateTimeOffset.UtcNow.AddMinutes(-5));
        var archived = await PostAsync(_agencyB, _workspaceB, "archived");
        await TargetAsync(_agencyB, archived, "instagram", "scheduled", DateTimeOffset.UtcNow.AddMinutes(-5));
        var otherOrganization = await PostAsync(_agencyB, _workspaceB, "publishing", DateTimeOffset.UtcNow.AddMinutes(-1));
        var theirs = await TargetAsync(_agencyB, otherOrganization, "instagram", "scheduled", DateTimeOffset.UtcNow.AddMinutes(-1));

        var claimed = await ClaimAsync();

        Assert.Equal(new[] { due, theirs }.Order(), claimed.Select(c => c.PublicationId).Order());
        Assert.Contains((_agencyA, scheduled, due), claimed);
        Assert.Contains((_agencyB, otherOrganization, theirs), claimed);
        await using var owner = await _db.OpenOwnerAsync();
        Assert.Equal("publishing", await owner.ExecuteScalarAsync<string>("SELECT publish_status FROM content_post_platforms WHERE id = @due", new { due }));
        Assert.Equal("scheduled", await owner.ExecuteScalarAsync<string>("SELECT publish_status FROM content_post_platforms WHERE id = @later", new { later }));
        Assert.Empty(await ClaimAsync());
    }

    [Fact]
    public async Task TheClaim_HonoursTheLimit_OldestFirst()
    {
        var post = await PostAsync(_agencyA, _workspaceA, "scheduled", DateTimeOffset.UtcNow.AddHours(1));
        var oldest = await TargetAsync(_agencyA, post, "instagram", "scheduled", DateTimeOffset.UtcNow.AddMinutes(-30));
        await TargetAsync(_agencyA, post, "linkedin", "scheduled", DateTimeOffset.UtcNow.AddMinutes(-10));
        await TargetAsync(_agencyA, post, "x", "scheduled", DateTimeOffset.UtcNow.AddMinutes(-5));

        var first = await ClaimAsync(limit: 1);

        Assert.Equal([oldest], first.Select(c => c.PublicationId));
        Assert.Equal(2, (await ClaimAsync(limit: 10)).Count);
    }

    [Fact]
    public async Task ATargetLeftPublishingByAProcessThatDied_IsClaimedAgainOnlyAfterTheStaleTime()
    {
        var post = await PostAsync(_agencyA, _workspaceA, "publishing", DateTimeOffset.UtcNow.AddHours(-1));
        var recent = await TargetAsync(_agencyA, post, "instagram", "publishing", DateTimeOffset.UtcNow.AddMinutes(-1), TimeSpan.FromMinutes(2));
        var stale = await TargetAsync(_agencyA, post, "linkedin", "publishing", DateTimeOffset.UtcNow.AddMinutes(-30), TimeSpan.FromMinutes(30));

        var claimed = await ClaimAsync(staleMinutes: 10);

        Assert.Equal([stale], claimed.Select(c => c.PublicationId));
        Assert.DoesNotContain(recent, claimed.Select(c => c.PublicationId));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(101, 10)]
    [InlineData(10, 0)]
    [InlineData(10, 1441)]
    public async Task TheClaim_RefusesALimitOrStaleTimeOutsideItsRange(int limit, int staleMinutes)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(() => ClaimAsync(limit, staleMinutes));

        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, failure.SqlState);
    }

    [Fact]
    public async Task TheClaim_IsForTheApplicationRoleOnly_AndReturnsIdsAndNothingElse()
    {
        await using var owner = await _db.OpenOwnerAsync();

        Assert.False(await owner.ExecuteScalarAsync<bool>("SELECT has_function_privilege('public', 'content_claim_due_publications(integer, integer)', 'EXECUTE')"));
        Assert.True(await owner.ExecuteScalarAsync<bool>("SELECT has_function_privilege('app_user', 'content_claim_due_publications(integer, integer)', 'EXECUTE')"));
        Assert.Equal(["agency_id", "post_id", "publication_id"], (await owner.QueryAsync<string>(
            "SELECT unnest(proargnames[array_length(proargtypes::oid[], 1) + 1:]) FROM pg_proc WHERE proname = 'content_claim_due_publications'")).ToList());
    }
}
