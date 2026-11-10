using Dapper;
using Npgsql;
using Platform.Auth.Services;
using Platform.Helpers.Constants;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>
/// <c>016_media_library.sql</c>: the ten tables, row-level security on each, the composite keys that keep one organization's rows from
/// pointing at another's, the rules of a file, a folder and a tag, what a file in use means, and the two permissions with their defaults.
/// Rows are arranged as the database owner (a superuser in the test server); everything about isolation runs as app_user.
/// (Written to the same checks as the script that was run by hand against the development database: 67 of them, all passed.)
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Database")]
public sealed class MediaLibraryMigrationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly string[] Tables =
    [
        "media_folders", "media_assets", "media_tags", "media_asset_tags", "media_folder_assets", "media_collections",
        "media_collection_assets", "media_asset_variants", "media_asset_usages", "media_upload_sessions"
    ];

    private TestDatabase _db = null!;
    private Guid _agencyA, _agencyB, _user, _workspaceA, _workspaceA2, _workspaceB;

    public async Task InitializeAsync()
    {
        _db = await postgres.CreateDatabaseAsync();
        await using var owner = await _db.OpenOwnerAsync();
        _agencyA = await DbTestSupport.InsertAgencyAsync(owner, "Agency A", "company");
        _agencyB = await DbTestSupport.InsertAgencyAsync(owner, "Agency B", "company");
        _user = await DbTestSupport.InsertUserAsync(owner, "uploader@example.com");
        _workspaceA = await DbTestSupport.InsertWorkspaceAsync(owner, _agencyA, "A one");
        _workspaceA2 = await DbTestSupport.InsertWorkspaceAsync(owner, _agencyA, "A two");
        _workspaceB = await DbTestSupport.InsertWorkspaceAsync(owner, _agencyB, "B one");
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static string KeyOf(Guid agency, Guid asset, string extension = "png") => $"{agency:N}/{asset:N}.{extension}";

    private async Task<Guid> AssetAsync(
        Guid agencyId, Guid workspaceId, string name = "a.png", string mediaType = "image", string? key = null, string mime = "image/png", long size = 1000)
    {
        var id = Guid.NewGuid();
        await using var owner = await _db.OpenOwnerAsync();
        await owner.ExecuteAsync(
            """
            INSERT INTO media_assets (id, agency_id, workspace_id, uploaded_by, file_name, mime_type, media_type, file_size_bytes, storage_key)
            VALUES (@id, @agencyId, @workspaceId, @user, @name, @mime, @mediaType, @size, @key)
            """,
            new { id, agencyId, workspaceId, user = _user, name, mime, mediaType, size, key = key ?? KeyOf(agencyId, id) });
        return id;
    }

    private async Task<Guid> FolderAsync(Guid agencyId, Guid workspaceId, string name = "Beach", Guid? parent = null)
    {
        await using var owner = await _db.OpenOwnerAsync();
        return await owner.ExecuteScalarAsync<Guid>(
            "INSERT INTO media_folders (agency_id, workspace_id, name, parent_folder_id) VALUES (@agencyId, @workspaceId, @name, @parent) RETURNING id",
            new { agencyId, workspaceId, name, parent });
    }

    private async Task<Guid> TagAsync(Guid agencyId, Guid workspaceId, string name = "Summer")
    {
        await using var owner = await _db.OpenOwnerAsync();
        return await owner.ExecuteScalarAsync<Guid>(
            "INSERT INTO media_tags (agency_id, workspace_id, name) VALUES (@agencyId, @workspaceId, @name) RETURNING id", new { agencyId, workspaceId, name });
    }

    private async Task AssertRefusedAsync(string sql, string sqlState, object? parameters = null)
    {
        await using var owner = await _db.OpenOwnerAsync();
        var failure = await Assert.ThrowsAsync<PostgresException>(() => owner.ExecuteAsync(sql, parameters));
        Assert.Equal(sqlState, failure.SqlState);
    }

    // --- The tables and who sees them ---------------------------------------------------------------

    [Fact]
    public async Task EveryTable_HasRowLevelSecurityEnabledAndForced_AndTheTenantPolicy()
    {
        await using var owner = await _db.OpenOwnerAsync();

        foreach (var table in Tables)
        {
            var flags = await owner.QuerySingleAsync<(bool Enabled, bool Forced)>("SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE relname = @table", new { table });
            Assert.True(flags.Enabled && flags.Forced, table);
            Assert.Equal(1, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_policies WHERE tablename = @table AND policyname = 'tenant_isolation'", new { table }));
            Assert.True(await owner.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = @table AND column_name = 'agency_id' AND is_nullable = 'NO')", new { table }), table);
        }
    }

    [Fact]
    public async Task AnOrganization_SeesOnlyItsOwnRows_InEveryTable()
    {
        var mine = await AssetAsync(_agencyA, _workspaceA);
        var theirs = await AssetAsync(_agencyB, _workspaceB);
        await FolderAsync(_agencyA, _workspaceA);
        await FolderAsync(_agencyB, _workspaceB);
        await TagAsync(_agencyA, _workspaceA);
        await TagAsync(_agencyB, _workspaceB);

        await using var app = await _db.OpenAppAsync(_agencyA, _user);
        Assert.Equal([mine], (await app.QueryAsync<Guid>("SELECT id FROM media_assets")).ToList());
        Assert.Equal(0, await app.ExecuteScalarAsync<long>("SELECT count(*) FROM media_assets WHERE id = @theirs", new { theirs }));
        Assert.Equal(1, await app.ExecuteScalarAsync<long>("SELECT count(*) FROM media_folders"));
        Assert.Equal(1, await app.ExecuteScalarAsync<long>("SELECT count(*) FROM media_tags"));
        foreach (var table in Tables)
        {
            Assert.Equal(0, await app.ExecuteScalarAsync<long>($"SELECT count(*) FROM {table} WHERE agency_id = @agency", new { agency = _agencyB }));
        }
    }

    [Fact]
    public async Task AWriteForAnotherOrganization_IsRefused_AndAnUpdateOfItsRowChangesNothing()
    {
        var theirs = await AssetAsync(_agencyB, _workspaceB, name: "b.png");
        await using var app = await _db.OpenAppAsync(_agencyA, _user);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => app.ExecuteAsync(
            """
            INSERT INTO media_assets (agency_id, workspace_id, file_name, mime_type, media_type, file_size_bytes, storage_key)
            VALUES (@agency, @workspace, 'x.png', 'image/png', 'image', 5, 'zz/x.png')
            """,
            new { agency = _agencyB, workspace = _workspaceB }));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, failure.SqlState);

        Assert.Equal(0, await app.ExecuteAsync("UPDATE media_assets SET file_name = 'hacked' WHERE id = @theirs", new { theirs }));
        Assert.Equal(0, await app.ExecuteAsync("DELETE FROM media_assets WHERE id = @theirs", new { theirs }));
        await using var owner = await _db.OpenOwnerAsync();
        Assert.Equal("b.png", await owner.ExecuteScalarAsync<string>("SELECT file_name FROM media_assets WHERE id = @theirs", new { theirs }));
    }

    [Fact]
    public async Task WithNoOrganizationSet_NothingIsVisible()
    {
        await AssetAsync(_agencyA, _workspaceA);

        await using var app = await _db.OpenAppAsync(null, null);

        Assert.Equal(0, await app.ExecuteScalarAsync<long>("SELECT count(*) FROM media_assets"));
    }

    // --- The rules of a file ----------------------------------------------------------------------

    [Theory]
    [InlineData("media_type = 'audio'")]
    [InlineData("file_size_bytes = 0")]
    [InlineData("file_size_bytes = 2147483649")]
    [InlineData("storage_key = 'a/../b.png'")]
    [InlineData("storage_key = '/abs.png'")]
    [InlineData("storage_key = 'has space.png'")]
    [InlineData("file_name = E'a\\nb'")]
    [InlineData("file_name = ''")]
    [InlineData("mime_type = 'IMAGE/PNG'")]
    [InlineData("mime_type = 'image'")]
    [InlineData("metadata = '[]'")]
    public async Task AFileThatBreaksARule_IsRefused(string change)
    {
        var id = await AssetAsync(_agencyA, _workspaceA);
        await AssertRefusedAsync($"UPDATE media_assets SET {change} WHERE id = @id", PostgresErrorCodes.CheckViolation, new { id });
    }

    [Fact]
    public async Task TheStorageKey_IsUnique()
    {
        var first = await AssetAsync(_agencyA, _workspaceA);
        await AssertRefusedAsync(
            """
            INSERT INTO media_assets (agency_id, workspace_id, file_name, mime_type, media_type, file_size_bytes, storage_key)
            SELECT agency_id, workspace_id, 'copy.png', mime_type, media_type, file_size_bytes, storage_key FROM media_assets WHERE id = @first
            """,
            PostgresErrorCodes.UniqueViolation, new { first });
    }

    [Fact]
    public async Task TheUploaderBeingDeleted_KeepsTheFile()
    {
        var id = await AssetAsync(_agencyA, _workspaceA);
        await using var owner = await _db.OpenOwnerAsync();

        await owner.ExecuteAsync("DELETE FROM users WHERE id = @user", new { user = _user });

        Assert.Null(await owner.ExecuteScalarAsync<Guid?>("SELECT uploaded_by FROM media_assets WHERE id = @id", new { id }));
    }

    // --- Folders and tags ---------------------------------------------------------------------------

    [Fact]
    public async Task AFolderName_IsUniqueInAWorkspace_InAnyLetterCase_ButNotAcrossWorkspacesOrParents()
    {
        var beach = await FolderAsync(_agencyA, _workspaceA, "Beach");

        await AssertRefusedAsync("INSERT INTO media_folders (agency_id, workspace_id, name) VALUES (@a, @w, 'beach')", PostgresErrorCodes.UniqueViolation, new { a = _agencyA, w = _workspaceA });
        await FolderAsync(_agencyA, _workspaceA2, "Beach");
        await FolderAsync(_agencyA, _workspaceA, "Beach", parent: beach);
    }

    [Fact]
    public async Task AFolderNeedsAProperName_AndCannotBeItsOwnParent_OrHaveAParentOfAnotherOrganization()
    {
        var folder = await FolderAsync(_agencyA, _workspaceA);
        var other = await FolderAsync(_agencyB, _workspaceB);

        await AssertRefusedAsync("INSERT INTO media_folders (agency_id, workspace_id, name) VALUES (@a, @w, ' padded ')", PostgresErrorCodes.CheckViolation, new { a = _agencyA, w = _workspaceA });
        await AssertRefusedAsync("UPDATE media_folders SET parent_folder_id = id WHERE id = @folder", PostgresErrorCodes.CheckViolation, new { folder });
        await AssertRefusedAsync("INSERT INTO media_folders (agency_id, workspace_id, name, parent_folder_id) VALUES (@a, @w, 'Child', @other)", PostgresErrorCodes.ForeignKeyViolation, new { a = _agencyA, w = _workspaceA, other });
    }

    [Fact]
    public async Task ADeletedFolder_TakesItsSubFoldersAndMemberships_AndLeavesTheFiles()
    {
        var asset = await AssetAsync(_agencyA, _workspaceA);
        var parent = await FolderAsync(_agencyA, _workspaceA, "Parent");
        var child = await FolderAsync(_agencyA, _workspaceA, "Child", parent);
        await using var owner = await _db.OpenOwnerAsync();
        await owner.ExecuteAsync("INSERT INTO media_folder_assets (agency_id, folder_id, media_asset_id) VALUES (@a, @parent, @asset), (@a, @child, @asset)", new { a = _agencyA, parent, child, asset });

        await owner.ExecuteAsync("DELETE FROM media_folders WHERE id = @parent", new { parent });

        Assert.Equal(0, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM media_folders WHERE id IN (@parent, @child)", new { parent, child }));
        Assert.Equal(0, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM media_folder_assets"));
        Assert.Equal(1, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM media_assets WHERE id = @asset", new { asset }));
    }

    [Fact]
    public async Task AFileCanBeInSeveralFolders_ButOnceInEach()
    {
        var asset = await AssetAsync(_agencyA, _workspaceA);
        var one = await FolderAsync(_agencyA, _workspaceA, "One");
        var two = await FolderAsync(_agencyA, _workspaceA, "Two");
        await using var owner = await _db.OpenOwnerAsync();
        await owner.ExecuteAsync("INSERT INTO media_folder_assets (agency_id, folder_id, media_asset_id) VALUES (@a, @one, @asset), (@a, @two, @asset)", new { a = _agencyA, one, two, asset });

        await AssertRefusedAsync("INSERT INTO media_folder_assets (agency_id, folder_id, media_asset_id) VALUES (@a, @one, @asset)", PostgresErrorCodes.UniqueViolation, new { a = _agencyA, one, asset });
    }

    [Fact]
    public async Task ARowOfOneOrganization_CannotPointAtAFileFolderOrTagOfAnother()
    {
        var folder = await FolderAsync(_agencyA, _workspaceA);
        var theirAsset = await AssetAsync(_agencyB, _workspaceB);
        var myAsset = await AssetAsync(_agencyA, _workspaceA);
        var theirTag = await TagAsync(_agencyB, _workspaceB);

        await AssertRefusedAsync("INSERT INTO media_folder_assets (agency_id, folder_id, media_asset_id) VALUES (@a, @folder, @theirAsset)", PostgresErrorCodes.ForeignKeyViolation, new { a = _agencyA, folder, theirAsset });
        await AssertRefusedAsync("INSERT INTO media_asset_tags (agency_id, media_asset_id, tag_id) VALUES (@a, @myAsset, @theirTag)", PostgresErrorCodes.ForeignKeyViolation, new { a = _agencyA, myAsset, theirTag });
        await AssertRefusedAsync(
            "INSERT INTO media_asset_usages (agency_id, workspace_id, media_asset_id, module_name, entity_type, entity_id) VALUES (@a, @w, @theirAsset, 'content_studio', 'post', gen_random_uuid())",
            PostgresErrorCodes.ForeignKeyViolation, new { a = _agencyA, w = _workspaceA, theirAsset });
    }

    [Fact]
    public async Task ATagName_IsUniqueInAWorkspace_InAnyLetterCase()
    {
        await TagAsync(_agencyA, _workspaceA, "Summer");

        await AssertRefusedAsync("INSERT INTO media_tags (agency_id, workspace_id, name) VALUES (@a, @w, 'SUMMER')", PostgresErrorCodes.UniqueViolation, new { a = _agencyA, w = _workspaceA });
        await TagAsync(_agencyA, _workspaceA2, "Summer");
    }

    // --- A file that is in use -------------------------------------------------------------------------

    [Fact]
    public async Task AFileWithAUsage_CannotBeDeleted_UntilTheUsageIsGone()
    {
        var asset = await AssetAsync(_agencyA, _workspaceA);
        await using var owner = await _db.OpenOwnerAsync();
        await owner.ExecuteAsync(
            "INSERT INTO media_asset_usages (agency_id, workspace_id, media_asset_id, module_name, entity_type, entity_id) VALUES (@a, @w, @asset, 'content_studio', 'post', gen_random_uuid())",
            new { a = _agencyA, w = _workspaceA, asset });

        await AssertRefusedAsync("DELETE FROM media_assets WHERE id = @asset", PostgresErrorCodes.ForeignKeyViolation, new { asset });

        await owner.ExecuteAsync("DELETE FROM media_asset_usages WHERE media_asset_id = @asset", new { asset });
        Assert.Equal(1, await owner.ExecuteAsync("DELETE FROM media_assets WHERE id = @asset", new { asset }));
    }

    [Fact]
    public async Task APostThatUsesAFile_BlocksItsDelete_AndCannotUseAFileOfAnotherOrganizationOrOneThatIsNotThere()
    {
        var mine = await AssetAsync(_agencyA, _workspaceA);
        var theirs = await AssetAsync(_agencyB, _workspaceB);
        await using var owner = await _db.OpenOwnerAsync();
        var post = await owner.ExecuteScalarAsync<Guid>("INSERT INTO content_posts (agency_id, workspace_id, title, content_type) VALUES (@a, @w, 'P', 'social_post') RETURNING id", new { a = _agencyA, w = _workspaceA });
        await owner.ExecuteAsync("INSERT INTO content_post_media (agency_id, post_id, media_asset_id) VALUES (@a, @post, @mine)", new { a = _agencyA, post, mine });

        await AssertRefusedAsync("DELETE FROM media_assets WHERE id = @mine", PostgresErrorCodes.ForeignKeyViolation, new { mine });
        await AssertRefusedAsync("INSERT INTO content_post_media (agency_id, post_id, media_asset_id, media_role) VALUES (@a, @post, @theirs, 'cover')", PostgresErrorCodes.ForeignKeyViolation, new { a = _agencyA, post, theirs });
        await AssertRefusedAsync("INSERT INTO content_post_media (agency_id, post_id, media_asset_id, media_role) VALUES (@a, @post, gen_random_uuid(), 'cover')", PostgresErrorCodes.ForeignKeyViolation, new { a = _agencyA, post });

        await owner.ExecuteAsync("DELETE FROM content_posts WHERE id = @post", new { post });   // the link goes with the post
        Assert.Equal(1, await owner.ExecuteAsync("DELETE FROM media_assets WHERE id = @mine", new { mine }));
    }

    [Fact]
    public async Task DeletingAFile_ClearsWhatOnlyPointedAtIt_AndKeepsTheOrganizationOfThoseRows()
    {
        var preview = await AssetAsync(_agencyA, _workspaceA, "preview.png");
        var cover = await AssetAsync(_agencyA, _workspaceA, "cover.png");
        var output = await AssetAsync(_agencyA, _workspaceA, "output.png");
        await using var owner = await _db.OpenOwnerAsync();
        var template = await owner.ExecuteScalarAsync<Guid>(
            "INSERT INTO content_templates (agency_id, workspace_id, name, content_type, preview_asset_id) VALUES (@a, @w, 'T', 'social_post', @preview) RETURNING id", new { a = _agencyA, w = _workspaceA, preview });
        var collection = await owner.ExecuteScalarAsync<Guid>(
            "INSERT INTO media_collections (agency_id, workspace_id, name, cover_asset_id) VALUES (@a, @w, 'Col', @cover) RETURNING id", new { a = _agencyA, w = _workspaceA, cover });
        var session = await owner.ExecuteScalarAsync<Guid>(
            "INSERT INTO media_upload_sessions (agency_id, workspace_id, file_name, status, completed_asset_id) VALUES (@a, @w, 'output.png', 'completed', @output) RETURNING id", new { a = _agencyA, w = _workspaceA, output });

        await owner.ExecuteAsync("DELETE FROM media_assets WHERE id IN (@preview, @cover, @output)", new { preview, cover, output });

        Assert.Null(await owner.ExecuteScalarAsync<Guid?>("SELECT preview_asset_id FROM content_templates WHERE id = @template", new { template }));
        Assert.Equal(_agencyA, await owner.ExecuteScalarAsync<Guid>("SELECT agency_id FROM content_templates WHERE id = @template", new { template }));
        Assert.Null(await owner.ExecuteScalarAsync<Guid?>("SELECT cover_asset_id FROM media_collections WHERE id = @collection", new { collection }));
        Assert.Equal(_agencyA, await owner.ExecuteScalarAsync<Guid>("SELECT agency_id FROM media_collections WHERE id = @collection", new { collection }));
        Assert.Null(await owner.ExecuteScalarAsync<Guid?>("SELECT completed_asset_id FROM media_upload_sessions WHERE id = @session", new { session }));
    }

    [Fact]
    public async Task DeletingAFile_TakesItsTagsMembershipsVariantsAndCollectionPlace()
    {
        var asset = await AssetAsync(_agencyA, _workspaceA);
        var folder = await FolderAsync(_agencyA, _workspaceA);
        var tag = await TagAsync(_agencyA, _workspaceA);
        await using var owner = await _db.OpenOwnerAsync();
        var collection = await owner.ExecuteScalarAsync<Guid>("INSERT INTO media_collections (agency_id, workspace_id, name) VALUES (@a, @w, 'Col') RETURNING id", new { a = _agencyA, w = _workspaceA });
        await owner.ExecuteAsync("INSERT INTO media_asset_tags (agency_id, media_asset_id, tag_id) VALUES (@a, @asset, @tag)", new { a = _agencyA, asset, tag });
        await owner.ExecuteAsync("INSERT INTO media_folder_assets (agency_id, folder_id, media_asset_id) VALUES (@a, @folder, @asset)", new { a = _agencyA, folder, asset });
        await owner.ExecuteAsync("INSERT INTO media_collection_assets (agency_id, collection_id, media_asset_id) VALUES (@a, @collection, @asset)", new { a = _agencyA, collection, asset });
        await owner.ExecuteAsync("INSERT INTO media_asset_variants (agency_id, media_asset_id, variant_type, storage_key) VALUES (@a, @asset, 'thumbnail', 'k/thumb.png')", new { a = _agencyA, asset });

        await owner.ExecuteAsync("DELETE FROM media_assets WHERE id = @asset", new { asset });

        foreach (var table in new[] { "media_asset_tags", "media_folder_assets", "media_collection_assets", "media_asset_variants" })
        {
            Assert.Equal(0, await DbTestSupport.CountAsync(owner, table));
        }

        Assert.Equal(1, await DbTestSupport.CountAsync(owner, "media_folders"));
        Assert.Equal(1, await DbTestSupport.CountAsync(owner, "media_tags"));
    }

    [Fact]
    public async Task VariantsCollectionsAndUploadSessions_HaveTheirRules()
    {
        var asset = await AssetAsync(_agencyA, _workspaceA);
        await using var owner = await _db.OpenOwnerAsync();
        await owner.ExecuteAsync("INSERT INTO media_asset_variants (agency_id, media_asset_id, variant_type, storage_key) VALUES (@a, @asset, 'thumbnail', 'k/t.png')", new { a = _agencyA, asset });
        var collection = await owner.ExecuteScalarAsync<Guid>("INSERT INTO media_collections (agency_id, workspace_id, name) VALUES (@a, @w, 'Col') RETURNING id", new { a = _agencyA, w = _workspaceA });
        await owner.ExecuteAsync("INSERT INTO media_collection_assets (agency_id, collection_id, media_asset_id) VALUES (@a, @collection, @asset)", new { a = _agencyA, collection, asset });

        await AssertRefusedAsync("INSERT INTO media_asset_variants (agency_id, media_asset_id, variant_type, storage_key) VALUES (@a, @asset, 'huge', 'k/h.png')", PostgresErrorCodes.CheckViolation, new { a = _agencyA, asset });
        await AssertRefusedAsync("INSERT INTO media_asset_variants (agency_id, media_asset_id, variant_type, storage_key) VALUES (@a, @asset, 'thumbnail', 'k/u.png')", PostgresErrorCodes.UniqueViolation, new { a = _agencyA, asset });
        await AssertRefusedAsync("UPDATE media_collection_assets SET display_order = -1 WHERE collection_id = @collection", PostgresErrorCodes.CheckViolation, new { collection });
        await AssertRefusedAsync("INSERT INTO media_upload_sessions (agency_id, workspace_id, file_name, status, completed_asset_id) VALUES (@a, @w, 'x.png', 'pending', @asset)", PostgresErrorCodes.CheckViolation, new { a = _agencyA, w = _workspaceA, asset });
        await AssertRefusedAsync("INSERT INTO media_upload_sessions (agency_id, workspace_id, file_name, status) VALUES (@a, @w, 'x.png', 'weird')", PostgresErrorCodes.CheckViolation, new { a = _agencyA, w = _workspaceA });
    }

    [Fact]
    public async Task DeletingAnOrganization_TakesItsFiles_EvenThoseThatAreInUse()
    {
        var asset = await AssetAsync(_agencyA, _workspaceA);
        await using var owner = await _db.OpenOwnerAsync();
        await owner.ExecuteAsync(
            "INSERT INTO media_asset_usages (agency_id, workspace_id, media_asset_id, module_name, entity_type, entity_id) VALUES (@a, @w, @asset, 'content_studio', 'post', gen_random_uuid())",
            new { a = _agencyA, w = _workspaceA, asset });

        await owner.ExecuteAsync("DELETE FROM agencies WHERE id = @a", new { a = _agencyA });

        Assert.Equal(0, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM media_assets WHERE agency_id = @a", new { a = _agencyA }));
    }

    // --- The permissions --------------------------------------------------------------------------------

    [Fact]
    public async Task TheTwoPermissions_AreInTheCatalog_NotProtected_AndSeededForEverySlotLikeTheOracleSays()
    {
        await using var owner = await _db.OpenOwnerAsync();

        var catalog = (await owner.QueryAsync<(string Key, bool IsProtected)>("SELECT key, is_protected FROM permissions WHERE key IN ('medialibrary.create', 'medialibrary.delete')")).ToList();
        var grants = (await owner.QueryAsync<(string Model, string Role, string Key, bool Granted)>(
            "SELECT business_model, role, permission_key, is_granted FROM role_permissions WHERE permission_key IN ('medialibrary.create', 'medialibrary.delete')")).ToList();

        Assert.Equal(2, catalog.Count);
        Assert.All(catalog, entry => Assert.False(entry.IsProtected));
        Assert.Equal(16, grants.Count); // 8 slots by 2 keys
        foreach (var grant in grants)
        {
            BusinessModelRules.TryParse(grant.Model, out var model);
            RolePermissions.TryParse(grant.Role, out var role);
            Assert.True(role.GrantedByDefault(model, grant.Key) == grant.Granted, $"{grant.Model}/{grant.Role} {grant.Key}");
        }
    }

    [Fact]
    public async Task OnlyACreatorsContentWriter_CanDeleteAmongTheContentWriters()
    {
        await using var owner = await _db.OpenOwnerAsync();

        var deleters = (await owner.QueryAsync<string>(
            "SELECT business_model || '/' || role FROM role_permissions WHERE permission_key = 'medialibrary.delete' AND is_granted ORDER BY 1")).ToList();

        Assert.Equal(
            ["agency/marketingmanager", "agency/organizationadmin", "company/marketingmanager", "company/organizationadmin", "creator/contentwriter"],
            deleters);
    }

    [Fact]
    public async Task RunningTheScriptAgain_DoesNotUndoWhatTheSiteAdminSet_AndAddsNothing()
    {
        await using var owner = await _db.OpenOwnerAsync();
        await owner.ExecuteAsync("UPDATE role_permissions SET is_granted = false WHERE business_model = 'company' AND role = 'contentwriter' AND permission_key = 'medialibrary.create'");
        var tablesBefore = await DbTestSupport.ScalarAsync<long>(owner, "SELECT count(*) FROM pg_class WHERE relkind = 'r' AND relname LIKE 'media\\_%'");

        await _db.MigrateAsync();

        Assert.False(await owner.ExecuteScalarAsync<bool>("SELECT is_granted FROM role_permissions WHERE business_model = 'company' AND role = 'contentwriter' AND permission_key = 'medialibrary.create'"));
        Assert.Equal(tablesBefore, await DbTestSupport.ScalarAsync<long>(owner, "SELECT count(*) FROM pg_class WHERE relkind = 'r' AND relname LIKE 'media\\_%'"));
        Assert.Equal(10, tablesBefore);
    }
}
