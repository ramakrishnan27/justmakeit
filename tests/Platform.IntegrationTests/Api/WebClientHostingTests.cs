using System.Net;
using System.Text.RegularExpressions;
using Platform.Helpers.Constants;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// The web client as the browser asks for it: the app shell, every file it pulls in, and the addresses
/// the client-side router uses. No database is involved, and no browser: the whole script graph is
/// followed through the real server, so a missing or mistyped file shows up here.
/// </summary>
public sealed partial class WebClientHostingTests : IAsyncLifetime
{
    private ApiTestHost _host = null!;

    public Task InitializeAsync()
    {
        _host = ApiTestHost.StartWithoutDatabase();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [GeneratedRegex(@"(?:href|src)=""(/[^""]+)""")]
    private static partial Regex HtmlReference();

    [GeneratedRegex("""(?:import|export)\s+(?:[^'"]*?\sfrom\s+)?['"](\.{1,2}/[^'"]+)['"]""")]
    private static partial Regex ModuleImport();

    // --- The shell and the router's addresses ------------------------------------------------

    [Theory]
    [InlineData("/")]
    [InlineData("/admin")]
    [InlineData("/admin/")]
    [InlineData("/admin/login")]
    [InlineData("/admin/anything/deeper/than/that")]
    [InlineData("/login")]
    [InlineData("/register")]
    [InlineData("/dashboard")]
    [InlineData("/dashboard/")]
    public async Task EveryClientSideAddress_AnswersWithTheAppShell_ToAVisitorWhoIsNotSignedIn(string path)
    {
        var response = await _host.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<div id=\"app\">", html);
        Assert.Contains("/js/main.js", html);
    }

    [Fact]
    public async Task TheAppShell_IsTheSameFile_AtEveryClientSideAddress()
    {
        var root = await (await _host.GetAsync("/")).Content.ReadAsStringAsync();

        Assert.Equal(root, await (await _host.GetAsync("/admin")).Content.ReadAsStringAsync());
        Assert.Equal(root, await (await _host.GetAsync("/admin/login")).Content.ReadAsStringAsync());
        Assert.Equal(root, await (await _host.GetAsync("/login")).Content.ReadAsStringAsync());
        Assert.Equal(root, await (await _host.GetAsync("/register")).Content.ReadAsStringAsync());
        Assert.Equal(root, await (await _host.GetAsync("/dashboard")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OnlyTheAppAddresses_FallBackToTheShell_SoApiTyposStillGetARealAnswer()
    {
        var typo = await _host.GetAsync("/v1/site/overviw");
        var elsewhere = await _host.GetAsync("/not-admin/login");
        var authTypo = await _host.GetAsync("/v1/auth/googl/start");
        var deeper = await _host.GetAsync("/dashboard/settings");

        Assert.NotEqual("text/html", typo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.Unauthorized, typo.StatusCode);
        Assert.NotEqual("text/html", elsewhere.Content.Headers.ContentType?.MediaType);
        Assert.NotEqual("text/html", authTypo.Content.Headers.ContentType?.MediaType);
        Assert.NotEqual("text/html", deeper.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ASignedInAgencyUser_GetsA404_ForAnUnknownApiRoute_NotTheShell()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/site/overviw");
        request.Headers.Authorization = new("Bearer", _host.Factory.CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.ClientManager));

        var response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ASiteAdminToken_GetsA403_ForAnUnknownApiRoute_BecauseItPassesNoAgencyPolicy()
    {
        // Nothing unmarked is open to a token without an agency, an unknown route included.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/site/overviw");
        request.Headers.Authorization = new("Bearer", _host.Factory.CreateSiteAdminAccessToken(Guid.NewGuid()));

        var response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Every file the page pulls in --------------------------------------------------------

    [Fact]
    public async Task TheShell_ReferencesOnlyFilesThatAreServed_WithTheRightTypes()
    {
        var html = await (await _host.GetAsync("/admin")).Content.ReadAsStringAsync();
        var references = HtmlReference().Matches(html).Select(m => m.Groups[1].Value).Where(r => !r.StartsWith("/#")).ToList();

        Assert.Contains("/css/app.css", references);
        Assert.Contains("/js/main.js", references);
        foreach (var reference in references)
        {
            var response = await _host.GetAsync(reference);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{reference} answered {(int)response.StatusCode}.");
        }

        Assert.Contains("/favicon.svg", references); // otherwise the browser asks for /favicon.ico and logs an error
        Assert.Equal("image/svg+xml", (await _host.GetAsync("/favicon.svg")).Content.Headers.ContentType?.MediaType);
        Assert.Equal("text/css", (await _host.GetAsync("/css/app.css")).Content.Headers.ContentType?.MediaType);
        Assert.Equal("text/javascript", (await _host.GetAsync("/js/main.js")).Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task EveryScriptTheClientImports_Resolves_AndIsServedAsJavaScript()
    {
        var seen = new HashSet<string>();
        var pending = new Queue<string>(["/js/main.js"]);

        while (pending.Count > 0)
        {
            var path = pending.Dequeue();
            if (!seen.Add(path))
            {
                continue;
            }

            var response = await _host.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {(int)response.StatusCode}.");
            Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);

            var source = await response.Content.ReadAsStringAsync();
            foreach (Match import in ModuleImport().Matches(source))
            {
                // Relative to the importing file, as the browser resolves it.
                var resolved = new Uri(new Uri("http://host" + path), import.Groups[1].Value).AbsolutePath;
                pending.Enqueue(resolved);
            }
        }

        // The graph is the whole client: the entry point, the core modules and both pages.
        foreach (var expected in new[]
                 {
                     "/js/main.js", "/js/core/api.js", "/js/core/session.js", "/js/core/router.js", "/js/core/jwt.js",
                     "/js/core/format.js", "/js/core/messages.js", "/js/core/dom.js",
                     "/js/pages/admin-login.js", "/js/pages/admin-dashboard.js",
                     "/js/pages/member-login.js", "/js/pages/member-register.js", "/js/pages/member-dashboard.js",
                     "/js/core/roles.js", "/js/core/google.js"
                 })
        {
            Assert.Contains(expected, seen);
        }
    }

    [Fact]
    public async Task TheClientFiles_AreOpenToAVisitorWhoIsNotSignedIn()
    {
        // The fallback authorization policy must not close the page that lets a person sign in.
        foreach (var path in new[]
                 {
                     "/", "/admin", "/admin/login", "/login", "/register", "/dashboard", "/css/app.css", "/js/main.js",
                     "/js/pages/admin-login.js", "/js/pages/member-login.js", "/js/pages/member-register.js"
                 })
        {
            Assert.Equal(HttpStatusCode.OK, (await _host.GetAsync(path)).StatusCode);
        }
    }

    [Theory]
    [InlineData("/tests/api.test.js")]
    [InlineData("/tests/helpers.js")]
    [InlineData("/TESTS/api.test.js")]
    [InlineData("/tests/")]
    public async Task TheClientTests_AreNeverServed(string path)
    {
        // web/tests holds unit tests and test doubles; they are part of the repository, not of the site.
        Assert.Equal(HttpStatusCode.NotFound, (await _host.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData(OrganizationRole.OrganizationAdmin)]
    [InlineData(OrganizationRole.ClientManager)]
    public async Task TheAppShell_IsTheSame_ForASignedInUser(OrganizationRole role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/login");
        request.Headers.Authorization = new("Bearer", _host.Factory.CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), role));

        var response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}
