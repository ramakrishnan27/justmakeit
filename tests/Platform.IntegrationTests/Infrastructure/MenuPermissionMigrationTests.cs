using Dapper;
using Npgsql;
using Platform.Auth.Services;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>
/// <c>005_menus_permissions.sql</c>: the permission catalog and grants, the permission version, the menu
/// page registry and menu items, who can read and write them, and that a restart never undoes what the
/// Site admin changed. Database-tagged like every test that needs a real PostgreSQL server.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Database")]
public sealed class MenuPermissionMigrationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly string[] ReadableTables = ["permissions", "role_permissions", "menu_pages", "menu_items"];
    private static readonly string[] SiteAdminWrites =
    [
        "site_admin_create_menu_item(text, text, text, text, boolean)",
        "site_admin_update_menu_item(uuid, text, text, boolean)",
        "site_admin_delete_menu_item(uuid)",
        "site_admin_reorder_menu(text, jsonb)",
        "site_admin_set_role_permission(text, text, text, boolean)"
    ];
    private static readonly string[] CacheReads = ["permission_snapshot()", "menu_snapshot()"];

    private TestDatabase _db = null!;

    public async Task InitializeAsync()
    {
        DapperConfiguration.Configure();
        _db = await postgres.CreateDatabaseAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static async Task<PostgresException> AssertRefusedAsync(Func<Task> action, string sqlState, string? constraintOrMessage = null)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(sqlState, exception.SqlState);
        if (constraintOrMessage is not null)
        {
            Assert.True(
                exception.ConstraintName == constraintOrMessage || exception.MessageText == constraintOrMessage,
                $"Expected constraint or message '{constraintOrMessage}', got constraint '{exception.ConstraintName}' and message '{exception.MessageText}'.");
        }

        return exception;
    }

    private async Task<Guid> CreateSiteAdminAsync()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var id = await DbTestSupport.InsertUserAsync(owner, $"{Guid.NewGuid():N}@example.com");
        await DbTestSupport.InsertSiteAdminAsync(owner, id);
        return id;
    }

    private async Task<long> VersionAsync()
    {
        await using var owner = await _db.OpenOwnerAsync();
        return await owner.ExecuteScalarAsync<long>("SELECT version FROM permission_state");
    }

    private static Task<bool> IsGrantedAsync(NpgsqlConnection connection, string model, string role, string key) =>
        connection.ExecuteScalarAsync<bool>(
            "SELECT is_granted FROM role_permissions WHERE business_model = @model AND role = @role AND permission_key = @key",
            new { model, role, key });

    /// <summary>A member page with no menu item yet (its default item is marked as made), for tests that add one.</summary>
    private async Task AddReportsPageAsync()
    {
        await using var owner = await _db.OpenOwnerAsync();
        await owner.ExecuteAsync(
            """
            INSERT INTO menu_pages (app, key, path, title, permission_key, default_label, default_icon, default_sort_order, default_item_created)
            VALUES ('member', 'reports', '/reports', 'Reports', 'data.read', 'Reports', 'chart', 30, true)
            """);
    }

    // --- The catalog and the seeded grants ---------------------------------------------------

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
    public async Task TheTableOfGrants_AcceptsAModelAndRolePair_ExactlyWhenBusinessModelRulesDoes(BusinessModel model, OrganizationRole role)
    {
        await using var owner = await _db.OpenOwnerAsync();

        Task<int> TryInsert() => owner.ExecuteAsync(
            "INSERT INTO role_permissions (business_model, role, permission_key, is_granted) VALUES (@model, @role, 'data.read', true) ON CONFLICT DO NOTHING",
            new { model = model.ToDb(), role = role.ToDb() });

        if (model.IsAllowed(role))
        {
            Assert.Equal(0, await TryInsert()); // the pair is seeded already, so nothing new
        }
        else
        {
            await AssertRefusedAsync(TryInsert, PostgresErrorCodes.CheckViolation, "ck_role_permissions_role_model");
        }
    }

    [Fact]
    public async Task TheCatalog_IsExactlyPermissionKeys_AndOnlyTheDashboardIsProtected()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var catalog = (await owner.QueryAsync<(string Key, bool IsProtected)>("SELECT key, is_protected FROM permissions")).ToList();

        Assert.Equal(PermissionKeys.All.Order(), catalog.Select(c => c.Key).Order());
        Assert.All(catalog, c => Assert.Equal(PermissionKeys.IsProtected(c.Key), c.IsProtected));
    }

    [Fact]
    public async Task TheSeededGrants_AreTheRulesThatWereHardCoded_ForEverySlotAndEveryKey()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var rows = (await owner.QueryAsync<(string Model, string Role, string Key, bool Granted)>(
            "SELECT business_model, role, permission_key, is_granted FROM role_permissions")).ToList();

        var slots = Enum.GetValues<BusinessModel>()
            .SelectMany(model => model.AllowedRoles().Select(role => (Model: model, Role: role)))
            .ToList();

        Assert.Equal(8, slots.Count);
        Assert.Equal(slots.Count * PermissionKeys.All.Count, rows.Count); // 8 slots by 16 keys = 128
        foreach (var (model, role) in slots)
        {
            foreach (var key in PermissionKeys.All)
            {
                var row = Assert.Single(rows, r => r.Model == model.ToDb() && r.Role == role.ToDb() && r.Key == key);
                Assert.True(
                    role.GrantedByDefault(model, key) == row.Granted,
                    $"{model}/{role} {key}: seeded {row.Granted}, RolePermissions says {role.GrantedByDefault(model, key)}.");
            }
        }
    }

    [Fact]
    public async Task EverySlot_HoldsTheProtectedPermission()
    {
        await using var owner = await _db.OpenOwnerAsync();

        Assert.Equal(8, await owner.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM role_permissions WHERE permission_key = 'dashboard.view' AND is_granted"));
    }

    [Fact]
    public async Task TheSeededPages_AndTheirDefaultItems_AreTheOnesThatExist()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var items = (await owner.QueryAsync<(string Menu, string Page, string Path, string? Permission, string Label, string Icon, int Sort, bool Visible, bool Locked)>(
            """
            SELECT i.menu_key, i.page_key, p.path, p.permission_key, i.label, i.icon, i.sort_order, i.is_visible, i.is_locked
            FROM menu_items i JOIN menu_pages p ON p.app = i.menu_key AND p.key = i.page_key
            ORDER BY i.menu_key, i.sort_order
            """)).ToList();

        Assert.Equal(
            [
                "admin/dashboard /admin [] Dashboard home 10 visible=True locked=True",
                "admin/users /admin/users [] Users users 12 visible=True locked=False",
                "admin/channels /admin/channels [] Channels list 14 visible=True locked=False",
                "admin/menus /admin/menus [] Menus menu 20 visible=True locked=True",
                "admin/permissions /admin/permissions [] Permissions shield 30 visible=True locked=True",
                "member/dashboard /dashboard [dashboard.view] Dashboard home 10 visible=True locked=True",
                "member/content-studio /content-studio [contentstudio.view] Content Studio edit 11 visible=True locked=False",
                "member/media-library /media-library [medialibrary.view] Media Library image 12 visible=True locked=False",
                "member/ai-studio /ai-studio [aistudio.view] AI Studio sparkles 13 visible=True locked=False",
                "member/calendar /calendar [calendar.view] Calendar calendar 14 visible=True locked=False",
                "member/workspaces /workspaces [workspaces.view] Workspaces folder 20 visible=True locked=False",
                "member/platforms /platforms [platforms.view] Platform list 25 visible=True locked=False",
                "member/team /team [members.manage] Team users 30 visible=True locked=False"
            ],
            items.Select(i =>
                $"{i.Menu}/{i.Page} {i.Path} [{i.Permission}] {i.Label} {i.Icon} {i.Sort} visible={i.Visible} locked={i.Locked}"));
        Assert.Equal(0, await DbTestSupport.CountAsync(owner, "menu_pages WHERE NOT default_item_created"));
    }

    [Fact]
    public async Task EveryIconTheScriptSeeds_IsOneTheApiAcceptsAndTheClientCanDraw()
    {
        await using var owner = await _db.OpenOwnerAsync();

        var icons = (await owner.QueryAsync<string>("SELECT icon FROM menu_items UNION SELECT default_icon FROM menu_pages")).ToList();

        Assert.NotEmpty(icons);
        Assert.All(icons, icon => Assert.True(MenuIcons.IsKnown(icon), $"'{icon}' is not in MenuIcons.All."));
    }

    // --- Who can read and who can write ------------------------------------------------------

    [Theory]
    [InlineData("permissions")]
    [InlineData("role_permissions")]
    [InlineData("menu_pages")]
    [InlineData("menu_items")]
    public async Task AppUser_CanOnlyReadTheTable_NeverWriteIt(string table)
    {
        await using var owner = await _db.OpenOwnerAsync();

        Assert.True(await owner.ExecuteScalarAsync<bool>("SELECT has_table_privilege('app_user', @t, 'SELECT')", new { t = "public." + table }));
        foreach (var privilege in new[] { "INSERT", "UPDATE", "DELETE", "TRUNCATE" })
        {
            Assert.False(
                await owner.ExecuteScalarAsync<bool>("SELECT has_table_privilege('app_user', @t, @p)", new { t = "public." + table, p = privilege }),
                $"app_user must not have {privilege} on {table}.");
        }
    }

    [Fact]
    public async Task TheVersionTable_IsNotReadableByAppUser_AtAll()
    {
        await using var owner = await _db.OpenOwnerAsync();
        Assert.False(await owner.ExecuteScalarAsync<bool>("SELECT has_table_privilege('app_user', 'public.permission_state', 'SELECT')"));

        await using var app = await _db.OpenAppAsync(null, await CreateSiteAdminAsync());
        await AssertRefusedAsync(() => app.ExecuteScalarAsync<long>("SELECT version FROM permission_state"), PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task ARequestWithNoUser_SeesNothing_AndASignedInUserSeesTheConfiguration()
    {
        await using (var anonymous = await _db.OpenAppAsync())
        {
            foreach (var table in ReadableTables)
            {
                Assert.Equal(0, await DbTestSupport.CountAsync(anonymous, table));
            }
        }

        await using var signedIn = await _db.OpenAppAsync(null, Guid.NewGuid());
        Assert.Equal(16, await DbTestSupport.CountAsync(signedIn, "permissions"));
        Assert.Equal(128, await DbTestSupport.CountAsync(signedIn, "role_permissions"));
        Assert.Equal(13, await DbTestSupport.CountAsync(signedIn, "menu_pages"));
        Assert.Equal(13, await DbTestSupport.CountAsync(signedIn, "menu_items"));
    }

    [Fact]
    public async Task ASiteAdmin_CannotWriteTheTablesDirectly_OnlyThroughTheFunctions()
    {
        await using var app = await _db.OpenAppAsync(null, await CreateSiteAdminAsync());

        await AssertRefusedAsync(
            () => app.ExecuteAsync("UPDATE role_permissions SET is_granted = false"), PostgresErrorCodes.InsufficientPrivilege);
        await AssertRefusedAsync(
            () => app.ExecuteAsync("DELETE FROM menu_items"), PostgresErrorCodes.InsufficientPrivilege);
        await AssertRefusedAsync(
            () => app.ExecuteAsync("UPDATE permissions SET is_protected = false"), PostgresErrorCodes.InsufficientPrivilege);
        await AssertRefusedAsync(
            () => app.ExecuteAsync("INSERT INTO menu_pages (app, key, path, title, default_label, default_icon, default_sort_order) VALUES ('member', 'x', '/x', 'X', 'X', 'home', 1)"),
            PostgresErrorCodes.InsufficientPrivilege);
    }

    // --- Constraints and triggers ------------------------------------------------------------

    [Fact]
    public async Task ARevokeOfAProtectedPermission_IsRefusedByTheDatabase_EvenForTheOwner()
    {
        await using var owner = await _db.OpenOwnerAsync();

        await AssertRefusedAsync(
            () => owner.ExecuteAsync("UPDATE role_permissions SET is_granted = false WHERE permission_key = 'dashboard.view'"),
            PostgresErrorCodes.RaiseException, "permission_protected");
        Assert.Equal(8, await owner.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM role_permissions WHERE permission_key = 'dashboard.view' AND is_granted"));
    }

    [Fact]
    public async Task TheVersion_RisesOnARealChange_AndNotOnANoOp()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var start = await VersionAsync();

        await owner.ExecuteAsync(
            "UPDATE role_permissions SET is_granted = false WHERE business_model = 'company' AND role = 'contentwriter' AND permission_key = 'data.write'");
        Assert.Equal(start + 1, await VersionAsync());

        // The same value again changes nothing, so nobody's token is invalidated for it.
        await owner.ExecuteAsync(
            "UPDATE role_permissions SET is_granted = false WHERE business_model = 'company' AND role = 'contentwriter' AND permission_key = 'data.write'");
        Assert.Equal(start + 1, await VersionAsync());

        await owner.ExecuteAsync(
            "UPDATE role_permissions SET is_granted = true WHERE business_model = 'company' AND role = 'contentwriter' AND permission_key = 'data.write'");
        Assert.Equal(start + 2, await VersionAsync());
    }

    [Fact]
    public async Task TheVersion_RisesWhenANewGrantRowIsAdded()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var start = await VersionAsync();

        await owner.ExecuteAsync(
            "INSERT INTO permissions (key, title, description) VALUES ('reports.view', 'View reports', 'See the reports.')");
        Assert.Equal(start, await VersionAsync()); // a catalog row alone changes nobody's access

        await owner.ExecuteAsync(
            "INSERT INTO role_permissions (business_model, role, permission_key, is_granted) VALUES ('company', 'organizationadmin', 'reports.view', true)");
        Assert.Equal(start + 1, await VersionAsync());
    }

    [Fact]
    public async Task AnUpdate_AlwaysStampsUpdatedAt_EvenIfTheStatementTriesToSetIt()
    {
        await using var owner = await _db.OpenOwnerAsync();

        await owner.ExecuteAsync(
            "UPDATE role_permissions SET is_granted = false, updated_at = '2000-01-01' WHERE business_model = 'agency' AND role = 'clientmanager' AND permission_key = 'workspaces.view'");

        Assert.True(await owner.ExecuteScalarAsync<bool>(
            "SELECT updated_at > '2020-01-01' FROM role_permissions WHERE business_model = 'agency' AND role = 'clientmanager' AND permission_key = 'workspaces.view'"));
    }

    [Fact]
    public async Task AGrantNeedsARealPermissionAndAnAllowedSlot()
    {
        await using var owner = await _db.OpenOwnerAsync();

        await AssertRefusedAsync(
            () => owner.ExecuteAsync("INSERT INTO role_permissions (business_model, role, permission_key, is_granted) VALUES ('company', 'organizationadmin', 'nope.nothing', true)"),
            PostgresErrorCodes.ForeignKeyViolation);
        await AssertRefusedAsync(
            () => owner.ExecuteAsync("INSERT INTO permissions (key, title, description) VALUES ('Bad Key', 't', 'd')"),
            PostgresErrorCodes.CheckViolation, "ck_permissions_key");
    }

    [Fact]
    public async Task AMenuItem_CanOnlySitInTheMenuOfItsOwnApp_AndOnlyOnce()
    {
        await using var owner = await _db.OpenOwnerAsync();

        // The member page "workspaces" does not exist in the admin app.
        await AssertRefusedAsync(
            () => owner.ExecuteAsync("INSERT INTO menu_items (menu_key, page_key, label, icon, sort_order) VALUES ('admin', 'workspaces', 'W', 'folder', 99)"),
            PostgresErrorCodes.ForeignKeyViolation, "fk_menu_items_page");
        await AssertRefusedAsync(
            () => owner.ExecuteAsync("INSERT INTO menu_items (menu_key, page_key, label, icon, sort_order) VALUES ('member', 'dashboard', 'Again', 'home', 99)"),
            PostgresErrorCodes.UniqueViolation, "uq_menu_items_menu_page");
    }

    [Fact]
    public async Task ALockedItem_CannotBeHidden_NotEvenByTheOwner()
    {
        await using var owner = await _db.OpenOwnerAsync();

        await AssertRefusedAsync(
            () => owner.ExecuteAsync("UPDATE menu_items SET is_visible = false WHERE menu_key = 'member' AND page_key = 'dashboard'"),
            PostgresErrorCodes.CheckViolation, "ck_menu_items_locked_visible");
    }

    [Theory]
    [InlineData("Trailing ")]
    [InlineData(" Leading")]
    [InlineData("")]
    public async Task AMenuItemLabel_IsTrimmedAndNotEmpty(string label)
    {
        await using var owner = await _db.OpenOwnerAsync();
        await AddReportsPageAsync();

        await AssertRefusedAsync(
            () => owner.ExecuteAsync(
                "INSERT INTO menu_items (menu_key, page_key, label, icon, sort_order) VALUES ('member', 'reports', @label, 'chart', 30)", new { label }),
            PostgresErrorCodes.CheckViolation, "ck_menu_items_label");
    }

    [Fact]
    public async Task AMenuItemLabel_HasAtMostSixtyCharacters()
    {
        await using var owner = await _db.OpenOwnerAsync();
        await AddReportsPageAsync();

        Task Insert(string label) => owner.ExecuteAsync(
            "INSERT INTO menu_items (menu_key, page_key, label, icon, sort_order) VALUES ('member', 'reports', @label, 'chart', 30)", new { label });

        await AssertRefusedAsync(() => Insert(new string('a', 61)), PostgresErrorCodes.CheckViolation, "ck_menu_items_label");
        await Insert(new string('a', 60));
    }

    [Theory]
    [InlineData("Home")]
    [InlineData("1home")]
    [InlineData("ho me")]
    [InlineData("")]
    public async Task AnIconName_MustBeLowerCaseWordsWithHyphens(string icon)
    {
        await using var owner = await _db.OpenOwnerAsync();
        await AddReportsPageAsync();

        await AssertRefusedAsync(
            () => owner.ExecuteAsync(
                "INSERT INTO menu_items (menu_key, page_key, label, icon, sort_order) VALUES ('member', 'reports', 'Reports', @icon, 30)", new { icon }),
            PostgresErrorCodes.CheckViolation, "ck_menu_items_icon");
    }

    [Theory]
    [InlineData("member", "/admin/anything", null, "ck_menu_pages_app_path")]
    [InlineData("member", "/admin", null, "ck_menu_pages_app_path")]
    [InlineData("member", "/", null, "ck_menu_pages_app_path")]
    [InlineData("admin", "/other", null, "ck_menu_pages_app_path")]
    [InlineData("admin", "/administrator", null, "ck_menu_pages_app_path")]
    [InlineData("admin", "/admin/extra", "data.read", "ck_menu_pages_admin_unguarded")]
    [InlineData("member", "/Reports", null, "ck_menu_pages_path")]
    [InlineData("member", "//evil.example", null, "ck_menu_pages_path")]
    [InlineData("member", "https://evil.example", null, "ck_menu_pages_path")]
    [InlineData("member", "/a b", null, "ck_menu_pages_path")]
    [InlineData("member", "/a?x=1", null, "ck_menu_pages_path")]
    [InlineData("member", "/a/", null, "ck_menu_pages_path")]
    [InlineData("member", "reports", null, "ck_menu_pages_path")]
    [InlineData("other", "/reports", null, "ck_menu_pages_app")]
    public async Task APagePath_IsAHostedInternalAddress_OfItsOwnApp(string app, string path, string? permission, string constraint)
    {
        await using var owner = await _db.OpenOwnerAsync();

        await AssertRefusedAsync(
            () => owner.ExecuteAsync(
                """
                INSERT INTO menu_pages (app, key, path, title, permission_key, default_label, default_icon, default_sort_order)
                VALUES (@app, 'probe', @path, 'Probe', @permission, 'Probe', 'home', 1)
                """,
                new { app, path, permission }),
            PostgresErrorCodes.CheckViolation, constraint);
    }

    // --- Restarts never undo the Site admin ---------------------------------------------------

    [Fact]
    public async Task RestartingTheApplication_DoesNotMoveThePermissionVersion()
    {
        var before = await VersionAsync();

        await _db.MigrateAsync();
        await _db.MigrateAsync();

        Assert.Equal(before, await VersionAsync());
    }

    [Fact]
    public async Task RestartingTheApplication_KeepsEveryChangeTheSiteAdminMade()
    {
        var admin = await CreateSiteAdminAsync();
        await using (var app = await _db.OpenAppAsync(null, admin))
        {
            await app.ExecuteScalarAsync<bool>("SELECT site_admin_set_role_permission('company', 'organizationadmin', 'workspaces.create', false)");
            await app.ExecuteScalarAsync<bool>("SELECT site_admin_set_role_permission('creator', 'contentwriter', 'workspaces.create', true)");
            await app.ExecuteScalarAsync<bool>("SELECT site_admin_delete_menu_item(i.id) FROM menu_items i WHERE i.menu_key = 'member' AND i.page_key = 'workspaces'");
            await app.ExecuteScalarAsync<bool>("SELECT site_admin_update_menu_item(i.id, 'Start', 'menu', null) FROM menu_items i WHERE i.menu_key = 'member' AND i.page_key = 'dashboard'");
        }

        var versionAfterEdits = await VersionAsync();

        await _db.MigrateAsync();
        await _db.MigrateAsync();

        await using var owner = await _db.OpenOwnerAsync();
        Assert.False(await IsGrantedAsync(owner, "company", "organizationadmin", "workspaces.create"));
        Assert.True(await IsGrantedAsync(owner, "creator", "contentwriter", "workspaces.create"));
        Assert.Equal(0, await DbTestSupport.CountAsync(owner, "menu_items WHERE menu_key = 'member' AND page_key = 'workspaces'"));
        Assert.Equal(("Start", "menu"), await owner.QuerySingleAsync<(string, string)>(
            "SELECT label, icon FROM menu_items WHERE menu_key = 'member' AND page_key = 'dashboard'"));
        Assert.Equal(versionAfterEdits, await VersionAsync());
        Assert.Equal(128, await DbTestSupport.CountAsync(owner, "role_permissions"));
    }

    [Fact]
    public async Task APageAddedByALaterScript_GetsItsItemOnce_AndADeletedItemStaysDeleted()
    {
        await using var owner = await _db.OpenOwnerAsync();

        // What a later numbered script would do: new rows with ON CONFLICT DO NOTHING, then the helper.
        const string laterScript =
            """
            INSERT INTO permissions (key, title, description) VALUES ('reports.view', 'View reports', 'See the reports.') ON CONFLICT (key) DO NOTHING;
            INSERT INTO role_permissions (business_model, role, permission_key, is_granted)
                VALUES ('company', 'organizationadmin', 'reports.view', true) ON CONFLICT DO NOTHING;
            INSERT INTO menu_pages (app, key, path, title, permission_key, default_label, default_icon, default_sort_order)
                VALUES ('member', 'reports', '/reports', 'Reports', 'reports.view', 'Reports', 'chart', 30) ON CONFLICT (app, key) DO NOTHING;
            SELECT menu_apply_default_items();
            """;

        await owner.ExecuteAsync(laterScript);
        await owner.ExecuteAsync(laterScript); // every start runs it again
        Assert.Equal(1, await DbTestSupport.CountAsync(owner, "menu_items WHERE menu_key = 'member' AND page_key = 'reports'"));

        await owner.ExecuteAsync("DELETE FROM menu_items WHERE menu_key = 'member' AND page_key = 'reports'");
        await owner.ExecuteAsync(laterScript);
        await _db.MigrateAsync();

        Assert.Equal(0, await DbTestSupport.CountAsync(owner, "menu_items WHERE menu_key = 'member' AND page_key = 'reports'"));
    }

    // --- The functions: who may call them and what they expose --------------------------------

    [Fact]
    public async Task TheFunctions_AreDefinerFunctionsWithAFixedSearchPath_NotCallableByPublic()
    {
        await using var owner = await _db.OpenOwnerAsync();

        foreach (var signature in SiteAdminWrites.Concat(CacheReads).Concat(["role_permissions_guard()", "permission_version_bump()"]))
        {
            var (isDefiner, config) = await owner.QuerySingleAsync<(bool, string)>(
                "SELECT prosecdef, array_to_string(proconfig, ',') FROM pg_proc WHERE oid = to_regprocedure(@signature)", new { signature });

            Assert.True(isDefiner, $"{signature} must be SECURITY DEFINER.");
            Assert.Contains("search_path=public, pg_temp", config);
            Assert.False(
                await owner.ExecuteScalarAsync<bool>("SELECT has_function_privilege('public', to_regprocedure(@signature), 'EXECUTE')", new { signature }),
                $"{signature} must not be callable by PUBLIC.");
        }
    }

    [Fact]
    public async Task AppUser_CanCallTheSiteAdminFunctionsAndTheSnapshots_ButNotTheTriggersOrTheDefaultsHelper()
    {
        await using var owner = await _db.OpenOwnerAsync();

        foreach (var signature in SiteAdminWrites.Concat(CacheReads))
        {
            Assert.True(
                await owner.ExecuteScalarAsync<bool>("SELECT has_function_privilege('app_user', to_regprocedure(@signature), 'EXECUTE')", new { signature }),
                $"app_user must be able to call {signature}.");
        }

        foreach (var signature in new[] { "role_permissions_guard()", "permission_version_bump()", "menu_apply_default_items()" })
        {
            Assert.False(
                await owner.ExecuteScalarAsync<bool>("SELECT has_function_privilege('app_user', to_regprocedure(@signature), 'EXECUTE')", new { signature }),
                $"app_user must not be able to call {signature}.");
        }
    }

    [Theory]
    [InlineData("SELECT site_admin_create_menu_item('member', 'workspaces', 'X', 'home', true)")]
    [InlineData("SELECT site_admin_update_menu_item(gen_random_uuid(), 'X', 'home', true)")]
    [InlineData("SELECT site_admin_delete_menu_item(gen_random_uuid())")]
    [InlineData("SELECT site_admin_reorder_menu('member', '[]'::jsonb)")]
    [InlineData("SELECT site_admin_set_role_permission('company', 'organizationadmin', 'data.read', true)")]
    public async Task EveryWriteFunction_RefusesAnOrdinaryUser_AndARequestWithNoUser(string call)
    {
        await using var owner = await _db.OpenOwnerAsync();
        var ordinaryUser = await DbTestSupport.InsertUserAsync(owner, $"{Guid.NewGuid():N}@example.com");
        var grantedBefore = await DbTestSupport.CountAsync(owner, "role_permissions WHERE is_granted");
        var versionBefore = await VersionAsync();

        await using var ordinary = await _db.OpenAppAsync(null, ordinaryUser);
        await AssertRefusedAsync(() => ordinary.ExecuteScalarAsync<object>(call), PostgresErrorCodes.InsufficientPrivilege);

        await using var anonymous = await _db.OpenAppAsync();
        await AssertRefusedAsync(() => anonymous.ExecuteScalarAsync<object>(call), PostgresErrorCodes.InsufficientPrivilege);

        // Nothing was written.
        Assert.Equal(grantedBefore, await DbTestSupport.CountAsync(owner, "role_permissions WHERE is_granted"));
        Assert.Equal(versionBefore, await VersionAsync());
        Assert.Equal(13, await DbTestSupport.CountAsync(owner, "menu_items"));
    }

    [Fact]
    public async Task TheSnapshots_NeedNoUser_AndReturnTheGrantsTheVersionAndTheMenu()
    {
        var version = await VersionAsync();
        await using var anonymous = await _db.OpenAppAsync();

        var grants = (await anonymous.QueryAsync<(long Version, string? Model, string? Role, string? Key, bool? Granted)>(
            "SELECT version, business_model, role, permission_key, is_granted FROM permission_snapshot()")).ToList();
        var menu = (await anonymous.QueryAsync<(string MenuKey, string PageKey, string Path, string Label, int SortOrder)>(
            "SELECT menu_key, page_key, path, label, sort_order FROM menu_snapshot()")).ToList();

        Assert.Equal(128, grants.Count);
        Assert.All(grants, g => Assert.Equal(version, g.Version));
        Assert.Equal(
            [
                "/admin", "/admin/users", "/admin/channels", "/admin/menus", "/admin/permissions",
                "/dashboard", "/content-studio", "/media-library", "/ai-studio", "/calendar", "/workspaces", "/platforms", "/team"
            ],
            menu.Select(m => m.Path));
    }

    [Fact]
    public async Task TheGrantSnapshot_StillReturnsTheVersion_WhenThereAreNoGrantsAtAll()
    {
        await using var owner = await _db.OpenOwnerAsync();
        await owner.ExecuteAsync("DELETE FROM role_permissions");
        var version = await VersionAsync();

        await using var anonymous = await _db.OpenAppAsync();
        var rows = (await anonymous.QueryAsync<(long Version, string? Model)>(
            "SELECT version, business_model FROM permission_snapshot()")).ToList();

        var row = Assert.Single(rows);
        Assert.Equal(version, row.Version);
        Assert.Null(row.Model);
    }

    // --- The functions at work ----------------------------------------------------------------

    [Fact]
    public async Task CreatingAnItem_AppendsItAfterTheOthers_TrimsTheLabel_AndShowsItByDefault()
    {
        await AddReportsPageAsync();
        await using var app = await _db.OpenAppAsync(null, await CreateSiteAdminAsync());

        var id = await app.ExecuteScalarAsync<Guid>(
            "SELECT site_admin_create_menu_item('member', 'reports', '  Our reports  ', 'chart', null)");

        var item = await app.QuerySingleAsync<(string Label, string Icon, int Sort, bool Visible, bool Locked)>(
            "SELECT label, icon, sort_order, is_visible, is_locked FROM menu_items WHERE id = @id", new { id });
        Assert.Equal(("Our reports", "chart", 40, true, false), item); // after Dashboard 10, Workspaces 20 and Team 30
    }

    [Fact]
    public async Task CreatingAnItem_RefusesAPageThatIsAlreadyThereAnUnknownPageAndABlankLabel()
    {
        await AddReportsPageAsync();
        await using var app = await _db.OpenAppAsync(null, await CreateSiteAdminAsync());

        await AssertRefusedAsync(
            () => app.ExecuteScalarAsync<Guid>("SELECT site_admin_create_menu_item('member', 'dashboard', 'Again', 'home', true)"),
            PostgresErrorCodes.UniqueViolation, "uq_menu_items_menu_page");
        await AssertRefusedAsync(
            () => app.ExecuteScalarAsync<Guid>("SELECT site_admin_create_menu_item('admin', 'workspaces', 'Wrong app', 'home', true)"),
            PostgresErrorCodes.ForeignKeyViolation, "fk_menu_items_page");
        await AssertRefusedAsync(
            () => app.ExecuteScalarAsync<Guid>("SELECT site_admin_create_menu_item('member', 'nope', 'Nothing', 'home', true)"),
            PostgresErrorCodes.ForeignKeyViolation, "fk_menu_items_page");
        await AssertRefusedAsync(
            () => app.ExecuteScalarAsync<Guid>("SELECT site_admin_create_menu_item('member', 'reports', '   ', 'chart', true)"),
            PostgresErrorCodes.CheckViolation, "ck_menu_items_label");
    }

    [Fact]
    public async Task UpdatingAnItem_ChangesLabelIconAndVisibility_ButNeverHidesALockedOne()
    {
        await using var app = await _db.OpenAppAsync(null, await CreateSiteAdminAsync());
        var workspaces = await app.ExecuteScalarAsync<Guid>("SELECT id FROM menu_items WHERE menu_key = 'member' AND page_key = 'workspaces'");
        var dashboard = await app.ExecuteScalarAsync<Guid>("SELECT id FROM menu_items WHERE menu_key = 'member' AND page_key = 'dashboard'");

        Assert.True(await app.ExecuteScalarAsync<bool>("SELECT site_admin_update_menu_item(@id, ' Spaces ', 'users', false)", new { id = workspaces }));
        Assert.Equal(("Spaces", "users", false), await app.QuerySingleAsync<(string, string, bool)>(
            "SELECT label, icon, is_visible FROM menu_items WHERE id = @id", new { id = workspaces }));

        // A null visibility leaves it as it is.
        Assert.True(await app.ExecuteScalarAsync<bool>("SELECT site_admin_update_menu_item(@id, 'Spaces', 'users', null)", new { id = workspaces }));
        Assert.False(await app.ExecuteScalarAsync<bool>("SELECT is_visible FROM menu_items WHERE id = @id", new { id = workspaces }));

        // A locked item can be renamed but not hidden.
        Assert.True(await app.ExecuteScalarAsync<bool>("SELECT site_admin_update_menu_item(@id, 'Home', 'home', true)", new { id = dashboard }));
        await AssertRefusedAsync(
            () => app.ExecuteScalarAsync<bool>("SELECT site_admin_update_menu_item(@id, 'Home', 'home', false)", new { id = dashboard }),
            PostgresErrorCodes.RaiseException, "menu_item_locked");
        Assert.Equal(("Home", true), await app.QuerySingleAsync<(string, bool)>(
            "SELECT label, is_visible FROM menu_items WHERE id = @id", new { id = dashboard }));

        Assert.False(await app.ExecuteScalarAsync<bool>("SELECT site_admin_update_menu_item(gen_random_uuid(), 'X', 'home', true)"));
    }

    [Fact]
    public async Task DeletingAnItem_RemovesAnUnlockedOne_RefusesALockedOne_AndReportsAMissingOne()
    {
        await using var app = await _db.OpenAppAsync(null, await CreateSiteAdminAsync());
        var workspaces = await app.ExecuteScalarAsync<Guid>("SELECT id FROM menu_items WHERE menu_key = 'member' AND page_key = 'workspaces'");
        var adminMenus = await app.ExecuteScalarAsync<Guid>("SELECT id FROM menu_items WHERE menu_key = 'admin' AND page_key = 'menus'");

        await AssertRefusedAsync(
            () => app.ExecuteScalarAsync<bool>("SELECT site_admin_delete_menu_item(@id)", new { id = adminMenus }),
            PostgresErrorCodes.RaiseException, "menu_item_locked");
        Assert.True(await app.ExecuteScalarAsync<bool>("SELECT site_admin_delete_menu_item(@id)", new { id = workspaces }));
        Assert.False(await app.ExecuteScalarAsync<bool>("SELECT site_admin_delete_menu_item(@id)", new { id = workspaces }));
        Assert.Equal(12, await DbTestSupport.CountAsync(app, "menu_items"));
    }

    [Fact]
    public async Task TheUsersAndChannelsItems_AreNotLocked_SoTheSiteAdminCanHideAndDeleteThem_ButTheRepairPagesAre()
    {
        await using var app = await _db.OpenAppAsync(null, await CreateSiteAdminAsync());
        var users = await app.ExecuteScalarAsync<Guid>("SELECT id FROM menu_items WHERE menu_key = 'admin' AND page_key = 'users'");
        var channels = await app.ExecuteScalarAsync<Guid>("SELECT id FROM menu_items WHERE menu_key = 'admin' AND page_key = 'channels'");

        Assert.True(await app.ExecuteScalarAsync<bool>("SELECT site_admin_update_menu_item(@id, 'People', 'users', false)", new { id = users }));
        Assert.True(await app.ExecuteScalarAsync<bool>("SELECT site_admin_delete_menu_item(@id)", new { id = channels }));
        Assert.Equal(
            ["Dashboard", "People", "Menus", "Permissions"],
            await app.QueryAsync<string>("SELECT label FROM menu_items WHERE menu_key = 'admin' ORDER BY sort_order"));
    }

    [Fact]
    public async Task Reordering_RewritesThePositions_AndRefusesAListThatIsNotExactlyTheMenu()
    {
        await using var app = await _db.OpenAppAsync(null, await CreateSiteAdminAsync());
        var ids = (await app.QueryAsync<Guid>("SELECT id FROM menu_items WHERE menu_key = 'admin' ORDER BY sort_order")).ToList();
        Assert.Equal(5, ids.Count);

        string Json(params Guid[] values) => "[" + string.Join(",", values.Select(v => $"\"{v}\"")) + "]";
        Task Reorder(string json) => app.ExecuteAsync("SELECT site_admin_reorder_menu('admin', @ids::jsonb)", new { ids = json });

        await Reorder(Json(ids.AsEnumerable().Reverse().ToArray()));
        Assert.Equal(
            ids.AsEnumerable().Reverse(),
            await app.QueryAsync<Guid>("SELECT id FROM menu_items WHERE menu_key = 'admin' ORDER BY sort_order"));
        Assert.Equal([10, 20, 30, 40, 50], await app.QueryAsync<int>("SELECT sort_order FROM menu_items WHERE menu_key = 'admin' ORDER BY sort_order"));

        var before = (await app.QueryAsync<Guid>("SELECT id FROM menu_items WHERE menu_key = 'admin' ORDER BY sort_order")).ToList();
        string[] mismatches =
        [
            Json(ids.Take(ids.Count - 1).ToArray()),                                // one missing
            Json([.. ids, Guid.NewGuid()]),                                         // one extra
            Json([ids[0], ids[0], .. ids.Skip(2)]),                                 // one twice, one missing
            Json([.. ids.Take(ids.Count - 1), Guid.NewGuid()]),                     // one unknown
            "[]",
            "{}",
            "\"text\"",
            "null"
        ];
        foreach (var json in mismatches)
        {
            await AssertRefusedAsync(() => Reorder(json), PostgresErrorCodes.RaiseException, "menu_order_mismatch");
        }

        // Nothing moved, and an id of another menu is not this menu's id either.
        Assert.Equal(before, await app.QueryAsync<Guid>("SELECT id FROM menu_items WHERE menu_key = 'admin' ORDER BY sort_order"));
        var memberId = await app.ExecuteScalarAsync<Guid>("SELECT id FROM menu_items WHERE menu_key = 'member' LIMIT 1");
        await AssertRefusedAsync(() => Reorder(Json([.. ids.Take(ids.Count - 1), memberId])), PostgresErrorCodes.RaiseException, "menu_order_mismatch");
        await AssertRefusedAsync(() => Reorder("[\"not-a-uuid\",\"b\",\"c\"]"), PostgresErrorCodes.InvalidTextRepresentation);
    }

    [Fact]
    public async Task SettingAGrant_ChangesIt_RaisesTheVersionOnce_AndReportsAnUnknownSlotOrKey()
    {
        await using var app = await _db.OpenAppAsync(null, await CreateSiteAdminAsync());
        var start = await VersionAsync();

        Assert.True(await app.ExecuteScalarAsync<bool>("SELECT site_admin_set_role_permission('company', 'organizationadmin', 'workspaces.create', false)"));
        Assert.Equal(start + 1, await VersionAsync());

        // Setting the same value again succeeds and changes nothing.
        Assert.True(await app.ExecuteScalarAsync<bool>("SELECT site_admin_set_role_permission('company', 'organizationadmin', 'workspaces.create', false)"));
        Assert.Equal(start + 1, await VersionAsync());

        // A slot that does not exist, a key that does not exist.
        Assert.False(await app.ExecuteScalarAsync<bool>("SELECT site_admin_set_role_permission('creator', 'organizationadmin', 'data.read', true)"));
        Assert.False(await app.ExecuteScalarAsync<bool>("SELECT site_admin_set_role_permission('company', 'organizationadmin', 'nope.nothing', true)"));
        Assert.Equal(start + 1, await VersionAsync());

        await AssertRefusedAsync(
            () => app.ExecuteScalarAsync<bool>("SELECT site_admin_set_role_permission('company', 'organizationadmin', 'data.read', null)"),
            PostgresErrorCodes.NullValueNotAllowed);
    }

    [Fact]
    public async Task SettingAGrant_CannotRevokeAProtectedPermission()
    {
        await using var app = await _db.OpenAppAsync(null, await CreateSiteAdminAsync());
        var start = await VersionAsync();

        await AssertRefusedAsync(
            () => app.ExecuteScalarAsync<bool>("SELECT site_admin_set_role_permission('agency', 'clientmanager', 'dashboard.view', false)"),
            PostgresErrorCodes.RaiseException, "permission_protected");

        Assert.Equal(start, await VersionAsync());
        await using var owner = await _db.OpenOwnerAsync();
        Assert.True(await IsGrantedAsync(owner, "agency", "clientmanager", "dashboard.view"));
    }
}
