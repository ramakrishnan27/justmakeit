using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

/// <summary>A signed-in user as a test sees them: the credentials and the two tokens a login produced.</summary>
public sealed record Session(
    string Email, string Password, string AccessToken, string RefreshToken, Guid UserId, Guid AgencyId);

/// <summary>
/// The API hosted in memory, with a client that does no cookie handling of its own: the refresh
/// cookie is Secure and scoped to /v1/auth, so a test reads and sends it by hand, which also lets it
/// replay an old one.
/// </summary>
public sealed class ApiTestHost : IAsyncDisposable
{
    public const string DefaultPassword = "a long enough password";
    public const string AgencyCookieName = "refresh_token";
    public const string SiteCookieName = "site_refresh_token";

    private readonly TestDatabase? _database;
    private int _counter;

    private ApiTestHost(ApiHostFactory factory, TestDatabase? database)
    {
        Factory = factory;
        _database = database;
        Client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public ApiHostFactory Factory { get; }

    public HttpClient Client { get; }

    public TestDatabase Database => _database ?? throw new InvalidOperationException("This host has no database.");

    /// <summary>A host over a fresh, migrated database. The API connects as app_user, as it does in production.</summary>
    public static async Task<ApiTestHost> StartWithDatabaseAsync(
        PostgresFixture postgres, Action<Dictionary<string, string?>, TestDatabase>? configure = null)
    {
        var database = await postgres.CreateDatabaseAsync();
        var settings = ApiHostFactory.ValidSettings();
        settings["ConnectionStrings:App"] = database.AppConnectionString;
        settings["RateLimiting:Auth:CredentialRequestsPerWindow"] = "100000";
        settings["RateLimiting:Auth:SessionRequestsPerWindow"] = "100000";
        configure?.Invoke(settings, database);
        return new ApiTestHost(new ApiHostFactory(settings), database);
    }

    /// <summary>A host that cannot reach any database: for rules that are decided before a query runs.</summary>
    public static ApiTestHost StartWithoutDatabase(Action<Dictionary<string, string?>>? configure = null)
    {
        var settings = ApiHostFactory.ValidSettings();
        configure?.Invoke(settings);
        return new ApiTestHost(new ApiHostFactory(settings), null);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    public Task<HttpResponseMessage> GetAsync(string path, string? accessToken = null, string? remoteIp = null) =>
        SendAsync(HttpMethod.Get, path, null, accessToken, null, remoteIp);

    /// <param name="cookieName">The cookie to send <paramref name="refreshCookie"/> as: the agency one by default, or <see cref="SiteCookieName"/>.</param>
    public Task<HttpResponseMessage> PostAsync(
        string path,
        object? body = null,
        string? accessToken = null,
        string? refreshCookie = null,
        string? remoteIp = null,
        string cookieName = AgencyCookieName) =>
        SendAsync(HttpMethod.Post, path, body, accessToken, refreshCookie, remoteIp, cookieName);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        string? accessToken,
        string? refreshCookie,
        string? remoteIp,
        string cookieName = AgencyCookieName)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (refreshCookie is not null)
        {
            request.Headers.Add("Cookie", $"{cookieName}={refreshCookie}");
        }

        if (remoteIp is not null)
        {
            request.Headers.Add(ApiHostFactory.RemoteIpHeader, remoteIp);
        }

        return await Client.SendAsync(request);
    }

    /// <summary>Registers a new user and agency through the API, then reads their ids back with /me.</summary>
    public async Task<Session> RegisterAsync(string? email = null, string agencyName = "Acme Agency", string password = DefaultPassword)
    {
        email ??= $"user{Interlocked.Increment(ref _counter)}-{Guid.NewGuid():N}@example.com";

        var response = await PostAsync("/v1/auth/register", new { email, password, fullName = "Test User", agencyName });
        response.EnsureSuccessStatusCode();
        return await ToSessionAsync(response, email, password);
    }

    public async Task<Session> LoginAsync(string email, string password = DefaultPassword, Guid? agencyId = null)
    {
        var response = await PostAsync("/v1/auth/login", new { email, password, agencyId });
        response.EnsureSuccessStatusCode();
        return await ToSessionAsync(response, email, password);
    }

    private async Task<Session> ToSessionAsync(HttpResponseMessage response, string email, string password)
    {
        var accessToken = (await ReadJsonAsync(response)).GetProperty("accessToken").GetString()!;
        var refreshToken = RefreshCookieValue(response)!;

        var me = await ReadJsonAsync(await GetAsync("/v1/auth/me", accessToken));
        return new Session(
            email,
            password,
            accessToken,
            refreshToken,
            me.GetProperty("user").GetProperty("id").GetGuid(),
            me.GetProperty("agency").GetProperty("id").GetGuid());
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    /// <summary>The raw Set-Cookie header for the agency refresh token, or null when the response sets none.</summary>
    public static string? RefreshCookieHeader(HttpResponseMessage response) => CookieHeader(response, AgencyCookieName);

    /// <summary>The agency refresh token value the response put in its cookie, or null when there is none.</summary>
    public static string? RefreshCookieValue(HttpResponseMessage response) => CookieValue(response, AgencyCookieName);

    /// <summary>The raw Set-Cookie header for the site admin refresh token, or null when the response sets none.</summary>
    public static string? SiteCookieHeader(HttpResponseMessage response) => CookieHeader(response, SiteCookieName);

    /// <summary>The site admin refresh token value the response put in its cookie, or null when there is none.</summary>
    public static string? SiteCookieValue(HttpResponseMessage response) => CookieValue(response, SiteCookieName);

    private static string? CookieHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(value => value.StartsWith(name + "=", StringComparison.Ordinal))
            : null;

    private static string? CookieValue(HttpResponseMessage response, string name)
    {
        var header = CookieHeader(response, name);
        if (header is null)
        {
            return null;
        }

        var value = header[(name.Length + 1)..].Split(';')[0];
        return value.Length == 0 ? null : value;
    }
}
