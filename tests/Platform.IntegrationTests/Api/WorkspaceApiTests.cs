using System.Net;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

/// <summary>The workspace endpoints end to end: the role rules, and agencies never seeing each other's workspaces.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Database")]
public sealed class WorkspaceApiTests(PostgresFixture postgres) : AuthApiTestBase(postgres)
{
    private static string[] NamesIn(System.Text.Json.JsonElement list) =>
        [.. list.EnumerateArray().Select(w => w.GetProperty("name").GetString()!)];

    /// <summary>A member with the given role in the session's agency, as a database row plus a token signed like the API's.</summary>
    private async Task<string> MemberTokenAsync(Session ofAgency, OrganizationRole role)
    {
        await using var owner = await Host.Database.OpenOwnerAsync();
        var userId = await DbTestSupport.InsertUserAsync(owner, $"{role}-{Guid.NewGuid():N}@example.com");
        await DbTestSupport.InsertMemberAsync(owner, ofAgency.AgencyId, userId, role.ToDb());
        return Host.Factory.CreateAccessToken(userId, ofAgency.AgencyId, role);
    }

    // --- Listing -----------------------------------------------------------------------------

    [Theory]
    [InlineData(OrganizationRole.OrganizationAdmin)]
    [InlineData(OrganizationRole.ClientManager)]
    [InlineData(OrganizationRole.MarketingManager)]
    [InlineData(OrganizationRole.ContentWriter)]
    public async Task EveryRole_CanListTheOrganizationsWorkspaces(OrganizationRole role)
    {
        var owner = await Host.RegisterAsync();
        var token = await MemberTokenAsync(owner, role);

        var response = await Host.GetAsync("/v1/workspaces", token);

        AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(["Main workspace"], NamesIn(await ApiTestHost.ReadJsonAsync(response)));
    }

    [Fact]
    public async Task List_ReturnsIdNameAndCreatedAt_OldestFirst()
    {
        var owner = await Host.RegisterAsync();
        await Host.PostAsync("/v1/workspaces", new { name = "Second" }, owner.AccessToken);
        await Host.PostAsync("/v1/workspaces", new { name = "Third" }, owner.AccessToken);

        var list = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/workspaces", owner.AccessToken));

        Assert.Equal(["Main workspace", "Second", "Third"], NamesIn(list));
        var first = list[0];
        Assert.NotEqual(Guid.Empty, first.GetProperty("id").GetGuid());
        Assert.True(first.TryGetProperty("createdAt", out _));
        Assert.False(first.TryGetProperty("agencyId", out _));
    }

    // --- Creating ----------------------------------------------------------------------------

    [Theory]
    [InlineData(OrganizationRole.OrganizationAdmin)]
    public async Task OrganizationAdmins_CanCreateAWorkspace(OrganizationRole role)
    {
        var owner = await Host.RegisterAsync();
        var token = await MemberTokenAsync(owner, role);

        var response = await Host.PostAsync("/v1/workspaces", new { name = "  Client one  " }, token);

        AssertStatus(HttpStatusCode.Created, response);
        var created = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal("Client one", created.GetProperty("name").GetString());
        var list = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/workspaces", owner.AccessToken));
        Assert.Contains("Client one", NamesIn(list));
    }

    [Theory]
    [InlineData(OrganizationRole.ClientManager)]
    [InlineData(OrganizationRole.MarketingManager)]
    [InlineData(OrganizationRole.ContentWriter)]
    public async Task EveryRoleButTheOrganizationAdmin_CannotCreateAWorkspace(OrganizationRole role)
    {
        var owner = await Host.RegisterAsync();
        var token = await MemberTokenAsync(owner, role);

        var response = await Host.PostAsync("/v1/workspaces", new { name = "Sneaky" }, token);

        AssertStatus(HttpStatusCode.Forbidden, response);
        Assert.Equal(1, await OwnerQueryAsync<long>(
            "SELECT count(*) FROM workspaces WHERE agency_id = @id", new { id = owner.AgencyId }));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("agency")]
    public async Task ACompanyOrAgencyFounder_CanAddMoreWorkspaces(string model)
    {
        var founder = await Host.RegisterAsync(businessModel: model);

        AssertStatus(HttpStatusCode.Created, await Host.PostAsync("/v1/workspaces", new { name = "Second" }, founder.AccessToken));
        AssertStatus(HttpStatusCode.Created, await Host.PostAsync("/v1/workspaces", new { name = "Third" }, founder.AccessToken));

        Assert.Equal(3, await OwnerQueryAsync<long>("SELECT count(*) FROM workspaces WHERE agency_id = @id", new { id = founder.AgencyId }));
    }

