using Dapper;
using Npgsql;
using Platform.Media.Repositories;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>
/// <see cref="MediaUsageRepository"/> against a real server, as app_user: the usages of one thing are made exactly the files it names,
/// the workspace comes from the file, another organization's file is ignored, another thing's usages are left alone, and a file that
/// something uses cannot be deleted until the usage is gone. Written but not run here (it needs <c>PLATFORM_TEST_POSTGRES</c>).
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Database")]
public sealed class MediaUsageRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Module = "content_studio";
    private const string Kind = "post";

    private TestDatabase _db = null!;
    private Guid _agencyA, _agencyB, _user, _workspaceA, _workspaceA2, _workspaceB;
    private MediaUsageRepository _repository = null!;

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
        _repository = new MediaUsageRepository(_db.CreateFactory());
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<Guid> AssetAsync(Guid agencyId, Guid workspaceId)
    {
        var id = Guid.NewGuid();
        await using var owner = await _db.OpenOwnerAsync();
        await owner.ExecuteAsync(
            """
            INSERT INTO media_assets (id, agency_id, workspace_id, uploaded_by, file_name, mime_type, media_type, file_size_bytes, storage_key)
            VALUES (@id, @agencyId, @workspaceId, @user, 'a.png', 'image/png', 'image', 1000, @key)
            """,
            new { id, agencyId, workspaceId, user = _user, key = $"{agencyId:N}/{id:N}.png" });
        return id;
    }

    private async Task<List<(Guid Asset, Guid Workspace, Guid Entity)>> UsagesAsync(Guid agencyId)
    {
        await using var owner = await _db.OpenOwnerAsync();
        return (await owner.QueryAsync<(Guid Asset, Guid Workspace, Guid Entity)>(
            "SELECT media_asset_id, workspace_id, entity_id FROM media_asset_usages WHERE agency_id = @agencyId ORDER BY created_at, media_asset_id",
            new { agencyId })).AsList();
    }

    private Task ReplaceAsync(Guid entity, params Guid[] assets) =>
        _repository.ReplaceAsync(_agencyA, _user, Module, Kind, entity, assets, CancellationToken.None);

    [Fact]
    public async Task Replace_AddsAUsagePerFile_WithTheWorkspaceOfTheFile()
    {
        var post = Guid.NewGuid();
        var one = await AssetAsync(_agencyA, _workspaceA);
        var two = await AssetAsync(_agencyA, _workspaceA2);

        await ReplaceAsync(post, one, two);

        var usages = await UsagesAsync(_agencyA);
        Assert.Equal(2, usages.Count);
        Assert.Equal(_workspaceA, usages.Single(u => u.Asset == one).Workspace);
        Assert.Equal(_workspaceA2, usages.Single(u => u.Asset == two).Workspace);
        Assert.All(usages, u => Assert.Equal(post, u.Entity));
    }

    [Fact]
    public async Task Replace_AgainWithTheSameFiles_ChangesNothing()
    {
        var post = Guid.NewGuid();
        var one = await AssetAsync(_agencyA, _workspaceA);

        await ReplaceAsync(post, one);
        var before = await UsagesAsync(_agencyA);
        await ReplaceAsync(post, one);

        Assert.Equal(before, await UsagesAsync(_agencyA));
    }

    [Fact]
    public async Task Replace_RemovesTheFilesTheThingNoLongerUses_AndKeepsTheOthers()
    {
        var post = Guid.NewGuid();
        var one = await AssetAsync(_agencyA, _workspaceA);
        var two = await AssetAsync(_agencyA, _workspaceA);
        var three = await AssetAsync(_agencyA, _workspaceA);
        await ReplaceAsync(post, one, two);

        await ReplaceAsync(post, two, three);

        Assert.Equal(new[] { two, three }.Order(), (await UsagesAsync(_agencyA)).Select(u => u.Asset).Order());
    }

    [Fact]
    public async Task Replace_WithNoFiles_RemovesOnlyThisThingsUsages()
    {
        var post = Guid.NewGuid();
        var otherPost = Guid.NewGuid();
        var one = await AssetAsync(_agencyA, _workspaceA);
        await ReplaceAsync(post, one);
        await ReplaceAsync(otherPost, one);

        await ReplaceAsync(post);

        var left = await UsagesAsync(_agencyA);
        Assert.Single(left);
        Assert.Equal(otherPost, left[0].Entity);
    }

    [Fact]
    public async Task AnotherModulesOrKindsUsages_AreNotTouched()
    {
        var thing = Guid.NewGuid();
        var one = await AssetAsync(_agencyA, _workspaceA);
        await _repository.ReplaceAsync(_agencyA, _user, "email", "campaign", thing, [one], CancellationToken.None);

        await ReplaceAsync(thing);

        var left = await UsagesAsync(_agencyA);
        Assert.Single(left);
    }

    [Fact]
    public async Task AFileThatIsNotThere_OrBelongsToAnotherOrganization_GetsNoUsage_AndIsNotAnError()
    {
        var post = Guid.NewGuid();
        var foreign = await AssetAsync(_agencyB, _workspaceB);
        var ours = await AssetAsync(_agencyA, _workspaceA);

        await ReplaceAsync(post, ours, foreign, Guid.NewGuid());

        Assert.Equal([ours], (await UsagesAsync(_agencyA)).Select(u => u.Asset));
        Assert.Empty(await UsagesAsync(_agencyB));
    }

    [Fact]
    public async Task AFileThatIsUsed_CannotBeDeleted_UntilTheUsageIsGone()
    {
        var post = Guid.NewGuid();
        var one = await AssetAsync(_agencyA, _workspaceA);
        await ReplaceAsync(post, one);

        await using (var owner = await _db.OpenOwnerAsync())
        {
            var refused = await Assert.ThrowsAsync<PostgresException>(() => owner.ExecuteAsync("DELETE FROM media_assets WHERE id = @one", new { one }));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, refused.SqlState);
        }

        await ReplaceAsync(post);

        await using var again = await _db.OpenOwnerAsync();
        Assert.Equal(1, await again.ExecuteAsync("DELETE FROM media_assets WHERE id = @one", new { one }));
    }

    [Fact]
    public async Task AnotherOrganization_CannotChangeThisOnesUsages()
    {
        var post = Guid.NewGuid();
        var one = await AssetAsync(_agencyA, _workspaceA);
        await ReplaceAsync(post, one);

        // The same thing named from organization B: row-level security shows B nothing of A's, so nothing is removed.
        await _repository.ReplaceAsync(_agencyB, _user, Module, Kind, post, [], CancellationToken.None);

        Assert.Single(await UsagesAsync(_agencyA));
    }
}
