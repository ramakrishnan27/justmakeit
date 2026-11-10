using Dapper;
using Npgsql;
using Platform.Infrastructure.Data;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>
/// 006_remove_refresh_tokens.sql on a database that still holds the two refresh token tables, the way the
/// development database does. Every start runs every script, so 001 and 002 create the tables again and 006 drops
/// them again; the result must be the same on the first start and on every later one, for a superuser owner and for
/// an owner that row-level security binds.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Database")]
public sealed class RefreshTokenRemovalMigrationTests(PostgresFixture postgres)
{
    private static readonly string[] ReleasedScripts =
        ["Migrations.001_init.sql", "Migrations.002_site_admin.sql"];

    private static async Task ApplyReleasedScriptsAsync(TestDatabase db)
    {
        var assembly = typeof(DbMigrator).Assembly;
        await using var owner = await db.OpenOwnerAsync();
        foreach (var name in ReleasedScripts)
        {
            await using var stream = assembly.GetManifestResourceStream(name)
                                     ?? throw new InvalidOperationException($"Embedded migration '{name}' was not found.");
            using var reader = new StreamReader(stream);
            await using var command = new NpgsqlCommand(await reader.ReadToEndAsync(), owner);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<string?> TableAsync(NpgsqlConnection owner, string table) =>
        await owner.ExecuteScalarAsync<string?>($"SELECT to_regclass('public.{table}')::text");

    private static async Task<long> FunctionsAsync(NpgsqlConnection owner) =>
        await owner.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM pg_proc WHERE proname IN ('auth_find_refresh_token', 'auth_find_site_refresh_token')");

    private static async Task UpgradeAndVerifyAsync(TestDatabase db)
    {
        await ApplyReleasedScriptsAsync(db);

        await using (var before = await db.OpenOwnerAsync())
        {
            // The starting point is real: the released scripts do create the tables and their lookups.
            Assert.Equal("refresh_tokens", await TableAsync(before, "refresh_tokens"));
            Assert.Equal("site_admin_refresh_tokens", await TableAsync(before, "site_admin_refresh_tokens"));
            Assert.Equal(2, await FunctionsAsync(before));
        }

        await db.MigrateAsync();

        await using var after = await db.OpenOwnerAsync();
        Assert.Null(await TableAsync(after, "refresh_tokens"));
        Assert.Null(await TableAsync(after, "site_admin_refresh_tokens"));
        Assert.Equal(0, await FunctionsAsync(after));

        // Every later start runs 001 and 002 again (which create the tables) and 006 drops them again.
        await db.MigrateAsync();
        await db.MigrateAsync();
        Assert.Null(await TableAsync(after, "refresh_tokens"));
        Assert.Null(await TableAsync(after, "site_admin_refresh_tokens"));
        Assert.Equal(0, await FunctionsAsync(after));

        // Nothing else was touched: the tables that matter are still there and still forced.
        Assert.Equal(
            ["agencies", "agency_members", "site_admins", "users", "workspaces"],
            (await after.QueryAsync<string>(
                """
                SELECT relname FROM pg_class
                WHERE relname IN ('agencies', 'agency_members', 'site_admins', 'users', 'workspaces')
                  AND relrowsecurity AND relforcerowsecurity ORDER BY relname
                """)).ToList());
    }

    [Fact]
    public async Task ADatabaseThatHeldRefreshTokens_LosesTheTablesAndTheirLookups_OnTheNextStart_AndStaysWithoutThem()
    {
        await using var released = await postgres.CreateEmptyDatabaseAsync();

        await UpgradeAndVerifyAsync(released);
    }

    [Fact]
    public async Task TheRemoval_AlsoWorks_ForAnOwnerThatRowLevelSecurityBinds()
    {
        await using var released = await postgres.CreateEmptyDatabaseWithLimitedOwnerAsync();

        await UpgradeAndVerifyAsync(released);
    }
}