    [Fact]
    public async Task AnIndividualCreator_HasOneWorkspace_CanListIt_AndCannotAddAnother()
    {
        var creator = await Host.RegisterAsync(businessModel: "creator");

        var list = await Host.GetAsync("/v1/workspaces", creator.AccessToken);
        var add = await Host.PostAsync("/v1/workspaces", new { name = "Another" }, creator.AccessToken);

        AssertStatus(HttpStatusCode.OK, list);
        Assert.Equal(["Main workspace"], NamesIn(await ApiTestHost.ReadJsonAsync(list)));
        AssertStatus(HttpStatusCode.Forbidden, add);
        Assert.Equal(1, await OwnerQueryAsync<long>("SELECT count(*) FROM workspaces WHERE agency_id = @id", new { id = creator.AgencyId }));
    }

    [Fact]
    public async Task Create_PutsTheWorkspaceInTheTokensAgency_NoMatterWhatTheBodyClaims()
    {
        var ana = await Host.RegisterAsync();
        var bob = await Host.RegisterAsync();

        var response = await Host.PostAsync(
            "/v1/workspaces", new { name = "Planted", agencyId = bob.AgencyId }, ana.AccessToken);

        AssertStatus(HttpStatusCode.Created, response);
        var id = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("id").GetGuid();
        Assert.Equal(ana.AgencyId, await OwnerQueryAsync<Guid>("SELECT agency_id FROM workspaces WHERE id = @id", new { id }));
        Assert.Equal(0, await OwnerQueryAsync<long>(
            "SELECT count(*) FROM workspaces WHERE agency_id = @id AND name = 'Planted'", new { id = bob.AgencyId }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithoutAName_Returns400(string? name)
    {
        var owner = await Host.RegisterAsync();

        var response = await Host.PostAsync("/v1/workspaces", new { name }, owner.AccessToken);

        AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.True((await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("name", out _));
    }

    [Fact]
    public async Task Create_WithATooLongName_Returns400_AndStoresNothing()
    {
        var owner = await Host.RegisterAsync();

        var response = await Host.PostAsync("/v1/workspaces", new { name = new string('n', 201) }, owner.AccessToken);

        AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.Equal(1, await OwnerQueryAsync<long>("SELECT count(*) FROM workspaces"));
    }

    // --- Isolation ---------------------------------------------------------------------------

    [Fact]
    public async Task AgencyA_CannotSeeAgencyBsWorkspaces_AndTheOtherWayAround()
    {
        var ana = await Host.RegisterAsync(agencyName: "Ana Agency");
        var bob = await Host.RegisterAsync(agencyName: "Bob Agency");
        await Host.PostAsync("/v1/workspaces", new { name = "Ana client" }, ana.AccessToken);
        await Host.PostAsync("/v1/workspaces", new { name = "Bob client" }, bob.AccessToken);

        var anasList = await Host.GetAsync("/v1/workspaces", ana.AccessToken);
        var bobsList = await Host.GetAsync("/v1/workspaces", bob.AccessToken);

        Assert.Equal(["Main workspace", "Ana client"], NamesIn(await ApiTestHost.ReadJsonAsync(anasList)));
        Assert.Equal(["Main workspace", "Bob client"], NamesIn(await ApiTestHost.ReadJsonAsync(bobsList)));
        Assert.DoesNotContain("Bob", await anasList.Content.ReadAsStringAsync());
        Assert.DoesNotContain("Ana", await bobsList.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AUserInTwoAgencies_SeesOnlyTheCurrentAgencysWorkspaces_AndSwitchingChangesThem()
    {
        var registered = await Host.RegisterAsync(agencyName: "First agency");
        await Host.PostAsync("/v1/workspaces", new { name = "First client" }, registered.AccessToken);
        var second = await AddMembershipAsync(registered, "organizationadmin", "Second agency");
        await OwnerExecuteAsync("INSERT INTO workspaces (agency_id, name) VALUES (@id, 'Second client')", new { id = second });

        var inFirst = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/workspaces", registered.AccessToken));
        var switched = await Host.PostAsync("/v1/auth/switch-agency", new { agencyId = second }, tokenCookie: registered.AccessToken);
        var inSecond = await ApiTestHost.ReadJsonAsync(
            await Host.GetAsync("/v1/workspaces", tokenCookie: ApiTestHost.MemberCookieValue(switched)));

        Assert.Equal(["Main workspace", "First client"], NamesIn(inFirst));
        Assert.Equal(["Second client"], NamesIn(inSecond));
    }

    [Fact]
    public async Task ATokenForAnAgencyWithNoRows_SeesAnEmptyList_NotAnotherAgencysRows()
    {
        var registered = await Host.RegisterAsync();
        await using (var owner = await Host.Database.OpenOwnerAsync())
        {
            await DbTestSupport.ExecuteAsync(owner, "DELETE FROM workspaces WHERE agency_id = $1", registered.AgencyId);
        }

        var list = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/workspaces", registered.AccessToken));

        Assert.Empty(list.EnumerateArray());
    }
}
