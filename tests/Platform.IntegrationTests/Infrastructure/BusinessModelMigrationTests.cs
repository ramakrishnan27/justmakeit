using Dapper;
using Npgsql;
using Platform.Auth.Services;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>
/// <c>003_business_models.sql</c>: the business model columns, the table of allowed model and role pairs,
/// the one-member rule for creators, and the upgrade of a database that still holds the released schema
/// (roles owner, admin, manager, viewer). Docker-tagged like every test that needs a real PostgreSQL.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class BusinessModelMigrationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string RoleModelCheck = "ck_agency_members_role_model";

    private TestDatabase _db = null!;

    public async Task InitializeAsync()
    {
        DapperConfiguration.Configure();
        _db = await postgres.CreateDatabaseAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<PostgresException> AssertRefusedAsync(Func<Task> action, string sqlState, string? constraint = null)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(sqlState, exception.SqlState);
        if (constraint is not null)
        {
            Assert.Equal(constraint, exception.ConstraintName);
        }

        return exception;
    }

    private static string Stored<TEnum>(TEnum value) where TEnum : struct, Enum => value.ToDb();

    // --- The table of allowed pairs ----------------------------------------------------------

    public static TheoryData<BusinessModel, OrganizationRole> EveryPair()
    {
        var data = new TheoryData<BusinessModel, OrganizationRole>();
        foreach (var model in Enum.GetValues<BusinessModel>())
        {
            foreach (var role in Enum.GetValues<OrganizationRole>())
            {
                data.Add(model, role);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPair))]
    public async Task TheDatabaseAcceptsAModelAndRolePair_ExactlyWhenBusinessModelRulesDoes(BusinessModel model, OrganizationRole role)
    {
        await using var owner = await _db.OpenOwnerAsync();
        var agency = await DbTestSupport.InsertAgencyAsync(owner, "Pairs", Stored(model));
        var user = await DbTestSupport.InsertUserAsync(owner, $"{Guid.NewGuid():N}@example.com");

        if (model.IsAllowed(role))
        {
            Assert.Equal(1, await DbTestSupport.InsertMemberAsync(owner, agency, user, Stored(role)));
        }
        else
        {
            await AssertRefusedAsync(
                () => DbTestSupport.InsertMemberAsync(owner, agency, user, Stored(role)),
                PostgresErrorCodes.CheckViolation, RoleModelCheck);
        }
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("admin")]
    [InlineData("manager")]
    [InlineData("viewer")]
    [InlineData("Organizationadmin")]
    [InlineData("")]
    public async Task TheRolesOfTheReleasedSchema_AreNoLongerAccepted(string role)
    {
        await using var owner = await _db.OpenOwnerAsync();
        var agency = await DbTestSupport.InsertAgencyAsync(owner);
        var user = await DbTestSupport.InsertUserAsync(owner, "old-role@example.com");

        await AssertRefusedAsync(
            () => DbTestSupport.InsertMemberAsync(owner, agency, user, role), PostgresErrorCodes.CheckViolation, RoleModelCheck);
    }

    // --- Creators have one member ------------------------------------------------------------

    [Fact]
    public async Task AnIndividualCreatorOrganization_CannotHaveASecondMember()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var agency = await DbTestSupport.InsertAgencyAsync(owner, "Solo", "creator");
        var first = await DbTestSupport.InsertUserAsync(owner, "first@example.com");
        var second = await DbTestSupport.InsertUserAsync(owner, "second@example.com");
        await DbTestSupport.InsertMemberAsync(owner, agency, first, "contentwriter");

        await AssertRefusedAsync(
            () => DbTestSupport.InsertMemberAsync(owner, agency, second, "contentwriter"),
            PostgresErrorCodes.UniqueViolation, "ux_agency_members_creator_single");
        Assert.Equal(1, await DbTestSupport.CountAsync(owner, $"agency_members WHERE agency_id = '{agency}'"));
    }

    [Theory]
    [InlineData("company")]
    [InlineData("agency")]
    public async Task ACompanyAndAnAgency_CanHaveManyMembers(string model)
    {
        await using var owner = await _db.OpenOwnerAsync();
        var agency = await DbTestSupport.InsertAgencyAsync(owner, "Team", model);
        foreach (var role in new[] { "organizationadmin", "marketingmanager", "contentwriter", "contentwriter" })
        {
            var user = await DbTestSupport.InsertUserAsync(owner, $"{Guid.NewGuid():N}@example.com");
            await DbTestSupport.InsertMemberAsync(owner, agency, user, role);
        }

        Assert.Equal(4, await DbTestSupport.CountAsync(owner, $"agency_members WHERE agency_id = '{agency}'"));
    }

    [Fact]
    public async Task TwoCreatorOrganizations_EachHaveTheirOwnSingleMember()
    {
        await using var owner = await _db.OpenOwnerAsync();
        foreach (var name in new[] { "Solo one", "Solo two" })
        {
            var agency = await DbTestSupport.InsertAgencyAsync(owner, name, "creator");
            var user = await DbTestSupport.InsertUserAsync(owner, $"{Guid.NewGuid():N}@example.com");
            Assert.Equal(1, await DbTestSupport.InsertMemberAsync(owner, agency, user, "contentwriter"));
        }

        Assert.Equal(2, await DbTestSupport.CountAsync(owner, "agency_members WHERE business_model = 'creator'"));
    }

    // --- The model belongs to the organization and cannot drift ------------------------------

    [Fact]
    public async Task AMembersModel_MustBeTheModelOfTheirOrganization()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var agency = await DbTestSupport.InsertAgencyAsync(owner, "An agency", "agency");
        var user = await DbTestSupport.InsertUserAsync(owner, "drift@example.com");

        await AssertRefusedAsync(
            () => DbTestSupport.ExecuteAsync(
                owner,
                "INSERT INTO agency_members (agency_id, user_id, business_model, role) VALUES ($1, $2, 'company', 'organizationadmin')",
                agency, user),
            PostgresErrorCodes.ForeignKeyViolation, "fk_agency_members_business_model");
    }

    [Fact]
    public async Task AnOrganizationsModel_CannotBeChanged_WhileItHasMembers_NotEvenByTheOwner()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var agency = await DbTestSupport.InsertAgencyAsync(owner, "Fixed", "agency");
        var user = await DbTestSupport.InsertUserAsync(owner, "fixed@example.com");
        await DbTestSupport.InsertMemberAsync(owner, agency, user, "organizationadmin");

        await AssertRefusedAsync(
            () => DbTestSupport.ExecuteAsync(owner, "UPDATE agencies SET business_model = 'company' WHERE id = $1", agency),
            PostgresErrorCodes.ForeignKeyViolation);
        Assert.Equal("agency", await owner.ExecuteScalarAsync<string>("SELECT business_model FROM agencies WHERE id = @agency", new { agency }));
    }

    [Fact]
    public async Task TheApplicationRole_CannotChangeItsOwnOrganizationsModel_NorGiveItselfAnotherRolesPair()
    {
        Guid agency;
        Guid user;
        await using (var owner = await _db.OpenOwnerAsync())
        {
            agency = await DbTestSupport.InsertAgencyAsync(owner, "Mine", "creator");
            user = await DbTestSupport.InsertUserAsync(owner, "mine@example.com");
            await DbTestSupport.InsertMemberAsync(owner, agency, user, "contentwriter");
        }

        await using var app = await _db.OpenAppAsync(agency, user);

        // The organization is the caller's own, so row-level security lets the statement through; the
        // foreign key and the CHECK are what stop it.
        await AssertRefusedAsync(
            () => DbTestSupport.ExecuteAsync(app, "UPDATE agencies SET business_model = 'company' WHERE id = $1", agency),
            PostgresErrorCodes.ForeignKeyViolation);
        await AssertRefusedAsync(
            () => DbTestSupport.ExecuteAsync(app, "UPDATE agency_members SET role = 'organizationadmin' WHERE agency_id = $1", agency),
            PostgresErrorCodes.CheckViolation, RoleModelCheck);
        Assert.Equal("creator", await app.ExecuteScalarAsync<string>("SELECT business_model FROM agencies"));
        Assert.Equal("contentwriter", await app.ExecuteScalarAsync<string>("SELECT role FROM agency_members"));
    }

    [Fact]
    public async Task AnUnknownModel_IsRefused()
    {
        await using var owner = await _db.OpenOwnerAsync();

        await AssertRefusedAsync(
            () => DbTestSupport.InsertAgencyAsync(owner, "Enterprise", "enterprise"),
            PostgresErrorCodes.CheckViolation, "ck_agencies_business_model");
    }

    [Fact]
    public async Task AnInsertThatForgetsTheModel_Fails_InsteadOfGuessing()
    {
        await using var owner = await _db.OpenOwnerAsync();

        await AssertRefusedAsync(
            () => DbTestSupport.ExecuteAsync(owner, "INSERT INTO agencies (name) VALUES ('Forgetful')"),
            PostgresErrorCodes.NotNullViolation);
        Assert.Null(await DbTestSupport.ScalarAsync<string>(
            owner, "SELECT column_default FROM information_schema.columns WHERE table_name = 'agencies' AND column_name = 'business_model'"));
        Assert.Null(await DbTestSupport.ScalarAsync<string>(
            owner, "SELECT column_default FROM information_schema.columns WHERE table_name = 'agency_members' AND column_name = 'business_model'"));
    }

    [Fact]
    public async Task DeletingAnOrganization_StillRemovesItsMembers()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var agency = await DbTestSupport.InsertAgencyAsync(owner, "Going", "company");
        var user = await DbTestSupport.InsertUserAsync(owner, "going@example.com");
        await DbTestSupport.InsertMemberAsync(owner, agency, user, "organizationadmin");

        Assert.Equal(1, await DbTestSupport.ExecuteAsync(owner, "DELETE FROM agencies WHERE id = $1", agency));

        Assert.Equal(0, await DbTestSupport.CountAsync(owner, "agency_members"));
    }

    // --- Isolation still holds with the new columns ------------------------------------------

    [Fact]
    public async Task EachTenant_SeesOnlyItsOwnBusinessModel()
    {
        Guid company;
        Guid creator;
        Guid companyUser;
        await using (var owner = await _db.OpenOwnerAsync())
        {
            company = await DbTestSupport.InsertAgencyAsync(owner, "Company", "company");
            creator = await DbTestSupport.InsertAgencyAsync(owner, "Creator", "creator");
            companyUser = await DbTestSupport.InsertUserAsync(owner, "company@example.com");
            var creatorUser = await DbTestSupport.InsertUserAsync(owner, "creator@example.com");
            await DbTestSupport.InsertMemberAsync(owner, company, companyUser, "organizationadmin");
            await DbTestSupport.InsertMemberAsync(owner, creator, creatorUser, "contentwriter");
        }

        await using var app = await _db.OpenAppAsync(company, companyUser);

        Assert.Equal(["company"], (await app.QueryAsync<string>("SELECT business_model FROM agencies")).ToList());
        Assert.Equal(["company"], (await app.QueryAsync<string>("SELECT business_model FROM agency_members")).ToList());
    }

    // --- The site admin list ------------------------------------------------------------------

    [Fact]
    public async Task TheSiteAdminListWithModels_ReturnsEveryOrganizationAndItsModel_ToASiteAdmin()
    {
        Guid siteAdmin;
        await using (var owner = await _db.OpenOwnerAsync())
        {
            await DbTestSupport.InsertAgencyAsync(owner, "A company", "company");
            await DbTestSupport.InsertAgencyAsync(owner, "A creator", "creator");
            siteAdmin = await DbTestSupport.InsertUserAsync(owner, "root@example.com");
            await DbTestSupport.InsertSiteAdminAsync(owner, siteAdmin);
        }

        await using var app = await _db.OpenAppAsync(null, siteAdmin);
        var rows = (await app.QueryAsync<(string Name, string BusinessModel, long Total)>(
            "SELECT name, business_model, total_count FROM site_admin_list_agencies_v2(10, 0) ORDER BY name")).ToList();

        Assert.Equal([("A company", "company", 2L), ("A creator", "creator", 2L)], rows);
    }

    [Fact]
    public async Task TheSiteAdminListWithModels_IsRefusedToAnyoneWhoIsNotASiteAdmin()
    {
        Guid user;
        await using (var owner = await _db.OpenOwnerAsync())
        {
            user = await DbTestSupport.InsertUserAsync(owner, "plain@example.com");
        }

        await using var app = await _db.OpenAppAsync(null, user);

        await AssertRefusedAsync(
            () => app.ExecuteScalarAsync("SELECT count(*) FROM site_admin_list_agencies_v2(10, 0)"),
            PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task TheSiteAdminListWithModels_CannotBeCalledWithoutAUser_AndIsNotOpenToPublic()
    {
        await using var app = await _db.OpenAppAsync();
        await AssertRefusedAsync(
            () => app.ExecuteScalarAsync("SELECT count(*) FROM site_admin_list_agencies_v2(10, 0)"),
            PostgresErrorCodes.InsufficientPrivilege);

        await using var owner = await _db.OpenOwnerAsync();
        Assert.False(await owner.ExecuteScalarAsync<bool>(
            "SELECT has_function_privilege('public', 'site_admin_list_agencies_v2(integer, integer)', 'EXECUTE')"));
        Assert.True(await owner.ExecuteScalarAsync<bool>(
            "SELECT has_function_privilege('app_user', 'site_admin_list_agencies_v2(integer, integer)', 'EXECUTE')"));
    }

    [Fact]
    public async Task TheSiteAdminListOf002_IsStillThere_WithItsOriginalShape()
    {
        // 002 re-creates it on every start, so it must keep its columns for the migrator to succeed.
        await using var owner = await _db.OpenOwnerAsync();

        var columns = (await owner.QueryAsync<string>(
            """
            SELECT unnest(proargnames) FROM pg_proc
            WHERE proname = 'site_admin_list_agencies' ORDER BY 1
            """)).ToList();

        Assert.Contains("plan", columns);
        Assert.DoesNotContain("business_model", columns);
    }

    // --- Nothing is weakened ------------------------------------------------------------------

    [Fact]
    public async Task RowLevelSecurity_StaysEnabledAndForced_AndNoMigrationPolicyIsLeftBehind()
    {
        await using var owner = await _db.OpenOwnerAsync();

        var tables = (await owner.QueryAsync<(string Name, bool Enabled, bool Forced)>(
            """
            SELECT relname, relrowsecurity, relforcerowsecurity FROM pg_class
            WHERE relname IN ('agencies', 'agency_members', 'workspaces', 'users') ORDER BY relname
            """)).ToList();

        Assert.Equal(4, tables.Count);
        Assert.All(tables, t => Assert.True(t.Enabled && t.Forced, $"{t.Name} must keep row-level security enabled and forced."));
        Assert.Equal(0, await DbTestSupport.CountAsync(owner, "pg_policies WHERE policyname = 'owner_migrate'"));
    }

    [Fact]
    public async Task TheApplicationRole_StillCannotBypassRowLevelSecurity()
    {
        await using var owner = await _db.OpenOwnerAsync();

        var flags = await owner.QuerySingleAsync<(bool Super, bool BypassRls)>(
            "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = 'app_user'");

        Assert.Equal((false, false), flags);
    }

    // --- Upgrading a database that holds the released schema ----------------------------------

    private static readonly string[] ReleasedScripts = ["Migrations.001_init.sql", "Migrations.002_site_admin.sql"];

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

    /// <summary>
    /// Adds one member the way the released schema allowed it. The owner acts as that tenant first, so
    /// it works for an owner that row-level security binds as well as for a superuser.
    /// </summary>
    private static async Task SeedReleasedMemberAsync(NpgsqlConnection owner, Guid agencyId, string role, bool createAgency)
    {
        var user = Guid.NewGuid();
        await DbTestSupport.ExecuteAsync(
            owner, "SELECT set_config('app.agency_id', $1, false), set_config('app.user_id', $2, false)",
            agencyId.ToString(), user.ToString());
        if (createAgency)
        {
            await DbTestSupport.ExecuteAsync(owner, "INSERT INTO agencies (id, name, plan) VALUES ($1, 'Released agency', 'paid')", agencyId);
        }

        await DbTestSupport.ExecuteAsync(
            owner, "INSERT INTO users (id, email, password_hash) VALUES ($1, $2, 'x')", user, $"{role}-{user:N}@example.com");
        await DbTestSupport.ExecuteAsync(owner, "INSERT INTO agency_members (agency_id, user_id, role) VALUES ($1, $2, $3)", agencyId, user, role);
    }

    private static async Task UpgradeAndVerifyAsync(TestDatabase db)
    {
        await ApplyReleasedScriptsAsync(db);

        var agency = Guid.NewGuid();
        await using (var owner = await db.OpenOwnerAsync())
        {
            var first = true;
            foreach (var role in new[] { "owner", "admin", "manager", "viewer" })
            {
                await SeedReleasedMemberAsync(owner, agency, role, createAgency: first);
                first = false;
            }
        }

        await db.MigrateAsync();

        await using var check = await db.OpenOwnerAsync();
        var roles = (await check.QueryAsync<string>("SELECT role FROM agency_members ORDER BY role")).ToList();
        Assert.Equal(["contentwriter", "marketingmanager", "organizationadmin", "organizationadmin"], roles);
        Assert.Equal(["agency"], (await check.QueryAsync<string>("SELECT DISTINCT business_model FROM agency_members")).ToList());
        Assert.Equal(["agency"], (await check.QueryAsync<string>("SELECT business_model FROM agencies")).ToList());
        Assert.Equal("paid", await check.ExecuteScalarAsync<string>("SELECT plan FROM agencies"));
        Assert.Equal(0, await DbTestSupport.CountAsync(check, "pg_policies WHERE policyname = 'owner_migrate'"));
        Assert.True(await check.ExecuteScalarAsync<bool>(
            "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = 'agency_members'"));

        // Every later start runs 001 and 002 again on the new shape, then 003 again. None of it may fail or change anything.
        await db.MigrateAsync();
        await db.MigrateAsync();
        Assert.Equal(roles, (await check.QueryAsync<string>("SELECT role FROM agency_members ORDER BY role")).ToList());
    }

    [Fact]
    public async Task UpgradingTheReleasedSchema_RemapsEveryOldRole_AndMakesTheExistingOrganizationsAgencies()
    {
        await using var released = await postgres.CreateEmptyDatabaseAsync();

        await UpgradeAndVerifyAsync(released);
    }

    [Fact]
    public async Task UpgradingTheReleasedSchema_AlsoWorks_ForAnOwnerThatRowLevelSecurityBinds()
    {
        // A superuser skips policies, which would hide a migration that silently updates nothing.
        // The limited owner is the proof that the temporary owner_migrate policy does its job.
        await using var released = await postgres.CreateEmptyDatabaseWithLimitedOwnerAsync();

        await UpgradeAndVerifyAsync(released);
    }

    [Fact]
    public async Task TheReleasedSchema_StillAcceptedTheOldRoles_SoTheUpgradeTestsStartFromARealOldState()
    {
        await using var released = await postgres.CreateEmptyDatabaseAsync();
        await ApplyReleasedScriptsAsync(released);

        await using var owner = await released.OpenOwnerAsync();
        var agency = Guid.NewGuid();
        await SeedReleasedMemberAsync(owner, agency, "viewer", createAgency: true);

        Assert.Equal("viewer", await owner.ExecuteScalarAsync<string>("SELECT role FROM agency_members"));
        Assert.False(await owner.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'agencies' AND column_name = 'business_model')"));
    }
}
