using Dapper;
using Platform.Auth.Models;
using Platform.Auth.Repositories;
using Platform.Infrastructure.Data;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Auth;

/// <summary>
/// Workspace queries as app_user. Agency A must never read agency B's workspaces, whether through the
/// repository or through plain SQL that forgets to filter.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class WorkspaceRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private TestDatabase _db = null!;
    private WorkspaceRepository _repository = null!;
    private Guid _agencyA, _agencyB;
    private Guid _userA, _userB;

    public async Task InitializeAsync()
    {
        DapperConfiguration.Configure();
        _db = await postgres.CreateDatabaseAsync();
        _repository = new WorkspaceRepository(_db.CreateFactory());

        await using var owner = await _db.OpenOwnerAsync();
        _agencyA = await DbTestSupport.InsertAgencyAsync(owner, "Agency A");
        _agencyB = await DbTestSupport.InsertAgencyAsync(owner, "Agency B");
        _userA = await DbTestSupport.InsertUserAsync(owner, "a@example.com");
        _userB = await DbTestSupport.InsertUserAsync(owner, "b@example.com");
        await DbTestSupport.InsertMemberAsync(owner, _agencyA, _userA, "organizationadmin");
        await DbTestSupport.InsertMemberAsync(owner, _agencyB, _userB, "organizationadmin");
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<WorkspaceRecord> AddAsync(Guid agencyId, Guid userId, string name, DateTimeOffset createdAt)
    {
        var workspace = new WorkspaceRecord { Id = Guid.NewGuid(), AgencyId = agencyId, Name = name, CreatedAt = createdAt };
        await _repository.InsertAsync(workspace, userId, CancellationToken.None);
        return workspace;
    }

    [Fact]
    public async Task List_ReturnsTheAgencysWorkspaces_OldestFirst()
    {
        var second = await AddAsync(_agencyA, _userA, "Second", Noon.AddMinutes(10));
        var first = await AddAsync(_agencyA, _userA, "First", Noon);
        var third = await AddAsync(_agencyA, _userA, "Third", Noon.AddMinutes(20));

        var list = await _repository.ListAsync(_agencyA, _userA, CancellationToken.None);

        Assert.Equal([first.Id, second.Id, third.Id], list.Select(w => w.Id));
        Assert.Equal(["First", "Second", "Third"], list.Select(w => w.Name));
        Assert.All(list, w => Assert.Equal(_agencyA, w.AgencyId));
        Assert.Equal(Noon, list[0].CreatedAt);
    }

    [Fact]
    public async Task List_NeverIncludesAnotherAgencysWorkspaces()
    {
        var mine = await AddAsync(_agencyA, _userA, "Mine", Noon);
        await AddAsync(_agencyB, _userB, "Theirs", Noon);
        await AddAsync(_agencyB, _userB, "Theirs too", Noon.AddMinutes(1));

        var asA = await _repository.ListAsync(_agencyA, _userA, CancellationToken.None);
        var asB = await _repository.ListAsync(_agencyB, _userB, CancellationToken.None);

        Assert.Equal([mine.Id], asA.Select(w => w.Id));
        Assert.Equal(["Theirs", "Theirs too"], asB.Select(w => w.Name));
    }

    [Fact]
    public async Task List_ForAnAgencyWithNoWorkspaces_IsEmpty()
    {
        await AddAsync(_agencyB, _userB, "Theirs", Noon);

        Assert.Empty(await _repository.ListAsync(_agencyA, _userA, CancellationToken.None));
    }

    [Fact]
    public async Task AgencyA_CannotReadAgencyBsWorkspaces_WhileConnectedAsAppUser()
    {
        await AddAsync(_agencyA, _userA, "A one", Noon);
        var secret = await AddAsync(_agencyB, _userB, "B secret", Noon);

        await using var asA = await _db.OpenAppAsync(_agencyA, _userA);

        // Plain SQL with no WHERE clause at all: row-level security alone keeps B out.
        var everything = (await asA.QueryAsync<Guid>("SELECT id FROM workspaces")).ToList();
        Assert.Single(everything);
        Assert.DoesNotContain(secret.Id, everything);

        // Asking for B's rows by name finds nothing, whether by agency or by id.
        Assert.Equal(0, await asA.ExecuteScalarAsync<long>("SELECT count(*) FROM workspaces WHERE agency_id = @id", new { id = _agencyB }));
        Assert.Equal(0, await asA.ExecuteScalarAsync<long>("SELECT count(*) FROM workspaces WHERE id = @id", new { id = secret.Id }));
        Assert.Null(await asA.ExecuteScalarAsync<string?>("SELECT name FROM workspaces WHERE id = @id", new { id = secret.Id }));
    }

    [Fact]
    public async Task AgencyB_CannotReadAgencyAsWorkspaces_EitherWay()
    {
        var secret = await AddAsync(_agencyA, _userA, "A secret", Noon);
        await AddAsync(_agencyB, _userB, "B one", Noon);

        await using var asB = await _db.OpenAppAsync(_agencyB, _userB);

        Assert.Equal(["B one"], (await asB.QueryAsync<string>("SELECT name FROM workspaces")).ToList());
        Assert.Equal(0, await asB.ExecuteScalarAsync<long>("SELECT count(*) FROM workspaces WHERE id = @id", new { id = secret.Id }));
    }

    [Fact]
    public async Task Insert_StoresTheWorkspace_VisibleToItsAgencyOnly()
    {
        var created = await AddAsync(_agencyA, _userA, "Client one", Noon);

        await using var asA = await _db.OpenAppAsync(_agencyA, _userA);
        await using var asB = await _db.OpenAppAsync(_agencyB, _userB);
        Assert.Equal(created.Id, await asA.ExecuteScalarAsync<Guid>("SELECT id FROM workspaces WHERE name = 'Client one'"));
        Assert.Equal(0, await DbTestSupport.CountAsync(asB, "workspaces"));
    }

    [Fact]
    public async Task Insert_KeepsTheGivenTimeAndAgency()
    {
        var created = await AddAsync(_agencyA, _userA, "Timed", Noon.AddHours(3));

        await using var owner = await _db.OpenOwnerAsync();
        var row = await owner.QuerySingleAsync<(Guid AgencyId, DateTimeOffset CreatedAt)>(
            "SELECT agency_id, created_at FROM workspaces WHERE id = @id", new { id = created.Id });
        Assert.Equal((_agencyA, Noon.AddHours(3)), row);
    }

    [Fact]
    public async Task Insert_WithAnOverlongName_FailsTheDatabaseCheck_AndStoresNothing()
    {
        var tooLong = new WorkspaceRecord { Id = Guid.NewGuid(), AgencyId = _agencyA, Name = new string('n', 201), CreatedAt = Noon };

        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => _repository.InsertAsync(tooLong, _userA, CancellationToken.None));

        Assert.Empty(await _repository.ListAsync(_agencyA, _userA, CancellationToken.None));
    }

    [Fact]
    public async Task AWorkspaceCannotBeInsertedIntoAnotherAgency_ByPlainSql()
    {
        await using var asA = await _db.OpenAppAsync(_agencyA, _userA);

        var attempt = Assert.ThrowsAsync<Npgsql.PostgresException>(() => asA.ExecuteAsync(
            "INSERT INTO workspaces (agency_id, name) VALUES (@agencyId, 'Planted')", new { agencyId = _agencyB }));

        Assert.Equal(Npgsql.PostgresErrorCodes.InsufficientPrivilege, (await attempt).SqlState);
        await using var owner = await _db.OpenOwnerAsync();
        Assert.Equal(0, await DbTestSupport.CountAsync(owner, "workspaces"));
    }
}
