using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        ["Cors:WebOrigin"] = ""
    };

    /// <summary>An access token for the given user, agency and role, signed with this host's key.</summary>
    public string CreateAccessToken(Guid userId, Guid agencyId, OrganizationRole role) =>
        TestAccessTokens.Create(settings["Jwt:SigningKey"]!, userId, agencyId, role);

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
