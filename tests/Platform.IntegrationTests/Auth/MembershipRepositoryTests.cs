using Dapper;
using Platform.Auth.Models;
using Platform.Auth.Repositories;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Auth;

/// <summary>Membership and agency queries as app_user: a user sees their own memberships and nothing else.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class MembershipRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private TestDatabase _db = null!;
    private MembershipRepository _memberships = null!;
    private AgencyRepository _agencies = null!;

    // Ana belongs to two agencies, Bob to one of them, Cleo to none of Ana's.
    private Guid _agencyOne, _agencyTwo, _agencyThree;
    private Guid _ana, _bob, _cleo;

    public async Task InitializeAsync()
    {
        DapperConfiguration.Configure();
        _db = await postgres.CreateDatabaseAsync();
        var factory = _db.CreateFactory();
        _memberships = new MembershipRepository(factory);
        _agencies = new AgencyRepository(factory);

        await using var owner = await _db.OpenOwnerAsync();
        _agencyOne = await DbTestSupport.InsertAgencyAsync(owner, "Agency One");
        _agencyTwo = await DbTestSupport.InsertAgencyAsync(owner, "Agency Two");
        _agencyThree = await DbTestSupport.InsertAgencyAsync(owner, "Agency Three", "company");
        _ana = await DbTestSupport.InsertUserAsync(owner, "ana@example.com");
        _bob = await DbTestSupport.InsertUserAsync(owner, "bob@example.com");
        _cleo = await DbTestSupport.InsertUserAsync(owner, "cleo@example.com");

        await DbTestSupport.InsertMemberAsync(owner, _agencyOne, _ana, "organizationadmin", Noon.AddHours(-2));
        await DbTestSupport.InsertMemberAsync(owner, _agencyTwo, _ana, "clientmanager", Noon.AddHours(-1));
        await DbTestSupport.InsertMemberAsync(owner, _agencyTwo, _bob, "marketingmanager", Noon);
        await DbTestSupport.InsertMemberAsync(owner, _agencyThree, _cleo, "organizationadmin", Noon);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<DateTimeOffset> LastActiveAsync(Guid agencyId, Guid userId)
    {
        await using var owner = await _db.OpenOwnerAsync();
        return await owner.QuerySingleAsync<DateTimeOffset>(
            "SELECT last_active_at FROM agency_members WHERE agency_id = @agencyId AND user_id = @userId", new { agencyId, userId });
    }

    // --- ListForUserAsync --------------------------------------------------------------------

    [Fact]
    public async Task ListForUser_ReturnsEveryMembershipOfThatUser_WithAgencyNameAndRole()
    {
        var list = await _memberships.ListForUserAsync(_ana, CancellationToken.None);

        Assert.Equal(2, list.Count);
        Assert.Contains(list, m => m.AgencyId == _agencyOne && m.AgencyName == "Agency One" && m.Role == OrganizationRole.OrganizationAdmin);
        Assert.Contains(list, m => m.AgencyId == _agencyTwo && m.AgencyName == "Agency Two" && m.Role == OrganizationRole.ClientManager);
    }

    [Fact]
    public async Task ListForUser_ReturnsTheBusinessModelOfEachOrganization()
    {
        await using (var owner = await _db.OpenOwnerAsync())
        {
            var solo = await DbTestSupport.InsertAgencyAsync(owner, "Solo", "creator");
            await DbTestSupport.InsertMemberAsync(owner, solo, _cleo, "contentwriter", Noon.AddHours(1));
        }

        var list = await _memberships.ListForUserAsync(_cleo, CancellationToken.None);

        Assert.Equal(
            [("Solo", BusinessModel.Creator, OrganizationRole.ContentWriter), ("Agency Three", BusinessModel.Company, OrganizationRole.OrganizationAdmin)],
            list.Select(m => (m.AgencyName, m.BusinessModel, m.Role)));
    }

    [Fact]
    public async Task ListForUser_PutsTheMostRecentlyUsedAgencyFirst()
    {
        var list = await _memberships.ListForUserAsync(_ana, CancellationToken.None);

        Assert.Equal([_agencyTwo, _agencyOne], list.Select(m => m.AgencyId));
    }

    [Fact]
    public async Task ListForUser_NeverIncludesAnotherUsersMemberships()
    {
        var bobs = await _memberships.ListForUserAsync(_bob, CancellationToken.None);
        var cleos = await _memberships.ListForUserAsync(_cleo, CancellationToken.None);

        Assert.Equal([_agencyTwo], bobs.Select(m => m.AgencyId));
        Assert.Equal([_agencyThree], cleos.Select(m => m.AgencyId));
    }

    [Fact]
    public async Task ListForUser_ForAUserWithNoMemberships_IsEmpty()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var loner = await DbTestSupport.InsertUserAsync(owner, "loner@example.com");

        Assert.Empty(await _memberships.ListForUserAsync(loner, CancellationToken.None));
        Assert.Empty(await _memberships.ListForUserAsync(Guid.NewGuid(), CancellationToken.None));
    }

    // --- FindAsync ---------------------------------------------------------------------------

    [Fact]
    public async Task Find_ReturnsTheMembership_ForAnAgencyTheUserBelongsTo()
    {
        var found = await _memberships.FindAsync(_ana, _agencyTwo, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(_agencyTwo, found.AgencyId);
        Assert.Equal("Agency Two", found.AgencyName);
        Assert.Equal(BusinessModel.Agency, found.BusinessModel);
        Assert.Equal(OrganizationRole.ClientManager, found.Role);
    }

    [Fact]
    public async Task Find_ReturnsNull_ForAnExistingAgencyTheUserDoesNotBelongTo()
    {
        Assert.Null(await _memberships.FindAsync(_ana, _agencyThree, CancellationToken.None));
        Assert.Null(await _memberships.FindAsync(_cleo, _agencyOne, CancellationToken.None));
        Assert.Null(await _memberships.FindAsync(_bob, _agencyOne, CancellationToken.None));
    }

    [Fact]
    public async Task Find_ReturnsNull_ForAnUnknownAgencyOrUser()
    {
        Assert.Null(await _memberships.FindAsync(_ana, Guid.NewGuid(), CancellationToken.None));
        Assert.Null(await _memberships.FindAsync(Guid.NewGuid(), _agencyOne, CancellationToken.None));
    }

    // --- TouchAsync --------------------------------------------------------------------------

    [Fact]
    public async Task Touch_MovesThatMembershipToTheFront_AndChangesNothingElse()
    {
        var bobBefore = await LastActiveAsync(_agencyTwo, _bob);
        var anaOtherBefore = await LastActiveAsync(_agencyTwo, _ana);
        var later = Noon.AddDays(1);

        await _memberships.TouchAsync(_agencyOne, _ana, later, CancellationToken.None);

        Assert.Equal(later, await LastActiveAsync(_agencyOne, _ana));
        Assert.Equal([_agencyOne, _agencyTwo], (await _memberships.ListForUserAsync(_ana, CancellationToken.None)).Select(m => m.AgencyId));
        Assert.Equal(anaOtherBefore, await LastActiveAsync(_agencyTwo, _ana));
        Assert.Equal(bobBefore, await LastActiveAsync(_agencyTwo, _bob));
    }

    [Fact]
    public async Task Touch_ByAUserWhoIsNotAMember_ChangesNobodysRow()
    {
        var before = await LastActiveAsync(_agencyThree, _cleo);

        // Ana is not in agency three: the row is Cleo's, and a connection for Ana in that agency cannot reach it.
        await _memberships.TouchAsync(_agencyThree, _ana, Noon.AddDays(5), CancellationToken.None);

        Assert.Equal(before, await LastActiveAsync(_agencyThree, _cleo));
    }

    // --- AgencyRepository --------------------------------------------------------------------

    [Fact]
    public async Task GetAgency_ReturnsItWithItsTrialPlan_ForAMember()
    {
        var agency = await _agencies.GetAsync(_agencyOne, _ana, CancellationToken.None);

        Assert.NotNull(agency);
        Assert.Equal(_agencyOne, agency.Id);
        Assert.Equal("Agency One", agency.Name);
        Assert.Equal(AgencyPlan.Trial, agency.Plan);
        Assert.Equal(BusinessModel.Agency, agency.BusinessModel);
    }

    [Fact]
    public async Task GetAgency_ReturnsTheBusinessModelItWasCreatedWith()
    {
        var company = await _agencies.GetAsync(_agencyThree, _cleo, CancellationToken.None);

        Assert.Equal(BusinessModel.Company, company!.BusinessModel);
    }

    [Fact]
    public async Task GetAgency_ReadsAPaidPlan()
    {
        await using (var owner = await _db.OpenOwnerAsync())
        {
            await DbTestSupport.ExecuteAsync(owner, "UPDATE agencies SET plan = 'paid' WHERE id = $1", _agencyOne);
        }

        Assert.Equal(AgencyPlan.Paid, (await _agencies.GetAsync(_agencyOne, _ana, CancellationToken.None))!.Plan);
    }

    [Fact]
    public async Task Agencies_AreInvisible_ToAUserWhoIsNotAMember()
    {
        // Cleo works in agency three. Whatever she asks for, agencies one and two are not hers to see.
        await using var asCleo = await _db.OpenAppAsync(_agencyThree, _cleo);

        Assert.Equal([_agencyThree], (await asCleo.QueryAsync<Guid>("SELECT id FROM agencies")).ToList());
        Assert.Equal(0, await asCleo.ExecuteScalarAsync<long>("SELECT count(*) FROM agencies WHERE id = @id", new { id = _agencyOne }));
        Assert.Equal(0, await asCleo.ExecuteScalarAsync<long>("SELECT count(*) FROM agency_members WHERE agency_id = @id", new { id = _agencyTwo }));
    }

    [Fact]
    public async Task Agencies_AMemberSeesTheirOwnAgenciesForTheSwitcher_ButNoOthers()
    {
        await using var asAna = await _db.OpenAppAsync(_agencyOne, _ana);

        var visible = (await asAna.QueryAsync<Guid>("SELECT id FROM agencies")).ToHashSet();

        Assert.True(visible.SetEquals([_agencyOne, _agencyTwo]));
        Assert.DoesNotContain(_agencyThree, visible);
    }

    [Fact]
    public async Task GetAgency_ForAnUnknownAgency_ReturnsNull()
    {
        Assert.Null(await _agencies.GetAsync(Guid.NewGuid(), _ana, CancellationToken.None));
    }
}
