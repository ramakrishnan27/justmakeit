using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Auth.Interfaces;
using Platform.Helpers.Constants;
using Platform.Helpers.Utilities;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// Hosts Platform.Api in memory with exactly the configuration a test supplies. Production is the
/// default environment: it skips user-secrets and Swagger, so a developer's local secrets can never
/// change what a test sees, and it does not migrate on start unless a test asks for that.
/// </summary>
public sealed class ApiHostFactory(
    IReadOnlyDictionary<string, string?> settings,
    string environment = "Production",
    Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    /// <summary>A database address that refuses connections at once, for tests that must not reach a database.</summary>
    public const string UnreachableConnectionString = "Host=127.0.0.1;Port=1;Database=none;Username=app_user;Password=none;Timeout=3";

    public static Dictionary<string, string?> ValidSettings() => new()
    {
        ["Jwt:SigningKey"] = TokenGenerator.Create(48),
        ["ConnectionStrings:App"] = UnreachableConnectionString,
        ["ConnectionStrings:Migrator"] = null,
        ["Database:MigrateOnStartup"] = "false",
        ["Cors:WebOrigin"] = "",
        // The content publishing worker looks across organizations on a timer; a test host starts without it, and a test of
        // publishing switches it on (and the fake publisher) for itself.
        ["Content:Publishing:WorkerEnabled"] = "false"
    };

    /// <summary>The key this host validates tokens with, for tests that mint a token of their own shape.</summary>
    public string SigningKey => settings["Jwt:SigningKey"]!;

    /// <summary>True when this host was given the address that refuses connections, so it can only decide from the token.</summary>
    private bool HasNoDatabase => settings.GetValueOrDefault("ConnectionStrings:App") == UnreachableConnectionString;

    /// <summary>
    /// An access token for the given user, agency and role, signed with this host's key. It carries the
    /// permissions the role holds in a fresh installation (or <paramref name="permissions"/>) and the
    /// permission version the host holds right now, so the API accepts it.
    /// </summary>
    public string CreateAccessToken(Guid userId, Guid agencyId, OrganizationRole role, IEnumerable<string>? permissions = null) =>
        TestAccessTokens.Create(settings["Jwt:SigningKey"]!, userId, agencyId, role, permissions, CurrentPermissionVersion());

    /// <summary>The same token with an explicit permission version, for a test that wants an out-of-date one.</summary>
    public string CreateAccessTokenWithVersion(Guid userId, Guid agencyId, OrganizationRole role, long permissionVersion) =>
        TestAccessTokens.Create(settings["Jwt:SigningKey"]!, userId, agencyId, role, null, permissionVersion);

    /// <summary>A current member token that does not say when it was issued.</summary>
    public string CreateAccessTokenWithoutIssuedAt(Guid userId, Guid agencyId, OrganizationRole role) =>
        TestAccessTokens.CreateWithoutIssuedAt(settings["Jwt:SigningKey"]!, userId, agencyId, role, CurrentPermissionVersion());

    /// <summary>A member token with no permissions and no version, as issued before permissions travelled in the token.</summary>
    public string CreateAccessTokenWithoutPermissions(Guid userId, Guid agencyId, OrganizationRole role) =>
        TestAccessTokens.CreateWithoutPermissions(settings["Jwt:SigningKey"]!, userId, agencyId, role);

    /// <summary>The permission version this host's API holds right now (read from the database, or the stub's 1).</summary>
    public long CurrentPermissionVersion() =>
        // On the thread pool, so blocking here cannot wait on the test framework's own synchronization context.
        Task.Run(() => Services.GetRequiredService<IPermissionService>().GetVersionAsync(CancellationToken.None)).GetAwaiter().GetResult();

    /// <summary>A site admin token (user and role, no agency) signed with this host's key.</summary>
    public string CreateSiteAdminAccessToken(Guid userId) =>
        TestAccessTokens.CreateSiteAdmin(settings["Jwt:SigningKey"]!, userId);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, RemoteIpFromHeaderStartupFilter>());

        // A test never writes keys to the developer machine: the data-protection key ring lives in memory.
        builder.ConfigureTestServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            // With no database to read the permission version from, the API would answer every member
            // token 500. The stub holds the default grants and a version that never moves.
            if (HasNoDatabase)
            {
                services.RemoveAll<IPermissionService>();
                services.AddSingleton<IPermissionService, DefaultGrantsPermissionService>();
                services.RemoveAll<IMenuCache>();
                services.AddSingleton<IMenuCache, DefaultMenuCache>();
                services.RemoveAll<ISessionRevocationService>();
                services.AddSingleton<ISessionRevocationService, DefaultSessionRevocationService>();
            }

            configureServices?.Invoke(services);
        });
    }

    /// <summary>
    /// The in-memory test server has no client address. A test sets one with the
    /// <see cref="RemoteIpHeader"/> header, so per-address behaviour such as rate limiting can be tested.
    /// </summary>
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    private sealed class RemoteIpFromHeaderStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(RemoteIpHeader, out var value) && IPAddress.TryParse(value, out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                return nextMiddleware();
            });
            next(app);
        };
    }
}
