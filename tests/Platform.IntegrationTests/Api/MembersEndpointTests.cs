using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Helpers.Constants;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// Who may reach the team endpoints, decided by the permission in the token alone (no database: the team service is a
/// recording fake). The role never decides, only <c>members.manage</c>, which only the organization admin of a company or
/// an agency holds by default. Also the shape of the answers: no-store, because one of them carries an invitation secret.
/// </summary>
public sealed class MembersEndpointTests : IAsyncLifetime
{
    private static readonly Guid SomeInvitation = Guid.NewGuid();
    private static readonly Guid SomeMember = Guid.NewGuid();

    private readonly RecordingTeamService _team = new();
    private ApiTestHost _host = null!;

    public Task InitializeAsync()
    {
        _host = ApiTestHost.StartWithoutDatabase(configureServices: services =>
        {
            services.RemoveAll<ITeamService>();
            services.AddSingleton<ITeamService>(_team);
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private string Token(OrganizationRole role, IEnumerable<string>? permissions = null) =>
        _host.Factory.CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), role, permissions);

    private static readonly (string Name, HttpMethod Method, string Path, object? Body)[] Endpoints =
    [
        ("list the team", HttpMethod.Get, "/v1/members", null),
        ("invite", HttpMethod.Post, "/v1/members/invitations", new { email = "new@example.com", role = "contentwriter" }),
        ("revoke an invitation", HttpMethod.Delete, $"/v1/members/invitations/{SomeInvitation}", null),
        ("change a role", HttpMethod.Put, $"/v1/members/{SomeMember}/role", new { role = "contentwriter" }),
        ("remove a member", HttpMethod.Delete, $"/v1/members/{SomeMember}", null)
    ];

    public static TheoryData<string> EndpointNames() => [.. Endpoints.Select(e => e.Name)];

    private async Task<HttpResponseMessage> CallAsync(string name, string? token)
    {
        var (_, method, path, body) = Endpoints.Single(e => e.Name == name);
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = System.Net.Http.Json.JsonContent.Create(body);
        }

        if (token is not null)
        {
            request.Headers.Authorization = new("Bearer", token);
        }

        return await _host.Client.SendAsync(request);
    }

    // --- Who ---------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(EndpointNames))]
    public async Task WithoutASignIn_EveryEndpointIsAnswered401(string name)
    {
        var response = await CallAsync(name, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_team.Calls);
    }

    [Theory]
    [MemberData(nameof(EndpointNames))]
    public async Task TheOrganizationAdmin_ReachesEveryEndpoint(string name)
    {
        var response = await CallAsync(name, Token(OrganizationRole.OrganizationAdmin));

        Assert.True(response.IsSuccessStatusCode, $"{name} answered {(int)response.StatusCode}.");
        Assert.Single(_team.Calls);
    }

    [Theory]
    [InlineData(OrganizationRole.ClientManager)]
    [InlineData(OrganizationRole.MarketingManager)]
    [InlineData(OrganizationRole.ContentWriter)]
    public async Task EveryOtherRole_IsAnswered403_OnEveryEndpoint_AndTheServiceIsNeverCalled(OrganizationRole role)
    {
        foreach (var (name, _, _, _) in Endpoints)
        {
            var response = await CallAsync(name, Token(role));

            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{name} as {role} answered {(int)response.StatusCode}.");
        }

        Assert.Empty(_team.Calls);
    }

    [Theory]
    [MemberData(nameof(EndpointNames))]
    public async Task ThePermission_NotTheRole_Decides(string name)
    {
        // A content writer the Site admin gave the permission to ...
        var granted = await CallAsync(name, Token(OrganizationRole.ContentWriter, [PermissionKeys.DashboardView, PermissionKeys.MembersManage]));
        Assert.True(granted.IsSuccessStatusCode, $"{name} answered {(int)granted.StatusCode}.");

        // ... and an organization admin who lost it, even with everything else the catalog has.
        var withoutIt = PermissionKeys.All.Where(key => key != PermissionKeys.MembersManage);
        var revoked = await CallAsync(name, Token(OrganizationRole.OrganizationAdmin, withoutIt));
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }

