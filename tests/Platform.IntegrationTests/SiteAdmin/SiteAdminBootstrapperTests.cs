using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Platform.Auth.Interfaces;
using Platform.Auth.Services;
using Platform.Auth.Settings;
using Platform.Infrastructure.Data;
using Platform.Infrastructure.Settings;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.SiteAdmin;

/// <summary>Creating the first site admin as the database owner, the way the app does right after migrating.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class SiteAdminBootstrapperTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Password = "a long enough password";

    private readonly Pbkdf2PasswordHasher _hasher = new(iterations: 1_000);
    private TestDatabase _db = null!;

    public async Task InitializeAsync()
    {
        DapperConfiguration.Configure();
        _db = await postgres.CreateDatabaseAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private SiteAdminBootstrapper Bootstrapper(string email = "", string password = "") => new(
        Options.Create(new DatabaseSettings { MigratorConnectionString = _db.OwnerConnectionString }),
        Options.Create(new SiteAdminSettings { Email = email, Password = password }),
        _hasher,
        NullLogger<SiteAdminBootstrapper>.Instance);

    private async Task<T> QueryAsync<T>(string sql, object? parameters = null)
    {
        await using var owner = await _db.OpenOwnerAsync();
        return await owner.QuerySingleAsync<T>(sql, parameters);
    }

    [Fact]
    public async Task WithNoEmailConfigured_DoesNothing()
    {
        var result = await Bootstrapper().EnsureAsync(CancellationToken.None);

        Assert.Equal(SiteAdminBootstrapResult.NotConfigured, result);
        Assert.Equal(0, await QueryAsync<long>("SELECT count(*) FROM users"));
        Assert.Equal(0, await QueryAsync<long>("SELECT count(*) FROM site_admins"));
    }

    [Fact]
    public async Task WhenTheAccountDoesNotExist_CreatesItAsASiteAdmin_WithAHashedPassword_AndNoAgency()
    {
        var result = await Bootstrapper("  Boss@Example.COM ", Password).EnsureAsync(CancellationToken.None);

        Assert.Equal(SiteAdminBootstrapResult.Created, result);
        var user = await QueryAsync<(Guid Id, string Email, string PasswordHash, string FullName)>(
            "SELECT id, email, password_hash, full_name FROM users");
        Assert.Equal("boss@example.com", user.Email);
        Assert.Equal("Site Admin", user.FullName);
        Assert.DoesNotContain(Password, user.PasswordHash);
        Assert.StartsWith("pbkdf2$", user.PasswordHash);
        Assert.True(_hasher.Verify(Password, user.PasswordHash));
        Assert.Equal(user.Id, await QueryAsync<Guid>("SELECT user_id FROM site_admins"));
        Assert.Equal(0, await QueryAsync<long>("SELECT count(*) FROM agency_members"));
        Assert.Equal(0, await QueryAsync<long>("SELECT count(*) FROM agencies"));
    }

    [Fact]
    public async Task WhenTheAccountAlreadyExists_PromotesIt_WithoutTouchingItsPassword()
    {
        Guid existing;
        await using (var owner = await _db.OpenOwnerAsync())
        {
            existing = await DbTestSupport.InsertUserAsync(owner, "boss@example.com", "Boss");
        }

        var before = await QueryAsync<string>("SELECT password_hash FROM users");
        var result = await Bootstrapper("BOSS@example.com", "a completely different password").EnsureAsync(CancellationToken.None);

        Assert.Equal(SiteAdminBootstrapResult.Promoted, result);
        Assert.Equal(existing, await QueryAsync<Guid>("SELECT user_id FROM site_admins"));
        Assert.Equal(before, await QueryAsync<string>("SELECT password_hash FROM users"));
        Assert.Equal(1, await QueryAsync<long>("SELECT count(*) FROM users"));
    }

    [Fact]
    public async Task WhenTheAccountExists_PromotingNeedsNoPassword()
    {
        await using (var owner = await _db.OpenOwnerAsync())
        {
            await DbTestSupport.InsertUserAsync(owner, "boss@example.com", "Boss");
        }

        var result = await Bootstrapper("boss@example.com").EnsureAsync(CancellationToken.None);

        Assert.Equal(SiteAdminBootstrapResult.Promoted, result);
    }

    [Fact]
    public async Task RunningItAgain_ChangesNothing()
    {
        var bootstrapper = Bootstrapper("boss@example.com", Password);
        await bootstrapper.EnsureAsync(CancellationToken.None);
        var hash = await QueryAsync<string>("SELECT password_hash FROM users");

        var second = await bootstrapper.EnsureAsync(CancellationToken.None);
        var third = await bootstrapper.EnsureAsync(CancellationToken.None);

        Assert.Equal(SiteAdminBootstrapResult.AlreadySiteAdmin, second);
        Assert.Equal(SiteAdminBootstrapResult.AlreadySiteAdmin, third);
        Assert.Equal(1, await QueryAsync<long>("SELECT count(*) FROM users"));
        Assert.Equal(1, await QueryAsync<long>("SELECT count(*) FROM site_admins"));
        Assert.Equal(hash, await QueryAsync<string>("SELECT password_hash FROM users"));
    }

    [Fact]
    public async Task ChangingThePasswordInSettings_DoesNotChangeAnExistingAdminsPassword()
    {
        await Bootstrapper("boss@example.com", Password).EnsureAsync(CancellationToken.None);

        await Bootstrapper("boss@example.com", "a different password now").EnsureAsync(CancellationToken.None);

        Assert.True(_hasher.Verify(Password, await QueryAsync<string>("SELECT password_hash FROM users")));
    }

    [Fact]
    public async Task WhenTheAccountDoesNotExist_AndThereIsNoPassword_CreatesNothing()
    {
        var result = await Bootstrapper("boss@example.com").EnsureAsync(CancellationToken.None);

        Assert.Equal(SiteAdminBootstrapResult.MissingPassword, result);
        Assert.Equal(0, await QueryAsync<long>("SELECT count(*) FROM users"));
        Assert.Equal(0, await QueryAsync<long>("SELECT count(*) FROM site_admins"));
    }

    [Fact]
    public async Task TheCreatedAdmin_IsASiteAdmin_AsTheApplicationRoleSeesIt()
    {
        await Bootstrapper("boss@example.com", Password).EnsureAsync(CancellationToken.None);
        var userId = await QueryAsync<Guid>("SELECT id FROM users");

        var repository = new Platform.Auth.Repositories.SiteAdminRepository(_db.CreateFactory());

        Assert.True(await repository.IsSiteAdminAsync(userId, CancellationToken.None));
    }
}
