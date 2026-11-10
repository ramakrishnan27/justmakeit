using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Platform.Auth.Interfaces;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// Permissions travel in the access token and every request is checked against them, with a real
/// database: what a token carries after each way of signing in, and how a change the Site admin makes
/// reaches a person who is already signed in (their token is refused, they sign in again, the new token has
/// the new permissions). There is no refresh, so signing in again is the only way to a current token.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Database")]
public sealed class PermissionTokenApiTests(PostgresFixture postgres) : AuthApiTestBase(postgres)
{
    private static string[] PermissionsIn(string accessToken) =>
        [.. new JsonWebToken(accessToken).GetPayloadValue<string[]>(ClaimNames.Permission).Order()];

    private static long VersionIn(string accessToken) =>
        new JsonWebToken(accessToken).GetPayloadValue<long>(ClaimNames.PermissionVersion);

    private Task<long> CurrentVersionAsync() => OwnerQueryAsync<long>("SELECT version FROM permission_state");

    /// <summary>What the Site admin API will do in a later step: change the grant, then tell this process.</summary>
    private async Task SetGrantAsync(string model, string role, string key, bool granted)
    {
        await OwnerExecuteAsync(
            "UPDATE role_permissions SET is_granted = @granted WHERE business_model = @model AND role = @role AND permission_key = @key",
            new { granted, model, role, key });
        Host.Factory.Services.GetRequiredService<IPermissionService>().Invalidate();
    }

    // --- What a token carries ----------------------------------------------------------------

    [Theory]
    [InlineData("company")]
    [InlineData("agency")]
    public async Task ACompanyOrAgencyFounder_GetsEveryPermission_AndTheCurrentVersion(string businessModel)
    {
        var session = await Host.RegisterAsync(businessModel: businessModel);

        Assert.Equal(PermissionKeys.All.Order(), PermissionsIn(session.AccessToken));
        Assert.Equal(await CurrentVersionAsync(), VersionIn(session.AccessToken));
    }

    [Fact]
    public async Task AnIndividualCreator_GetsTheContentWritersPermissions_NotTheAdminOnes()
    {
        var session = await Host.RegisterAsync(businessModel: "creator");

        var permissions = PermissionsIn(session.AccessToken);
        Assert.Equal(
            [PermissionKeys.DashboardView, PermissionKeys.DataRead, PermissionKeys.DataWrite, PermissionKeys.WorkspacesView],
            permissions);
        Assert.DoesNotContain(PermissionKeys.WorkspacesCreate, permissions);
        Assert.DoesNotContain(PermissionKeys.OrganizationManage, permissions);
    }

    [Fact]
    public async Task RegisterAndLogin_IssueTheSamePermissions_UnderTheCurrentVersion()
    {
        var registered = await Host.RegisterAsync(businessModel: "company");

        var login = await Host.LoginAsync(registered.Email);

        Assert.Equal(PermissionKeys.All.Order(), PermissionsIn(registered.AccessToken));
        Assert.Equal(PermissionKeys.All.Order(), PermissionsIn(login.AccessToken));
        Assert.Equal(await CurrentVersionAsync(), VersionIn(login.AccessToken));
    }

    [Fact]
    public async Task ASiteAdminToken_CarriesNoPermissionsAndNoVersion()
    {
        await using var owner = await Host.Database.OpenOwnerAsync();
        var userId = await DbTestSupport.InsertUserAsync(owner, $"{Guid.NewGuid():N}@example.com");
        await DbTestSupport.InsertSiteAdminAsync(owner, userId);

        var token = new JsonWebToken(Host.Factory.CreateSiteAdminAccessToken(userId));

        Assert.False(token.TryGetClaim(ClaimNames.Permission, out _));
        Assert.False(token.TryGetClaim(ClaimNames.PermissionVersion, out _));
    }

    // --- A change reaches people who are signed in -------------------------------------------

    [Fact]
    public async Task ARevokedPermission_MakesTheOldTokenStale_AndTheNextSignInLacksIt()
    {
        var admin = await Host.RegisterAsync(businessModel: "company");
        AssertStatus(HttpStatusCode.Created, await Host.PostAsync("/v1/workspaces", new { name = "Before" }, tokenCookie: admin.AccessToken));

        await SetGrantAsync("company", "organizationadmin", PermissionKeys.WorkspacesCreate, granted: false);

        // The token they hold was issued under the old version: any request is told to sign in again, not denied.
        var stale = await Host.GetAsync("/v1/workspaces", tokenCookie: admin.AccessToken);
        AssertStatus(HttpStatusCode.Unauthorized, stale);
        Assert.Equal("permissions_changed", await CodeOfAsync(stale));
        AssertStatus(HttpStatusCode.Unauthorized, await Host.GetAsync("/v1/auth/me", tokenCookie: admin.AccessToken));
        AssertStatus(HttpStatusCode.Unauthorized, await Host.GetAsync("/v1/workspaces", admin.AccessToken)); // as a header too

        var fresh = (await Host.LoginAsync(admin.Email)).AccessToken;

        Assert.DoesNotContain(PermissionKeys.WorkspacesCreate, PermissionsIn(fresh));
        Assert.Contains(PermissionKeys.WorkspacesView, PermissionsIn(fresh));
        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/workspaces", tokenCookie: fresh));
        AssertStatus(HttpStatusCode.Forbidden, await Host.PostAsync("/v1/workspaces", new { name = "After" }, tokenCookie: fresh));
    }

