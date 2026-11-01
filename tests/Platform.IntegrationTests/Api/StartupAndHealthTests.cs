using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Platform.Helpers.Constants;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

/// <summary>Startup rules for the host. These need no database, only a connection that is refused.</summary>
public sealed class StartupValidationTests
{
    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        yield return exception;

        IEnumerable<Exception> inner = exception switch
        {
            AggregateException aggregate => aggregate.InnerExceptions,
            { InnerException: { } single } => [single],
            _ => []
        };

        foreach (var child in inner.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    private static string StartAndCaptureFailure(Dictionary<string, string?> settings, string environment = "Production")
    {
        using var factory = new ApiHostFactory(settings, environment);
        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        return string.Join(" | ", Flatten(exception).Select(e => e.Message));
    }

    [Fact]
    public void Startup_WithAValidConfiguration_Succeeds()
    {
        using var factory = new ApiHostFactory(ApiHostFactory.ValidSettings());

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    [Fact]
    public void Startup_WithoutASigningKey_FailsWithAClearMessage()
    {
        var settings = ApiHostFactory.ValidSettings();
        settings["Jwt:SigningKey"] = "";

        Assert.Contains("Jwt:SigningKey is missing", StartAndCaptureFailure(settings));
    }

    [Fact]
    public void Startup_WithASigningKeyShorterThan32Bytes_FailsWithAClearMessage()
    {
        var settings = ApiHostFactory.ValidSettings();
        settings["Jwt:SigningKey"] = new string('k', 31);

        var message = StartAndCaptureFailure(settings);

        Assert.Contains("Jwt:SigningKey is too short", message);
        Assert.Contains("31 bytes", message);
    }

    [Fact]
    public void Startup_WithAKeyOfExactly32Bytes_Succeeds()
    {
        var settings = ApiHostFactory.ValidSettings();
        settings["Jwt:SigningKey"] = new string('k', 32);
        using var factory = new ApiHostFactory(settings);

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    [Fact]
    public void Startup_WithoutTheAppConnectionString_FailsWithAClearMessage()
    {
        var settings = ApiHostFactory.ValidSettings();
        settings["ConnectionStrings:App"] = "";

        Assert.Contains("ConnectionStrings:App is not configured", StartAndCaptureFailure(settings));
    }

    [Fact]
    public void Startup_WithAnInvalidConnectionString_FailsWithAClearMessage()
    {
        var settings = ApiHostFactory.ValidSettings();
        settings["ConnectionStrings:App"] = "this is not a connection string";

        Assert.Contains("not a valid PostgreSQL connection string", StartAndCaptureFailure(settings));
    }

    [Fact]
    public void Startup_FailureMessage_NeverEchoesTheSigningKey()
    {
        var settings = ApiHostFactory.ValidSettings();
        settings["Jwt:SigningKey"] = "tiny-secret-value";

        Assert.DoesNotContain("tiny-secret-value", StartAndCaptureFailure(settings));
    }

    [Fact]
    public void Startup_InDevelopment_FailsClearlyWithoutAMigratorConnectionString()
    {
        // Development always migrates, and migrations need the owner connection, not app_user.
        var message = StartAndCaptureFailure(ApiHostFactory.ValidSettings(), environment: "Development");

        Assert.Contains("ConnectionStrings:Migrator is not configured", message);
    }

    [Fact]
    public void Startup_InProduction_DoesNotMigrateUnlessAsked()
    {
        // No migrator connection string and an unreachable database: starting succeeds, so nothing was attempted.
        using var factory = new ApiHostFactory(ApiHostFactory.ValidSettings());

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }

    [Fact]
    public void Startup_InProduction_MigratesWhenMigrateOnStartupIsTrue()
    {
        var settings = ApiHostFactory.ValidSettings();
        settings["Database:MigrateOnStartup"] = "true";

        Assert.Contains("ConnectionStrings:Migrator is not configured", StartAndCaptureFailure(settings));
    }
}

public sealed class HealthAndHostingTests
{
    [Fact]
    public async Task Health_ReportsUnhealthy_WhenTheDatabaseIsUnreachable()
    {
        using var factory = new ApiHostFactory(ApiHostFactory.ValidSettings());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_DoesNotLeakDatabaseDetails()
    {
        using var factory = new ApiHostFactory(ApiHostFactory.ValidSettings());
        using var client = factory.CreateClient();

        var body = await (await client.GetAsync("/health")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("127.0.0.1", body);
        Assert.DoesNotContain("app_user", body);
    }

    [Fact]
    public async Task Root_ServesTheWebFolder()
    {
        using var factory = new ApiHostFactory(ApiHostFactory.ValidSettings());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<div id=\"app\">", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownRoutes_AreClosedToAnonymousCallers()
    {
        // The authorization fallback policy applies to every request, so without a token even a route
        // that does not exist answers 401 and reveals nothing about which routes exist.
        using var factory = new ApiHostFactory(ApiHostFactory.ValidSettings());
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/does-not-exist")).StatusCode);
    }

    [Fact]
    public async Task UnknownRoutes_Return404_ForASignedInUser()
    {
        using var factory = new ApiHostFactory(ApiHostFactory.ValidSettings());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), OrganizationRole.ClientManager));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/v1/does-not-exist")).StatusCode);
    }

    [Fact]
    public async Task Swagger_IsNotServedInProduction()
    {
        using var factory = new ApiHostFactory(ApiHostFactory.ValidSettings());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("openapi", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cors_WithNoConfiguredOrigin_GrantsNothing()
    {
        using var factory = new ApiHostFactory(ApiHostFactory.ValidSettings());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "https://evil.example");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Cors_AllowsOnlyTheConfiguredWebOrigin()
    {
        var settings = ApiHostFactory.ValidSettings();
        settings["Cors:WebOrigin"] = "https://app.example";
        using var factory = new ApiHostFactory(settings);
        using var client = factory.CreateClient();

        using var allowed = new HttpRequestMessage(HttpMethod.Get, "/health");
        allowed.Headers.Add("Origin", "https://app.example");
        using var denied = new HttpRequestMessage(HttpMethod.Get, "/health");
        denied.Headers.Add("Origin", "https://evil.example");

        var allowedResponse = await client.SendAsync(allowed);
        var deniedResponse = await client.SendAsync(denied);

        Assert.Equal("https://app.example", allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", allowedResponse.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        Assert.False(deniedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void ResolvedJwtBearerOptions_AcceptOnlyHmacSha256_AndKeepClaimNames()
    {
        using var factory = new ApiHostFactory(ApiHostFactory.ValidSettings());

        var options = factory.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>>()
            .Get("Bearer");

        Assert.False(options.MapInboundClaims);
        Assert.Equal(["HS256"], options.TokenValidationParameters.ValidAlgorithms);
        Assert.True(options.TokenValidationParameters.ValidateLifetime);
        Assert.True(options.TokenValidationParameters.ValidateIssuerSigningKey);
        Assert.True(options.TokenValidationParameters.ValidateIssuer);
        Assert.True(options.TokenValidationParameters.ValidateAudience);
        Assert.Equal(TimeSpan.FromSeconds(30), options.TokenValidationParameters.ClockSkew);
    }
}

[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class HealthWithDatabaseTests(PostgresFixture postgres) : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await postgres.CreateDatabaseAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Health_ReportsHealthy_WhenTheDatabaseIsMigratedAndReachable()
    {
        var settings = ApiHostFactory.ValidSettings();
        settings["ConnectionStrings:App"] = _db.AppConnectionString;
        using var factory = new ApiHostFactory(settings);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Startup_InDevelopment_AppliesMigrationsToAnEmptyDatabase()
    {
        await using var empty = await postgres.CreateEmptyDatabaseAsync();
        var settings = ApiHostFactory.ValidSettings();
        settings["ConnectionStrings:App"] = empty.AppConnectionString;
        settings["ConnectionStrings:Migrator"] = empty.OwnerConnectionString;
        using var factory = new ApiHostFactory(settings, environment: "Development");
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        await using var owner = await empty.OpenOwnerAsync();
        Assert.Equal(1, await DbTestSupport.ScalarAsync<long>(owner, "SELECT count(*) FROM pg_class WHERE relname = 'workspaces'"));
    }

    [Fact]
    public async Task Swagger_InDevelopment_ServesTheDocument()
    {
        var settings = ApiHostFactory.ValidSettings();
        settings["ConnectionStrings:App"] = _db.AppConnectionString;
        settings["ConnectionStrings:Migrator"] = _db.OwnerConnectionString;
        using var factory = new ApiHostFactory(settings, environment: "Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Platform API", await response.Content.ReadAsStringAsync());
    }
}
