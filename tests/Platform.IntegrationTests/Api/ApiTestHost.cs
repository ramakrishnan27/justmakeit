using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

/// <summary>A signed-in user as a test sees them: the credentials and the access token a login put in its cookie.</summary>
public sealed record Session(string Email, string Password, string AccessToken, Guid UserId, Guid AgencyId);

/// <summary>
/// The API hosted in memory, with a client that does no cookie handling of its own: the access token
/// cookie is Secure and scoped to a path, so a test reads and sends it by hand. A test may also send the
/// same token as an <c>Authorization: Bearer</c> header, which the API still accepts.
/// </summary>
public sealed class ApiTestHost : IAsyncDisposable
{
    public const string DefaultPassword = "a long enough password";
    public const string MemberCookieName = "access_token";
    public const string SiteCookieName = "site_access_token";

    private readonly TestDatabase? _database;
    private int _counter;

    private ApiTestHost(ApiHostFactory factory, TestDatabase? database)
    {
        Factory = factory;
        _database = database;
        // No cookies and no redirects: a test reads Set-Cookie and Location itself, and a redirect to Google or to
        // a page of the app must be seen, not followed.
        Client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
    }

    public ApiHostFactory Factory { get; }

    public HttpClient Client { get; }

    public TestDatabase Database => _database ?? throw new InvalidOperationException("This host has no database.");

    /// <summary>A host over a fresh, migrated database. The API connects as app_user, as it does in production.</summary>
    public static async Task<ApiTestHost> StartWithDatabaseAsync(
        PostgresFixture postgres,
        Action<Dictionary<string, string?>, TestDatabase>? configure = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var database = await postgres.CreateDatabaseAsync();
        var settings = ApiHostFactory.ValidSettings();
        settings["ConnectionStrings:App"] = database.AppConnectionString;
        settings["RateLimiting:Auth:CredentialRequestsPerWindow"] = "100000";
        settings["RateLimiting:Auth:SessionRequestsPerWindow"] = "100000";
        configure?.Invoke(settings, database);
        return new ApiTestHost(new ApiHostFactory(settings, configureServices: configureServices), database);
    }