    [Theory]
    [MemberData(nameof(EndpointNames))]
    public async Task ASiteAdminToken_OpensNothingHere(string name)
    {
        var response = await CallAsync(name, _host.Factory.CreateSiteAdminAccessToken(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_team.Calls);
    }

    [Fact]
    public async Task OrganizationManage_DoesNotStandInForTheTeamPermission()
    {
        var onlyThat = Token(OrganizationRole.OrganizationAdmin, [PermissionKeys.DashboardView, PermissionKeys.OrganizationManage]);

        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync("invite", onlyThat)).StatusCode);
    }

    // --- The answers -------------------------------------------------------------------------

    [Fact]
    public async Task Inviting_Answers201WithTheSecret_AndNeverLetsAnythingKeepIt()
    {
        var response = await CallAsync("invite", Token(OrganizationRole.OrganizationAdmin));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal(RecordingTeamService.Secret, body.GetProperty("token").GetString());
        Assert.Equal("contentwriter", body.GetProperty("invitation").GetProperty("role").GetString());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
        Assert.Equal("new@example.com", _team.LastInvitedEmail);
    }

    [Theory]
    [MemberData(nameof(EndpointNames))]
    public async Task EveryAnswer_IsMarkedNoStore(string name)
    {
        var response = await CallAsync(name, Token(OrganizationRole.OrganizationAdmin));

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task TheTeamList_HasMembersAndInvitations_AndNoSecret()
    {
        var response = await CallAsync("list the team", Token(OrganizationRole.OrganizationAdmin));

        var body = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal("organizationadmin", body.GetProperty("members")[0].GetProperty("role").GetString());
        Assert.True(body.GetProperty("members")[0].GetProperty("isYou").GetBoolean());
        Assert.Equal(0, body.GetProperty("invitations").GetArrayLength());
        Assert.DoesNotContain("token", (await response.Content.ReadAsStringAsync()), StringComparison.OrdinalIgnoreCase);
    }

    // --- The request -------------------------------------------------------------------------

    [Theory]
    [InlineData("{}", "email")]
    [InlineData("""{"email":"new@example.com"}""", "role")]
    [InlineData("""{"email":"new@example.com","role":"boss"}""", "role")]
    [InlineData("""{"email":"nope","role":"contentwriter"}""", "email")]
    public async Task AnInviteWithABadBody_Is400WithTheField_AndTheServiceIsNotCalled(string json, string field)
    {
        var response = await Send(HttpMethod.Post, "/v1/members/invitations", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out _), $"No error for '{field}'.");
        Assert.Empty(_team.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"role":"owner"}""")]
    public async Task AChangeOfRoleWithABadBody_Is400_AndTheServiceIsNotCalled(string json)
    {
        var response = await Send(HttpMethod.Put, $"/v1/members/{SomeMember}/role", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_team.Calls);
    }

    [Theory]
    [InlineData("DELETE", "/v1/members/not-a-guid")]
    [InlineData("DELETE", "/v1/members/invitations/not-a-guid")]
    [InlineData("PUT", "/v1/members/not-a-guid/role")]
    public async Task AnIdThatIsNotAGuid_IsNotARoute(string method, string path)
    {
        var response = await Send(new HttpMethod(method), path, """{"role":"contentwriter"}""");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_team.Calls);
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string json)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new("Bearer", Token(OrganizationRole.OrganizationAdmin));
        return await _host.Client.SendAsync(request);
    }

    // --- The invitation page's endpoints: public, and shaped like the rest of /v1/auth -------

    [Theory]
    [InlineData("/v1/auth/invitations/preview")]
    [InlineData("/v1/auth/invitations/accept")]
    public async Task ThePublicInvitationEndpoints_NeedNoSignIn_AndRefuseAnEmptyForm(string path)
    {
        var response = await _host.PostAsync(path, new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("token", out _));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }

    [Theory]
    [InlineData("/v1/auth/invitations/preview")]
    [InlineData("/v1/auth/invitations/accept")]
    public async Task ThePublicInvitationEndpoints_TakeThePostOnly_SoTheSecretIsNeverInAnAddress(string path)
    {
        // A GET is not served. An anonymous caller is told 401, because the authorization fallback closes the
        // "method not allowed" answer as it closes any unknown route; a signed-in one is told 405. Either way nothing
        // is looked up, so a link with the secret in the address does nothing.
        var anonymous = await _host.GetAsync(path + "?token=abc");
        var signedIn = await _host.GetAsync(path + "?token=abc", Token(OrganizationRole.OrganizationAdmin));

        Assert.True(anonymous.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.MethodNotAllowed, $"Answered {(int)anonymous.StatusCode}.");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, signedIn.StatusCode);
        Assert.Empty(_team.Calls);
    }

    private sealed class RecordingTeamService : ITeamService
    {
        public const string Secret = "the-secret-of-the-link";

        private readonly List<string> _calls = [];

        public IReadOnlyList<string> Calls => _calls;
        public string? LastInvitedEmail { get; private set; }

        public Task<TeamResponse> GetTeamAsync(CancellationToken cancellationToken)
        {
            _calls.Add("list");
            var me = new TeamMemberResponse(Guid.NewGuid(), "me@example.com", "Me", OrganizationRole.OrganizationAdmin, DateTimeOffset.UtcNow, IsYou: true);
            return Task.FromResult(new TeamResponse([me], []));
        }

        public Task<CreatedInvitationResponse> InviteAsync(InviteMemberRequest request, CancellationToken cancellationToken)
        {
            _calls.Add("invite");
            LastInvitedEmail = request.Email;
            var now = DateTimeOffset.UtcNow;
            var invitation = new InvitationResponse(Guid.NewGuid(), request.Email!, OrganizationRole.ContentWriter, now, now.AddDays(7), IsExpired: false);
            return Task.FromResult(new CreatedInvitationResponse(invitation, Secret));
        }

        public Task RevokeInvitationAsync(Guid invitationId, CancellationToken cancellationToken)
        {
            _calls.Add("revoke");
            return Task.CompletedTask;
        }

        public Task ChangeRoleAsync(Guid memberUserId, ChangeMemberRoleRequest request, CancellationToken cancellationToken)
        {
            _calls.Add("change");
            return Task.CompletedTask;
        }

        public Task RemoveMemberAsync(Guid memberUserId, CancellationToken cancellationToken)
        {
            _calls.Add("remove");
            return Task.CompletedTask;
        }
    }
}
