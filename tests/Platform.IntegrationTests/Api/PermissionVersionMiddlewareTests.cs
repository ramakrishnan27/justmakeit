using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Helpers.Constants;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// The permission version check, through the real pipeline and with a permission service the test
/// controls, so no database is involved. A member token issued under another version (or with none)
/// is answered 401 so the person signs in again (there is no refresh); nothing else about the token is looked at first.
/// The token may arrive as the HttpOnly cookie the web pages use or as a Bearer header; both are checked the same way.
/// </summary>
public sealed class PermissionVersionMiddlewareTests : IAsyncLifetime
{
    private readonly ControllablePermissionService _permissions = new();
    private ApiTestHost _host = null!;

    public Task InitializeAsync()
    {
        _host = ApiTestHost.StartWithoutDatabase(configureServices: services =>
        {
            services.RemoveAll<IPermissionService>();
            services.AddSingleton<IPermissionService>(_permissions);
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private string Token(OrganizationRole role = OrganizationRole.OrganizationAdmin) =>
        _host.Factory.CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), role);

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        (await ApiTestHost.ReadJsonAsync(response)).GetProperty("code").GetString();

    [Fact]
    public async Task ATokenIssuedUnderTheCurrentVersion_ReachesAuthorization()
    {
        // A body the controller refuses with 400 shows the request got through the version check and the policy.
        var response = await _host.PostAsync("/v1/workspaces", new { name = "" }, Token());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ATokenIssuedUnderAnOlderVersion_IsAnswered401PermissionsChanged_BeforeAnyPolicyRuns()
    {
        _permissions.Version = 5;
        var stale = _host.Factory.CreateAccessTokenWithVersion(Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.OrganizationAdmin, 4);

        var response = await _host.GetAsync("/v1/workspaces", stale);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("permissions_changed", await CodeOfAsync(response));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task AStaleTokenInTheCookie_IsAnswered401PermissionsChanged_JustLikeOneInAHeader()
    {
        _permissions.Version = 5;
        var stale = _host.Factory.CreateAccessTokenWithVersion(Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.OrganizationAdmin, 4);

        var response = await _host.GetAsync("/v1/workspaces", tokenCookie: stale);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("permissions_changed", await CodeOfAsync(response));
    }

    [Fact]
    public async Task ACurrentTokenInTheCookie_ReachesAuthorization()
    {
        var response = await _host.PostAsync("/v1/workspaces", new { name = "" }, tokenCookie: Token());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ATokenIssuedUnderANewerVersion_IsRefusedToo_NotOnlyAnOlderOne()
    {
        _permissions.Version = 5;
        var newer = _host.Factory.CreateAccessTokenWithVersion(Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.OrganizationAdmin, 6);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.GetAsync("/v1/workspaces", newer)).StatusCode);
    }

    [Fact]
    public async Task ATokenWithNoPermissionsAndNoVersion_ThatWasIssuedBeforeThisFeature_IsAnswered401()
    {
        var old = _host.Factory.CreateAccessTokenWithoutPermissions(Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.OrganizationAdmin);

        var response = await _host.GetAsync("/v1/auth/me", old);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("permissions_changed", await CodeOfAsync(response));
    }

    [Fact]
    public async Task ARaisedVersion_MakesEveryTokenOfTheOldOneStale_AtOnce()
    {
        var token = Token();
        Assert.Equal(HttpStatusCode.BadRequest, (await _host.PostAsync("/v1/workspaces", new { name = "" }, token)).StatusCode);

        _permissions.Version++; // what a grant change does

        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.PostAsync("/v1/workspaces", new { name = "" }, token)).StatusCode);
    }

    [Fact]
    public async Task AStaleToken_IsToldToSignInAgain_NotDeniedForPermissionsItNeverWasJudgedBy()
    {
        // A content writer cannot create a workspace (403 with a current token); with a stale one the answer is 401, not 403.
        _permissions.Version = 3;
        var stale = _host.Factory.CreateAccessTokenWithVersion(Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.ContentWriter, 2);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.PostAsync("/v1/workspaces", new { name = "x" }, stale)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _host.PostAsync("/v1/workspaces", new { name = "x" }, Token(OrganizationRole.ContentWriter))).StatusCode);
    }

    [Fact]
    public async Task ASiteAdminToken_HasNoVersion_AndIsNeverCheckedAgainstOne()
    {
        _permissions.Version = 99;
        var siteAdmin = _host.Factory.CreateSiteAdminAccessToken(Guid.NewGuid());
        _permissions.ResetReads();

        // 400 from the controller: it passed the version check and the SiteAdmin policy.
        var response = await _host.GetAsync("/v1/site/agencies?page=0", siteAdmin);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _permissions.VersionReads);
    }

    [Theory]
    [InlineData("/v1/auth/login")]
    [InlineData("/v1/auth/register")]
    [InlineData("/v1/auth/logout")]
    public async Task APublicEndpoint_IgnoresAStaleToken_SoSigningInAgainAlwaysWorks(string path)
    {
        _permissions.Version = 5;
        var stale = _host.Factory.CreateAccessTokenWithVersion(Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.OrganizationAdmin, 1);
        _permissions.ResetReads();

        var response = await _host.PostAsync(path, new { }, tokenCookie: stale);

        // Each answers for itself (400 for the empty forms, 204 for logout), never with the version refusal.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            Assert.NotEqual("permissions_changed", await CodeOfAsync(response));
        }

        Assert.Equal(0, _permissions.VersionReads);
    }

    [Fact]
    public async Task ARequestWithNoToken_NeverAsksForTheVersion()
    {
        var response = await _host.GetAsync("/v1/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _permissions.VersionReads);
    }

    [Fact]
    public async Task ATokenSignedWithAnotherKey_IsRefusedBeforeTheVersionIsAskedFor()
    {
        var forged = TestAccessTokens.Create(new string('z', 64), Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.OrganizationAdmin);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.GetAsync("/v1/workspaces", forged)).StatusCode);
        Assert.Equal(0, _permissions.VersionReads);
    }

    [Fact]
    public async Task WhenTheCurrentVersionCannotBeRead_TheRequestFails_AndIsNeverLetThrough()
    {
        var token = Token();
        _permissions.Throws = true;

        var response = await _host.PostAsync("/v1/workspaces", new { name = "" }, token);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("server_error", await CodeOfAsync(response));
    }

    private sealed class ControllablePermissionService : IPermissionService
    {
        private int _versionReads;

        public long Version { get; set; } = DefaultGrantsPermissionService.Version;
        public bool Throws { get; set; }
        public int VersionReads => Volatile.Read(ref _versionReads);

        /// <summary>Forgets the reads so far: minting a test token asks for the version too.</summary>
        public void ResetReads() => Volatile.Write(ref _versionReads, 0);

        public Task<PermissionGrants> GetGrantsAsync(BusinessModel businessModel, OrganizationRole role, CancellationToken cancellationToken) =>
            Task.FromResult(new PermissionGrants(Version, DefaultGrantsPermissionService.DefaultKeys(role)));

        public Task<long> GetVersionAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _versionReads);
            return Throws ? throw new InvalidOperationException("The database is not reachable.") : Task.FromResult(Version);
        }

        public void Invalidate()
        {
        }
    }
}
