using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Repositories;
using Platform.Auth.Services;
using Platform.Auth.Settings;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;
using Platform.Infrastructure.Settings;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.SiteAdmin;

/// <summary>
/// The site admin features on a database owned by an ordinary role, which is how production should be
/// set up. Row-level security is FORCED, so even the owner sees no rows without a policy; the
/// SECURITY DEFINER functions run as that owner. If the owner policies in 002_site_admin.sql were
/// missing, the functions would not fail, they would just report zeros. These tests catch that.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class SiteAdminWithLimitedOwnerTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Password = "a long enough password";

    private TestDatabase _db = null!;

    public async Task InitializeAsync()
    {
        DapperConfiguration.Configure();
        _db = await postgres.CreateDatabaseWithLimitedOwnerAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<Guid> RegisterAgencyAsync(string email, string agencyName, string workspaceName)
    {
        // Through the app role and the registration repository, because the owner itself cannot insert
        // tenant rows under forced row-level security without acting as that tenant.
        var account = new NewAccount(
            Guid.NewGuid(), email, "pbkdf2$1$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==", "Owner", Guid.NewGuid(), agencyName, BusinessModel.Agency, OrganizationRole.OrganizationAdmin, Guid.NewGuid(), workspaceName);
        Assert.True(await new AccountRepository(_db.CreateFactory()).CreateAccountAsync(account, CancellationToken.None));
        return account.AgencyId;
    }

    private SiteAdminBootstrapper Bootstrapper(string email, string password = "") => new(
        Options.Create(new DatabaseSettings { MigratorConnectionString = _db.OwnerConnectionString }),
        Options.Create(new SiteAdminSettings { Email = email, Password = password }),
        new Pbkdf2PasswordHasher(iterations: 1_000),
        NullLogger<SiteAdminBootstrapper>.Instance);

    [Fact]
    public async Task TheOwner_IsReallyNotASuperuser_AndCannotBypassRowLevelSecurity()
    {
        await using var owner = await _db.OpenOwnerAsync();

        var flags = await owner.QuerySingleAsync<(bool Super, bool BypassRls)>(
            "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user");

        Assert.Equal((false, false), flags);
    }

    [Fact]
    public async Task TheFunctions_StillSeeEveryRow_BecauseOfTheOwnerPolicies()
    {
        await RegisterAgencyAsync("one@example.com", "Agency One", "Workspace one");
        await RegisterAgencyAsync("two@example.com", "Agency Two", "Workspace two");
        await RegisterAgencyAsync("three@example.com", "Agency Three", "Workspace three");
        await Bootstrapper("root@example.com", Password).EnsureAsync(CancellationToken.None);
        var admin = await QueryOwnerAsync<Guid>("SELECT user_id FROM site_admins");

        var repository = new SiteAdminRepository(_db.CreateFactory());
        var overview = await repository.GetOverviewAsync(admin, CancellationToken.None);
        var page = await repository.ListAgenciesAsync(admin, 1, 20, CancellationToken.None);

        Assert.Equal(3, overview.AgencyCount);
        Assert.Equal(3, overview.TrialAgencyCount);
        Assert.Equal(4, overview.UserCount); // three owners and the site admin
        Assert.Equal(3, overview.WorkspaceCount);
        Assert.Equal(3, overview.MemberCount);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(["Agency One", "Agency Three", "Agency Two"], page.Items.Select(a => a.Name).Order());
        Assert.All(page.Items, a =>
        {
            Assert.Equal(1, a.MemberCount);
            Assert.Equal(1, a.WorkspaceCount);
        });
    }

    [Fact]
    public async Task TheBootstrap_CreatesAndPromotes_AsTheLimitedOwner()
    {
        var created = await Bootstrapper("root@example.com", Password).EnsureAsync(CancellationToken.None);
        var again = await Bootstrapper("root@example.com", Password).EnsureAsync(CancellationToken.None);

        Assert.Equal(SiteAdminBootstrapResult.Created, created);
        Assert.Equal(SiteAdminBootstrapResult.AlreadySiteAdmin, again);

        // Promotion of an existing account, registered through the app role first.
        await RegisterAgencyAsync("owner@example.com", "Agency", "Workspace");
        var promoted = await Bootstrapper("owner@example.com").EnsureAsync(CancellationToken.None);
        Assert.Equal(SiteAdminBootstrapResult.Promoted, promoted);
    }

    [Fact]
    public async Task AnAgencyOwner_IsStillRefused_ByTheFunctions()
    {
        await RegisterAgencyAsync("owner@example.com", "Agency", "Workspace");
        var ownerId = await QueryOwnerAsync<Guid>("SELECT id FROM users WHERE email = 'owner@example.com'");

        await Assert.ThrowsAsync<Platform.Helpers.Exceptions.ForbiddenException>(
            () => new SiteAdminRepository(_db.CreateFactory()).GetOverviewAsync(ownerId, CancellationToken.None));
    }

    [Fact]
    public async Task TheLimitedOwner_CannotLeakRowsToTheApplicationRole_ThroughThePolicies()
    {
        await RegisterAgencyAsync("one@example.com", "Agency One", "Workspace one");
        await RegisterAgencyAsync("two@example.com", "Agency Two", "Workspace two");

        // The owner policies are for the owner only. The application role, with no tenant, still sees nothing.
        await using var anonymous = await _db.OpenAppAsync();
        Assert.Equal(0, await DbTestSupport.CountAsync(anonymous, "agencies"));
        Assert.Equal(0, await DbTestSupport.CountAsync(anonymous, "agency_members"));
        Assert.Equal(0, await DbTestSupport.CountAsync(anonymous, "workspaces"));
        Assert.Equal(0, await DbTestSupport.CountAsync(anonymous, "site_admins"));
    }

    private async Task<T> QueryOwnerAsync<T>(string sql)
    {
        await using var owner = new NpgsqlConnection(_db.OwnerConnectionString);
        await owner.OpenAsync();
        return await owner.QuerySingleAsync<T>(sql);
    }
}