    [Fact]
    public async Task AGrantedPermission_ReachesTheSamePersonAtTheirNextSignIn()
    {
        var admin = await Host.RegisterAsync(businessModel: "company");
        await SetGrantAsync("company", "organizationadmin", PermissionKeys.WorkspacesCreate, granted: false);
        var withoutIt = (await Host.LoginAsync(admin.Email)).AccessToken;
        AssertStatus(HttpStatusCode.Forbidden, await Host.PostAsync("/v1/workspaces", new { name = "No" }, tokenCookie: withoutIt));

        await SetGrantAsync("company", "organizationadmin", PermissionKeys.WorkspacesCreate, granted: true);

        AssertStatus(HttpStatusCode.Unauthorized, await Host.GetAsync("/v1/workspaces", tokenCookie: withoutIt));
        var withIt = (await Host.LoginAsync(admin.Email)).AccessToken;
        AssertStatus(HttpStatusCode.Created, await Host.PostAsync("/v1/workspaces", new { name = "Yes" }, tokenCookie: withIt));
    }

    [Fact]
    public async Task EveryTokenOfTheOldVersion_IsRefused_NotOnlyTheOnesOfTheChangedRole()
    {
        var creator = await Host.RegisterAsync(businessModel: "creator");

        await SetGrantAsync("company", "organizationadmin", PermissionKeys.DataWrite, granted: false);

        // Simple and safe: any change moves the one version, so everyone signs in once more.
        AssertStatus(HttpStatusCode.Unauthorized, await Host.GetAsync("/v1/workspaces", tokenCookie: creator.AccessToken));
        var fresh = (await Host.LoginAsync(creator.Email)).AccessToken;
        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/workspaces", tokenCookie: fresh));
    }

    [Fact]
    public async Task ASignInWithAStaleTokenInTheBrowser_StillWorks_BecauseSignInIsAPublicEndpoint()
    {
        var admin = await Host.RegisterAsync();
        await SetGrantAsync("agency", "organizationadmin", PermissionKeys.DataWrite, granted: false);

        // The browser still holds the old cookie when the person types their password: it must not get in the way.
        var response = await Host.PostAsync(
            "/v1/auth/login", new { email = admin.Email, password = ApiTestHost.DefaultPassword }, tokenCookie: admin.AccessToken);

        AssertStatus(HttpStatusCode.OK, response);
        Assert.NotNull(ApiTestHost.MemberCookieValue(response));
    }

    [Fact]
    public async Task ATokenIssuedBeforePermissionsTraveledInTheToken_IsRefused()
    {
        var admin = await Host.RegisterAsync();
        var old = Host.Factory.CreateAccessTokenWithoutPermissions(admin.UserId, admin.AgencyId, OrganizationRole.OrganizationAdmin);

        var response = await Host.GetAsync("/v1/workspaces", old);

        AssertStatus(HttpStatusCode.Unauthorized, response);
        Assert.Equal("permissions_changed", await CodeOfAsync(response));
    }

    [Fact]
    public async Task ASiteAdminSession_IsNotAffectedByAGrantChange()
    {
        Guid userId;
        await using (var owner = await Host.Database.OpenOwnerAsync())
        {
            userId = await DbTestSupport.InsertUserAsync(owner, $"{Guid.NewGuid():N}@example.com");
            await DbTestSupport.InsertSiteAdminAsync(owner, userId);
        }

        var siteAdmin = Host.Factory.CreateSiteAdminAccessToken(userId);
        await SetGrantAsync("company", "organizationadmin", PermissionKeys.DataWrite, granted: false);

        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/site/me", siteAdmin));
    }

    // --- The token decides, not the role -----------------------------------------------------

    [Fact]
    public async Task TheTokensPermissions_Decide_NotTheRoleName()
    {
        var owner = await Host.RegisterAsync(businessModel: "company");
        var version = await CurrentVersionAsync();
        var tokens = Host.Factory;

        // A content writer whose token carries workspaces.create may create; an organization admin whose does not may not.
        var writer = tokens.CreateAccessToken(
            owner.UserId, owner.AgencyId, OrganizationRole.ContentWriter, [PermissionKeys.DashboardView, PermissionKeys.WorkspacesCreate]);
        var admin = tokens.CreateAccessToken(
            owner.UserId, owner.AgencyId, OrganizationRole.OrganizationAdmin, [PermissionKeys.DashboardView, PermissionKeys.WorkspacesView]);

        Assert.Equal(version, await CurrentVersionAsync());
        AssertStatus(HttpStatusCode.Created, await Host.PostAsync("/v1/workspaces", new { name = "By a writer" }, writer));
        AssertStatus(HttpStatusCode.Forbidden, await Host.PostAsync("/v1/workspaces", new { name = "By an admin" }, admin));
        AssertStatus(HttpStatusCode.Forbidden, await Host.GetAsync("/v1/workspaces", writer)); // no workspaces.view in that token
    }

    [Fact]
    public async Task ATokenWithEveryPermissionButARoleThisBuildDoesNotKnow_IsDenied()
    {
        var owner = await Host.RegisterAsync();
        var version = await CurrentVersionAsync();

        var known = TestAccessTokens.CreateWithRoleName(
            Host.Factory.SigningKey, owner.UserId, owner.AgencyId, "organizationadmin", PermissionKeys.All, version);
        var unknown = TestAccessTokens.CreateWithRoleName(
            Host.Factory.SigningKey, owner.UserId, owner.AgencyId, "superuser", PermissionKeys.All, version);

        // The same claims, one word apart: the permission alone is not enough, the role has to be one we know.
        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/workspaces", known));
        AssertStatus(HttpStatusCode.Forbidden, await Host.GetAsync("/v1/workspaces", unknown));
    }
}
