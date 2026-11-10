using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// Who may reach the Platform endpoints, decided by the permission in the token alone (no database: the service is a
/// recording fake). Every role holds <c>platforms.view</c> by default; the Site admin can take it away, and then the endpoint
/// answers 403 whatever else the token holds. Also the headers: the list is never kept, the icon is served as the image it is.
/// </summary>
public sealed class PlatformsEndpointTests : IAsyncLifetime
{
    private static readonly Guid WithImage = Guid.NewGuid();
    private static readonly Guid BuiltIn = Guid.NewGuid();

    private readonly RecordingPlatformService _platforms = new();
    private ApiTestHost _host = null!;

    public Task InitializeAsync()
    {
        _host = ApiTestHost.StartWithoutDatabase(configureServices: services =>
        {
            services.RemoveAll<IPlatformService>();
            services.AddSingleton<IPlatformService>(_platforms);
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private string Token(OrganizationRole role, IEnumerable<string>? permissions = null) =>
        _host.Factory.CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), role, permissions);

    private async Task<HttpResponseMessage> GetAsync(string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new("Bearer", token);
        }

        return await _host.Client.SendAsync(request);
    }

    public static TheoryData<string> Paths() => ["/v1/platforms", $"/v1/platforms/{WithImage}/icon"];

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task WithoutASignIn_EveryEndpointIsAnswered401(string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(path, null)).StatusCode);
        Assert.Equal(0, _platforms.Calls);
    }

    [Theory]
    [InlineData(OrganizationRole.OrganizationAdmin)]
    [InlineData(OrganizationRole.ClientManager)]
    [InlineData(OrganizationRole.MarketingManager)]
    [InlineData(OrganizationRole.ContentWriter)]
    public async Task EveryRole_ReachesTheList_ByDefault(OrganizationRole role)
    {
        var response = await GetAsync("/v1/platforms", Token(role));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _platforms.Calls);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task WithoutThePermission_ItIsAnswered403_EvenForAnOrganizationAdminWithEverythingElse(string path)
    {
        var withoutIt = PermissionKeys.All.Where(key => key != PermissionKeys.PlatformsView);

        var response = await GetAsync(path, Token(OrganizationRole.OrganizationAdmin, withoutIt));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, _platforms.Calls);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task ThePermission_NotTheRole_Decides(string path)
    {
        var response = await GetAsync(path, Token(OrganizationRole.ClientManager, [PermissionKeys.DashboardView, PermissionKeys.PlatformsView]));

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task ASiteAdminToken_OpensNothingHere(string path)
    {
        var response = await GetAsync(path, _host.Factory.CreateSiteAdminAccessToken(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, _platforms.Calls);
    }

    [Fact]
    public async Task OtherPermissions_DoNotStandIn()
    {
        var others = new[] { PermissionKeys.DashboardView, PermissionKeys.WorkspacesView, PermissionKeys.DataRead, PermissionKeys.OrganizationManage };

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/v1/platforms", Token(OrganizationRole.OrganizationAdmin, others))).StatusCode);
    }

    [Fact]
    public async Task TheList_HasTheFieldsThePageNeeds_AndIsNeverKept()
    {
        var response = await GetAsync("/v1/platforms", Token(OrganizationRole.ContentWriter));

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
        var body = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal(2, body.GetArrayLength());
        var first = body[0];
        Assert.Equal("Instagram", first.GetProperty("name").GetString());
        Assert.Equal("instagram", first.GetProperty("iconKey").GetString());
        Assert.False(first.GetProperty("hasIconImage").GetBoolean());
        Assert.True(first.GetProperty("isEnabled").GetBoolean());
        Assert.True(body[1].GetProperty("hasIconImage").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body[1].GetProperty("iconKey").ValueKind);

        // No creator, no counts, no image bytes: a member sees none of what the Site admin list holds.
        var names = body[0].EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(["description", "hasIconImage", "iconKey", "id", "isEnabled", "name"], names);
    }

    [Fact]
    public async Task TheIcon_IsServedAsTheStoredImage_WithNosniff_AndMayBeKeptPrivately()
    {
        var response = await GetAsync($"/v1/platforms/{WithImage}/icon", Token(OrganizationRole.ContentWriter));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(RecordingPlatformService.Png, await response.Content.ReadAsByteArrayAsync());
        var cache = response.Headers.CacheControl?.ToString() ?? string.Empty;
        Assert.Contains("private", cache);
        Assert.Contains("max-age=3600", cache);
    }

    [Fact]
    public async Task TheIcon_OfAPlatformWithABuiltInIcon_IsAnswered404()
    {
        var response = await GetAsync($"/v1/platforms/{BuiltIn}/icon", Token(OrganizationRole.ContentWriter));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("platform_not_found", (await ApiTestHost.ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    private sealed class RecordingPlatformService : IPlatformService
    {
        public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

        public int Calls { get; private set; }

        public Task<IReadOnlyList<PlatformResponse>> ListAsync(CancellationToken cancellationToken)
        {
            Calls++;
            IReadOnlyList<PlatformResponse> list =
            [
                new(Guid.NewGuid(), "Instagram", "Photos", "instagram", false, true),
                new(Guid.NewGuid(), "Telegram", "Chat", null, true, false)
            ];
            return Task.FromResult(list);
        }

        public Task<ChannelIconRecord> GetIconAsync(Guid channelId, CancellationToken cancellationToken)
        {
            Calls++;
            return channelId == WithImage
                ? Task.FromResult(new ChannelIconRecord { ContentType = "image/png", Image = Png })
                : throw new NotFoundException("That platform has no uploaded icon.", "platform_not_found");
        }
    }
}
