using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Platform.Helpers.Utilities;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// Hosts Platform.Api in memory with exactly the configuration a test supplies. Production is the
/// default environment: it skips user-secrets and Swagger, so a developer's local secrets can never
/// change what a test sees, and it does not migrate on start unless a test asks for that.
/// </summary>
public sealed class ApiHostFactory(IReadOnlyDictionary<string, string?> settings, string environment = "Production")
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
    }
}