    /// <summary>A host that cannot reach any database: for rules that are decided before a query runs.</summary>
    public static ApiTestHost StartWithoutDatabase(
        Action<Dictionary<string, string?>>? configure = null, Action<IServiceCollection>? configureServices = null)
    {
        var settings = ApiHostFactory.ValidSettings();
        configure?.Invoke(settings);
        return new ApiTestHost(new ApiHostFactory(settings, configureServices: configureServices), null);
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

    /// <param name="accessToken">Sent as an <c>Authorization: Bearer</c> header.</param>
    /// <param name="tokenCookie">Sent as the access token cookie named <paramref name="cookieName"/>, the way a browser carries it.</param>
    /// <param name="cookieName">The cookie to send <paramref name="tokenCookie"/> as: the member one by default, or <see cref="SiteCookieName"/>.</param>
    public Task<HttpResponseMessage> GetAsync(
        string path,
        string? accessToken = null,
        string? remoteIp = null,
        string? tokenCookie = null,
        string cookieName = MemberCookieName) =>
        SendAsync(HttpMethod.Get, path, null, accessToken, tokenCookie, remoteIp, cookieName);

    /// <param name="cookieName">The cookie to send <paramref name="tokenCookie"/> as: the member one by default, or <see cref="SiteCookieName"/>.</param>
    public Task<HttpResponseMessage> PostAsync(
        string path,
        object? body = null,
        string? accessToken = null,
        string? tokenCookie = null,
        string? remoteIp = null,
        string cookieName = MemberCookieName) =>
        SendAsync(HttpMethod.Post, path, body, accessToken, tokenCookie, remoteIp, cookieName);

    /// <summary>A GET that carries several cookies at once, the way a browser sends both sessions' cookies to /v1/site.</summary>
    public async Task<HttpResponseMessage> GetWithCookiesAsync(string path, params (string Name, string Value)[] cookies)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}")));
        return await Client.SendAsync(request);
    }

    public Task<HttpResponseMessage> PutAsync(string path, object? body = null, string? accessToken = null) =>
        SendAsync(HttpMethod.Put, path, body, accessToken, null, null);

    public Task<HttpResponseMessage> DeleteAsync(string path, string? accessToken = null) =>
        SendAsync(HttpMethod.Delete, path, null, accessToken, null, null);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        string? accessToken,
        string? tokenCookie,
        string? remoteIp,
        string cookieName = MemberCookieName)
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

        if (tokenCookie is not null)
        {
            request.Headers.Add("Cookie", $"{cookieName}={tokenCookie}");
        }

        if (remoteIp is not null)
        {
            request.Headers.Add(ApiHostFactory.RemoteIpHeader, remoteIp);
        }

        return await Client.SendAsync(request);
    }

    /// <summary>
    /// Registers a new user and organization through the API, then reads their ids back with /me.
    /// <paramref name="agencyName"/> is the organization name; <paramref name="businessModel"/> is company, agency or creator.
    /// </summary>
    public async Task<Session> RegisterAsync(
        string? email = null, string agencyName = "Acme Agency", string password = DefaultPassword, string businessModel = "agency")
    {
        email ??= $"user{Interlocked.Increment(ref _counter)}-{Guid.NewGuid():N}@example.com";

        var response = await PostAsync(
            "/v1/auth/register", new { email, password, fullName = "Test User", organizationName = agencyName, businessModel });
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
        // The token is in the cookie, never in the body.
        var accessToken = MemberCookieValue(response)!;

        var meResponse = await GetAsync("/v1/auth/me", tokenCookie: accessToken);
        if (!meResponse.IsSuccessStatusCode)
        {
            // Say what the server answered: a bare KeyNotFoundException from the parsing below hides the reason.
            throw new InvalidOperationException(
                $"/v1/auth/me answered {(int)meResponse.StatusCode} right after sign-in: {await meResponse.Content.ReadAsStringAsync()}");
        }

        var me = await ReadJsonAsync(meResponse);
        return new Session(
            email,
            password,
            accessToken,
            me.GetProperty("user").GetProperty("id").GetGuid(),
            me.GetProperty("agency").GetProperty("id").GetGuid());
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    /// <summary>The raw Set-Cookie header for the member access token, or null when the response sets none.</summary>
    public static string? MemberCookieHeader(HttpResponseMessage response) => CookieHeader(response, MemberCookieName);

    /// <summary>The member access token the response put in its cookie, or null when there is none or it was cleared.</summary>
    public static string? MemberCookieValue(HttpResponseMessage response) => CookieValue(response, MemberCookieName);

    /// <summary>The raw Set-Cookie header for the site admin access token, or null when the response sets none.</summary>
    public static string? SiteCookieHeader(HttpResponseMessage response) => CookieHeader(response, SiteCookieName);

    /// <summary>The site admin access token the response put in its cookie, or null when there is none or it was cleared.</summary>
    public static string? SiteCookieValue(HttpResponseMessage response) => CookieValue(response, SiteCookieName);

    /// <summary>The raw Set-Cookie header for any cookie by name, or null when the response sets none.</summary>
    public static string? CookieHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(value => value.StartsWith(name + "=", StringComparison.Ordinal))
            : null;

    /// <summary>When the cookie the response sets expires (its <c>Expires</c> attribute), or null when it has none.</summary>
    public static DateTimeOffset? CookieExpires(HttpResponseMessage response, string name)
    {
        var attribute = CookieHeader(response, name)?
            .Split(';')
            .Select(part => part.Trim())
            .FirstOrDefault(part => part.StartsWith("expires=", StringComparison.OrdinalIgnoreCase));
        return attribute is null
            ? null
            : DateTimeOffset.Parse(
                attribute["expires=".Length..], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal);
    }

    /// <summary>The value of any cookie the response sets, or null when there is none or it was cleared.</summary>
    public static string? CookieValue(HttpResponseMessage response, string name)
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
