using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Platform.Auth.Services;
using Platform.Auth.Settings;
using Platform.Infrastructure.Data;
using Platform.Infrastructure.Settings;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>
/// <c>005_menus_permissions.sql</c> on a database owned by an ordinary role, which is how production
/// should be set up. Row-level security is FORCED, so even the owner sees and writes no rows without a
/// policy; the seeds and the SECURITY DEFINER functions run as that owner. Without the owner_manage
/// policies the seeds would be refused and the migration would fail to run at all, and these tests
/// would never get a database to look at.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class MenuPermissionsWithLimitedOwnerTests(PostgresFixture postgres) : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync()
    {
        DapperConfiguration.Configure();
        _db = await postgres.CreateDatabaseWithLimitedOwnerAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<Guid> CreateSiteAdminAsync()
    {
        // The limited owner cannot insert a user under forced row-level security, so the startup
        // bootstrap does it, as it does in production.
        var bootstrapper = new SiteAdminBootstrapper(
            Options.Create(new DatabaseSettings { MigratorConnectionString = _db.OwnerConnectionString }),
            Options.Create(new SiteAdminSettings { Email = "root@example.com", Password = "a long enough password" }),
            new Pbkdf2PasswordHasher(iterations: 1_000),
            NullLogger<SiteAdminBootstrapper>.Instance);
        await bootstrapper.EnsureAsync(CancellationToken.None);

        await using var owner = await _db.OpenOwnerAsync();
        return await owner.ExecuteScalarAsync<Guid>("SELECT user_id FROM site_admins");
    }

    [Fact]
    public async Task TheOwner_IsReallyNotASuperuser_AndCannotBypassRowLevelSecurity()
    {
        await using var owner = await _db.OpenOwnerAsync();

        var flags = await owner.QuerySingleAsync<(bool Super, bool BypassRls)>(
            "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user");

        Assert.Equal((false, false), flags);
    }

    [Fact]
    public async Task TheSeeds_WereWritten_ByTheOrdinaryOwner()
    {
        await using var owner = await _db.OpenOwnerAsync();

        Assert.Equal(7, await DbTestSupport.CountAsync(owner, "permissions"));
        Assert.Equal(56, await DbTestSupport.CountAsync(owner, "role_permissions"));
        Assert.Equal(1, await DbTestSupport.CountAsync(owner, "permission_state"));
        Assert.Equal(6, await DbTestSupport.CountAsync(owner, "menu_pages"));
        Assert.Equal(6, await DbTestSupport.CountAsync(owner, "menu_items"));
        Assert.Equal(0, await DbTestSupport.CountAsync(owner, "menu_pages WHERE NOT default_item_created"));
    }

    [Fact]
    public async Task ARestart_ChangesNothing_AndDoesNotMoveTheVersion()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var version = await owner.ExecuteScalarAsync<long>("SELECT version FROM permission_state");

        await _db.MigrateAsync();
        await _db.MigrateAsync();

        Assert.Equal(version, await owner.ExecuteScalarAsync<long>("SELECT version FROM permission_state"));
        Assert.Equal(56, await DbTestSupport.CountAsync(owner, "role_permissions"));
        Assert.Equal(6, await DbTestSupport.CountAsync(owner, "menu_items"));
    }

    [Fact]
    public async Task TheSnapshots_StillSeeEveryRow_BecauseOfTheOwnerPolicies()
    {
        await using var anonymous = await _db.OpenAppAsync();

        Assert.Equal(56, (await anonymous.QueryAsync("SELECT * FROM permission_snapshot()")).Count());
        Assert.Equal(6, (await anonymous.QueryAsync("SELECT * FROM menu_snapshot()")).Count());
    }

    [Fact]
    public async Task TheSiteAdminFunctions_WorkForASiteAdmin_AndTheTriggerMovesTheVersion()
    {
        var admin = await CreateSiteAdminAsync();
        await using var owner = await _db.OpenOwnerAsync();
        var version = await owner.ExecuteScalarAsync<long>("SELECT version FROM permission_state");

        await using var app = await _db.OpenAppAsync(null, admin);
        Assert.True(await app.ExecuteScalarAsync<bool>("SELECT site_admin_set_role_permission('agency', 'marketingmanager', 'data.write', false)"));
        Assert.True(await app.ExecuteScalarAsync<bool>(
            "SELECT site_admin_update_menu_item(i.id, 'Spaces', 'users', false) FROM menu_items i WHERE i.menu_key = 'member' AND i.page_key = 'workspaces'"));
        Assert.True(await app.ExecuteScalarAsync<bool>(
            "SELECT site_admin_delete_menu_item(i.id) FROM menu_items i WHERE i.menu_key = 'member' AND i.page_key = 'workspaces'"));

        Assert.Equal(version + 1, await owner.ExecuteScalarAsync<long>("SELECT version FROM permission_state"));
        Assert.False(await owner.ExecuteScalarAsync<bool>(
            "SELECT is_granted FROM role_permissions WHERE business_model = 'agency' AND role = 'marketingmanager' AND permission_key = 'data.write'"));
        Assert.Equal(5, await DbTestSupport.CountAsync(owner, "menu_items"));
    }

    [Fact]
    public async Task AnOrdinaryUser_IsStillRefused_AndTheApplicationRoleWithNoUserSeesNothing()
    {
        await using var anonymous = await _db.OpenAppAsync();
        foreach (var table in new[] { "permissions", "role_permissions", "menu_pages", "menu_items" })
        {
            Assert.Equal(0, await DbTestSupport.CountAsync(anonymous, table));
        }

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => anonymous.ExecuteScalarAsync<bool>("SELECT site_admin_set_role_permission('company', 'organizationadmin', 'data.read', true)"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }
}
