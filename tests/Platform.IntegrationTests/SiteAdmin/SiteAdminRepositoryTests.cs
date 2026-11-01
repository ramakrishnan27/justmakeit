using Dapper;
using Npgsql;
using Platform.Auth.Models;
using Platform.Auth.Repositories;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Infrastructure.Data;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.SiteAdmin;

/// <summary>
/// The site admin queries as app_user. A site admin has no agency, and being one grants no direct access
/// to any table: everything platform-wide comes through functions that check the caller in the database.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class SiteAdminRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private TestDatabase _db = null!;
    private SiteAdminRepository _repository = null!;

    private Guid _alpha, _bravo, _charlie;                       // trial, paid, trial
    private Guid _alphaOwner, _alphaViewer, _bravoOwner, _charlieOwner;
    private Guid _siteAdmin, _otherSiteAdmin;

    public async Task InitializeAsync()
    {
        DapperConfiguration.Configure();
        _db = await postgres.CreateDatabaseAsync();
        _repository = new SiteAdminRepository(_db.CreateFactory());

        await using var owner = await _db.OpenOwnerAsync();
        _alpha = await DbTestSupport.InsertAgencyAsync(owner, "Alpha");
        _bravo = await DbTestSupport.InsertAgencyAsync(owner, "Bravo", "company");
        _charlie = await DbTestSupport.InsertAgencyAsync(owner, "Charlie", "creator");
        await DbTestSupport.ExecuteAsync(owner, "UPDATE agencies SET plan = 'paid' WHERE id = $1", _bravo);
        await DbTestSupport.ExecuteAsync(owner, "UPDATE agencies SET created_at = $2 WHERE id = $1", _alpha, T0.UtcDateTime);
        await DbTestSupport.ExecuteAsync(owner, "UPDATE agencies SET created_at = $2 WHERE id = $1", _bravo, T0.AddHours(1).UtcDateTime);
        await DbTestSupport.ExecuteAsync(owner, "UPDATE agencies SET created_at = $2 WHERE id = $1", _charlie, T0.AddHours(2).UtcDateTime);

        _alphaOwner = await DbTestSupport.InsertUserAsync(owner, "alpha-owner@example.com");
        _alphaViewer = await DbTestSupport.InsertUserAsync(owner, "alpha-viewer@example.com");
        _bravoOwner = await DbTestSupport.InsertUserAsync(owner, "bravo-owner@example.com");
        _charlieOwner = await DbTestSupport.InsertUserAsync(owner, "charlie-owner@example.com");
        _siteAdmin = await DbTestSupport.InsertUserAsync(owner, "root@example.com");
        _otherSiteAdmin = await DbTestSupport.InsertUserAsync(owner, "root2@example.com");

        await DbTestSupport.InsertMemberAsync(owner, _alpha, _alphaOwner, "organizationadmin");
        await DbTestSupport.InsertMemberAsync(owner, _alpha, _alphaViewer, "clientmanager");
        await DbTestSupport.InsertMemberAsync(owner, _bravo, _bravoOwner, "organizationadmin");
        await DbTestSupport.InsertMemberAsync(owner, _charlie, _charlieOwner, "contentwriter");

        await DbTestSupport.InsertWorkspaceAsync(owner, _alpha, "Alpha one");
        await DbTestSupport.InsertWorkspaceAsync(owner, _alpha, "Alpha two");
        await DbTestSupport.InsertWorkspaceAsync(owner, _bravo, "Bravo one");

        await DbTestSupport.InsertSiteAdminAsync(owner, _siteAdmin);
        await DbTestSupport.InsertSiteAdminAsync(owner, _otherSiteAdmin);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    // --- IsSiteAdminAsync --------------------------------------------------------------------

    [Fact]
    public async Task IsSiteAdmin_IsTrueOnlyForUsersInTheTable()
    {
        Assert.True(await _repository.IsSiteAdminAsync(_siteAdmin, CancellationToken.None));
        Assert.True(await _repository.IsSiteAdminAsync(_otherSiteAdmin, CancellationToken.None));
        Assert.False(await _repository.IsSiteAdminAsync(_alphaOwner, CancellationToken.None));
        Assert.False(await _repository.IsSiteAdminAsync(_alphaViewer, CancellationToken.None));
        Assert.False(await _repository.IsSiteAdminAsync(Guid.NewGuid(), CancellationToken.None));
    }

    // --- The table itself --------------------------------------------------------------------

    [Fact]
    public async Task ASiteAdmin_SeesOnlyTheirOwnRow_OfTheSiteAdminsTable()
    {
        await using var asAdmin = await _db.OpenAppAsync(null, _siteAdmin);

        Assert.Equal([_siteAdmin], (await asAdmin.QueryAsync<Guid>("SELECT user_id FROM site_admins")).ToList());
        Assert.Equal(0, await asAdmin.ExecuteScalarAsync<long>("SELECT count(*) FROM site_admins WHERE user_id = @id", new { id = _otherSiteAdmin }));
    }

    [Fact]
    public async Task AnOrdinaryUser_SeesNothing_InTheSiteAdminsTable()
    {
        await using var asOwner = await _db.OpenAppAsync(_alpha, _alphaOwner);
        await using var asNobody = await _db.OpenAppAsync();

        Assert.Equal(0, await DbTestSupport.CountAsync(asOwner, "site_admins"));
        Assert.Equal(0, await DbTestSupport.CountAsync(asNobody, "site_admins"));
    }

    [Fact]
    public async Task AnOrdinaryUser_CannotMakeThemselvesASiteAdmin()
    {
        await using var asOwner = await _db.OpenAppAsync(_alpha, _alphaOwner);

        var attempt = await Assert.ThrowsAsync<PostgresException>(() => asOwner.ExecuteAsync(
            "INSERT INTO site_admins (user_id) VALUES (@id)", new { id = _alphaOwner }));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, attempt.SqlState);
        Assert.False(await _repository.IsSiteAdminAsync(_alphaOwner, CancellationToken.None));
    }

    [Fact]
    public async Task EvenASiteAdmin_CannotWriteTheSiteAdminsTable_SoNoBugInTheApiCanPromoteAnybody()
    {
        await using var asAdmin = await _db.OpenAppAsync(null, _siteAdmin);

        var insert = await Assert.ThrowsAsync<PostgresException>(() => asAdmin.ExecuteAsync(
            "INSERT INTO site_admins (user_id) VALUES (@id)", new { id = _alphaOwner }));
        var update = await Assert.ThrowsAsync<PostgresException>(() => asAdmin.ExecuteAsync(
            "UPDATE site_admins SET created_at = now()"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => asAdmin.ExecuteAsync(
            "DELETE FROM site_admins WHERE user_id = @id", new { id = _otherSiteAdmin }));

        Assert.All([insert, update, delete], e => Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, e.SqlState));
        await using var owner = await _db.OpenOwnerAsync();
        Assert.Equal(2, await DbTestSupport.CountAsync(owner, "site_admins"));
    }

    [Fact]
    public async Task BeingASiteAdmin_GrantsNoDirectAccessToAnyOtherTable()
    {
        await using var asAdmin = await _db.OpenAppAsync(null, _siteAdmin);

        // Row-level security still applies: with no agency and no memberships, nothing is visible.
        Assert.Equal(0, await DbTestSupport.CountAsync(asAdmin, "agencies"));
        Assert.Equal(0, await DbTestSupport.CountAsync(asAdmin, "agency_members"));
        Assert.Equal(0, await DbTestSupport.CountAsync(asAdmin, "workspaces"));
        Assert.Equal(0, await DbTestSupport.CountAsync(asAdmin, "refresh_tokens"));
        Assert.Equal(1, await DbTestSupport.CountAsync(asAdmin, "users")); // only their own row
    }

    // --- GetOverviewAsync --------------------------------------------------------------------

    [Fact]
    public async Task Overview_CountsEverythingOnThePlatform_ForASiteAdminWithNoAgency()
    {
        var overview = await _repository.GetOverviewAsync(_siteAdmin, CancellationToken.None);

        Assert.Equal(3, overview.AgencyCount);
        Assert.Equal(2, overview.TrialAgencyCount);
        Assert.Equal(1, overview.PaidAgencyCount);
        Assert.Equal(6, overview.UserCount);
        Assert.Equal(3, overview.WorkspaceCount);
        Assert.Equal(4, overview.MemberCount);
    }

    [Fact]
    public async Task Overview_GivesTheSameTotals_ToEverySiteAdmin()
    {
        var first = await _repository.GetOverviewAsync(_siteAdmin, CancellationToken.None);
        var second = await _repository.GetOverviewAsync(_otherSiteAdmin, CancellationToken.None);

        Assert.Equivalent(first, second);
    }

    [Fact]
    public async Task Overview_ChangesWithTheData()
    {
        await using (var owner = await _db.OpenOwnerAsync())
        {
            var delta = await DbTestSupport.InsertAgencyAsync(owner, "Delta");
            await DbTestSupport.InsertWorkspaceAsync(owner, delta, "Delta one");
        }

        var overview = await _repository.GetOverviewAsync(_siteAdmin, CancellationToken.None);

        Assert.Equal(4, overview.AgencyCount);
        Assert.Equal(3, overview.TrialAgencyCount);
        Assert.Equal(4, overview.WorkspaceCount);
    }

    [Fact]
    public async Task Overview_IsRefused_ForAnAgencyOwner_EvenOfTheBiggestAgency()
    {
        var exception = await Assert.ThrowsAsync<ForbiddenException>(
            () => _repository.GetOverviewAsync(_alphaOwner, CancellationToken.None));

        Assert.Equal("not_site_admin", exception.Code);
    }

    [Fact]
    public async Task Overview_IsRefused_ForAUnknownUser()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _repository.GetOverviewAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Overview_IsRefusedByTheDatabaseItself_WhenCalledDirectlyByAnOrdinaryUser()
    {
        await using var asOwner = await _db.OpenAppAsync(_alpha, _alphaOwner);

        var attempt = await Assert.ThrowsAsync<PostgresException>(
            () => asOwner.QueryAsync("SELECT * FROM site_admin_overview()"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, attempt.SqlState);
    }

    [Fact]
    public async Task Overview_IsRefusedWhenNoUserIsSet()
    {
        await using var anonymous = await _db.OpenAppAsync();

        var attempt = await Assert.ThrowsAsync<PostgresException>(
            () => anonymous.QueryAsync("SELECT * FROM site_admin_overview()"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, attempt.SqlState);
    }

    // --- ListAgenciesAsync -------------------------------------------------------------------

    [Fact]
    public async Task ListAgencies_ReturnsEveryAgency_NewestFirst_WithTheirCounts()
    {
        var page = await _repository.ListAgenciesAsync(_siteAdmin, 1, 20, CancellationToken.None);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(["Charlie", "Bravo", "Alpha"], page.Items.Select(a => a.Name));
        Assert.Equal([AgencyPlan.Trial, AgencyPlan.Paid, AgencyPlan.Trial], page.Items.Select(a => a.Plan));
        Assert.Equal([BusinessModel.Creator, BusinessModel.Company, BusinessModel.Agency], page.Items.Select(a => a.BusinessModel));
        Assert.Equal([1L, 1L, 2L], page.Items.Select(a => a.MemberCount));
        Assert.Equal([0L, 1L, 2L], page.Items.Select(a => a.WorkspaceCount));
        Assert.Equal(T0.AddHours(2), page.Items[0].CreatedAt);
        Assert.Equal([_charlie, _bravo, _alpha], page.Items.Select(a => a.Id));
    }

    [Fact]
    public async Task ListAgencies_PagesThroughTheList_KeepingTheTotal()
    {
        var first = await _repository.ListAgenciesAsync(_siteAdmin, 1, 2, CancellationToken.None);
        var second = await _repository.ListAgenciesAsync(_siteAdmin, 2, 2, CancellationToken.None);

        Assert.Equal(["Charlie", "Bravo"], first.Items.Select(a => a.Name));
        Assert.Equal(["Alpha"], second.Items.Select(a => a.Name));
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(3, second.TotalCount);
    }

    [Fact]
    public async Task ListAgencies_BeyondTheLastPage_IsEmpty_ButStillReportsTheTotal()
    {
        var page = await _repository.ListAgenciesAsync(_siteAdmin, 9, 20, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task ListAgencies_IsRefused_ForAnAgencyOwner()
    {
        var exception = await Assert.ThrowsAsync<ForbiddenException>(
            () => _repository.ListAgenciesAsync(_bravoOwner, 1, 20, CancellationToken.None));

        Assert.Equal("not_site_admin", exception.Code);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(101, 0)]
    [InlineData(10, -1)]
    public async Task ListAgencies_TheFunctionRefusesBadPagingByItself(int limit, int offset)
    {
        await using var asAdmin = await _db.OpenAppAsync(null, _siteAdmin);

        var attempt = await Assert.ThrowsAsync<PostgresException>(
            () => asAdmin.QueryAsync("SELECT * FROM site_admin_list_agencies(@limit, @offset)", new { limit, offset }));

        Assert.Equal(PostgresErrorCodes.InvalidParameterValue, attempt.SqlState);
    }

    [Fact]
    public async Task TheSiteAdminFunctions_AreSecurityDefiner_WithAFixedSearchPath_AndNotCallableByPublic()
    {
        await using var owner = await _db.OpenOwnerAsync();

        var functions = (await owner.QueryAsync<(string Name, bool Definer, string? Config, bool PublicCanRun)>(
            """
            SELECT p.proname,
                   p.prosecdef,
                   array_to_string(p.proconfig, ','),
                   has_function_privilege('public', p.oid, 'EXECUTE')
            FROM pg_proc p
            WHERE p.pronamespace = 'public'::regnamespace
              AND p.proname IN ('site_admin_overview', 'site_admin_list_agencies', 'auth_find_site_refresh_token')
            """)).ToList();

        Assert.Equal(3, functions.Count);
        Assert.All(functions, f =>
        {
            Assert.True(f.Definer, $"{f.Name} is not SECURITY DEFINER.");
            Assert.Contains("search_path=public, pg_temp", f.Config);
            Assert.False(f.PublicCanRun, $"{f.Name} can be run by PUBLIC.");
        });
    }
}
