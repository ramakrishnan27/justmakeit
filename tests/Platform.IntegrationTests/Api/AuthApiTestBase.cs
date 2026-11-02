using System.Net;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Platform.Infrastructure.Data;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// Starts the API over a fresh migrated database for each test, so a test owns every row it looks
/// at. The API connects as app_user; helpers here arrange and inspect data as the database owner.
/// </summary>
public abstract class AuthApiTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    protected PostgresFixture Postgres { get; } = postgres;

    protected ApiTestHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        DapperConfiguration.Configure();
        Host = await ApiTestHost.StartWithDatabaseAsync(Postgres, configureServices: ConfigureServices);
    }

    /// <summary>Lets a test class replace or wrap a service of the hosted API, for example to count what it reads.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    public async Task DisposeAsync() => await Host.DisposeAsync();

    /// <summary>Adds an agency the user also belongs to, as the owner does in the database, and returns its id.</summary>
    protected async Task<Guid> AddMembershipAsync(
        Session user, string role, string agencyName = "Second agency", DateTimeOffset? lastActiveAt = null)
    {
        await using var owner = await Host.Database.OpenOwnerAsync();
        var agencyId = await DbTestSupport.InsertAgencyAsync(owner, agencyName);
        await DbTestSupport.InsertMemberAsync(owner, agencyId, user.UserId, role, lastActiveAt);
        return agencyId;
    }

    protected async Task<T> OwnerQueryAsync<T>(string sql, object? parameters = null)
    {
        await using var owner = await Host.Database.OpenOwnerAsync();
        return await owner.QuerySingleAsync<T>(sql, parameters);
    }

    protected async Task OwnerExecuteAsync(string sql, object? parameters = null)
    {
        await using var owner = await Host.Database.OpenOwnerAsync();
        await owner.ExecuteAsync(sql, parameters);
    }

    protected static async Task<string> CodeOfAsync(HttpResponseMessage response) =>
        (await ApiTestHost.ReadJsonAsync(response)).GetProperty("code").GetString()!;

    protected static void AssertStatus(HttpStatusCode expected, HttpResponseMessage response) =>
        Assert.Equal(expected, response.StatusCode);
}
