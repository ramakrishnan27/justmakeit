using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Auth.Interfaces;
using Platform.Helpers.Constants;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// Ending a session, through the real pipeline and with a revocation service the test controls, so no database is
/// involved. A member token issued at or before the moment the person's access was changed is answered 401
/// <c>session_ended</c> (there is no refresh: the web client treats any 401 as the end of the session). The token may
/// arrive as the HttpOnly cookie the web pages use or as a Bearer header; both are judged the same way.
/// </summary>
public sealed class SessionRevocationMiddlewareTests : IAsyncLifetime
{
    private readonly ControllableRevocations _revocations = new();
    private ApiTestHost _host = null!;

    public Task InitializeAsync()
    {
        _host = ApiTestHost.StartWithoutDatabase(configureServices: services =>
        {
            services.RemoveAll<ISessionRevocationService>();
            services.AddSingleton<ISessionRevocationService>(_revocations);
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) =>
        (await ApiTestHost.ReadJsonAsync(response)).GetProperty("code").GetString();

    // A 400 from the controller shows the request got through every middleware and the policy.
    private Task<HttpResponseMessage> ReachController(string token, bool asCookie = false) =>
        asCookie
            ? _host.PostAsync("/v1/workspaces", new { name = "" }, tokenCookie: token)
            : _host.PostAsync("/v1/workspaces", new { name = "" }, token);

    [Fact]
    public async Task ATokenNobodyEnded_ReachesAuthorization()
    {
        var token = _host.Factory.CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.OrganizationAdmin);

        Assert.Equal(HttpStatusCode.BadRequest, (await ReachController(token)).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ATokenIssuedBeforeTheSessionWasEnded_IsAnswered401SessionEnded_FromAHeaderAndFromTheCookie(bool asCookie)
    {
        var (agency, user) = (Guid.NewGuid(), Guid.NewGuid());
        var token = _host.Factory.CreateAccessToken(user, agency, OrganizationRole.OrganizationAdmin);
        _revocations.End(agency, user, Now + 10);

        var response = await ReachController(token, asCookie);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("session_ended", await CodeOfAsync(response));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task ATokenIssuedAfterTheSessionWasEnded_IsAccepted_SoSigningInAgainWorks()
    {
        var (agency, user) = (Guid.NewGuid(), Guid.NewGuid());
        var token = _host.Factory.CreateAccessToken(user, agency, OrganizationRole.OrganizationAdmin);
        _revocations.End(agency, user, Now - 100);

        Assert.Equal(HttpStatusCode.BadRequest, (await ReachController(token)).StatusCode);
    }

    [Fact]
    public async Task OnlyThatPersonInThatOrganization_IsRefused()
    {
        var (agency, user) = (Guid.NewGuid(), Guid.NewGuid());
        _revocations.End(agency, user, Now + 10);

        var someoneElse = _host.Factory.CreateAccessToken(Guid.NewGuid(), agency, OrganizationRole.OrganizationAdmin);
        var samePersonElsewhere = _host.Factory.CreateAccessToken(user, Guid.NewGuid(), OrganizationRole.OrganizationAdmin);

        Assert.Equal(HttpStatusCode.BadRequest, (await ReachController(someoneElse)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ReachController(samePersonElsewhere)).StatusCode);
    }

    [Fact]
    public async Task ARevokedTokenIsRefusedOnEveryMemberEndpoint_NotOnlyTheOnesTheTestPicked()
    {
        var (agency, user) = (Guid.NewGuid(), Guid.NewGuid());
        var token = _host.Factory.CreateAccessToken(user, agency, OrganizationRole.OrganizationAdmin);
        _revocations.End(agency, user, Now + 10);

        foreach (var path in new[] { "/v1/auth/me", "/v1/workspaces", "/v1/members", "/v1/menu" })
        {
            var response = await _host.GetAsync(path, token);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("session_ended", await CodeOfAsync(response));
        }
    }

    [Fact]
    public async Task ATokenThatDoesNotSayWhenItWasIssued_CannotBeComparedAndIsRefused()
    {
        var token = _host.Factory.CreateAccessTokenWithoutIssuedAt(Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.OrganizationAdmin);

        var response = await ReachController(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("session_ended", await CodeOfAsync(response));
    }

    [Fact]
    public async Task ASiteAdminToken_IsNeverCheckedAgainstTheEndedSessions()
    {
        var siteAdmin = Guid.NewGuid();
        var token = _host.Factory.CreateSiteAdminAccessToken(siteAdmin);
        _revocations.ResetReads();

        // 400 from the controller: it passed the SiteAdmin policy.
        var response = await _host.GetAsync("/v1/site/agencies?page=0", token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _revocations.Reads);
    }

    [Theory]
    [InlineData("/v1/auth/login")]
    [InlineData("/v1/auth/register")]
    [InlineData("/v1/auth/logout")]
    [InlineData("/v1/auth/invitations/preview")]
    [InlineData("/v1/auth/invitations/accept")]
    public async Task APublicEndpoint_IgnoresAnEndedSession_SoSigningInAgainAlwaysWorks(string path)
    {
        var (agency, user) = (Guid.NewGuid(), Guid.NewGuid());
        var token = _host.Factory.CreateAccessToken(user, agency, OrganizationRole.OrganizationAdmin);
        _revocations.End(agency, user, Now + 10);
        _revocations.ResetReads();

        var response = await _host.PostAsync(path, new { }, tokenCookie: token);

        // Each answers for itself (400 for the empty forms, 204 for logout), never with the refusal.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            Assert.NotEqual("session_ended", await CodeOfAsync(response));
        }

        Assert.Equal(0, _revocations.Reads);
    }

    [Fact]
    public async Task ARequestWithNoToken_NeverAsksForTheEndedSessions()
    {
        var response = await _host.GetAsync("/v1/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _revocations.Reads);
    }

    [Fact]
    public async Task ATokenSignedWithAnotherKey_IsRefusedBeforeTheEndedSessionsAreAskedFor()
    {
        var forged = TestAccessTokens.Create(new string('z', 64), Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.OrganizationAdmin);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.GetAsync("/v1/workspaces", forged)).StatusCode);
        Assert.Equal(0, _revocations.Reads);
    }

    [Fact]
    public async Task WhenTheListCannotBeRead_TheRequestFails_AndIsNeverLetThrough()
    {
        var token = _host.Factory.CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.OrganizationAdmin);
        _revocations.Throws = true;

        var response = await ReachController(token);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("server_error", await CodeOfAsync(response));
    }

    private sealed class ControllableRevocations : ISessionRevocationService
    {
        private readonly Dictionary<(Guid, Guid), long> _ended = [];
        private int _reads;

        public bool Throws { get; set; }
        public int Reads => Volatile.Read(ref _reads);

        /// <summary>Ends the person's sessions: tokens issued at or before <paramref name="validAfterSeconds"/> are void.</summary>
        public void End(Guid agencyId, Guid userId, long validAfterSeconds) => _ended[(agencyId, userId)] = validAfterSeconds;

        public void ResetReads() => Volatile.Write(ref _reads, 0);

        public Task<bool> IsRevokedAsync(Guid agencyId, Guid userId, long issuedAtSeconds, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _reads);
            return Throws
                ? throw new InvalidOperationException("The database is not reachable.")
                : Task.FromResult(_ended.TryGetValue((agencyId, userId), out var after) && issuedAtSeconds <= after);
        }

        public void Invalidate()
        {
        }
    }
}
