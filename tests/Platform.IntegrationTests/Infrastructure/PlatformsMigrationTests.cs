using Dapper;
using Npgsql;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>
/// <c>012_member_platforms.sql</c>: the platforms.view permission and the Platform menu page, and the two functions a member reads the
/// channels through. A fifth part of <see cref="TeamMigrationTestsBase"/> (it needs the same arranging, and so runs on both a database
/// owned by a superuser and one owned by an ordinary role that row-level security binds).
/// </summary>
public abstract partial class TeamMigrationTestsBase
{
    private async Task<List<(Guid Id, string Name, bool HasImage, bool IsEnabled)>> PlatformsAsync(Guid agencyId, Guid userId)
    {
        await using var app = await Db.OpenAppAsync(agencyId, userId);
        return (await app.QueryAsync<(Guid, string, bool, bool)>(
            "SELECT id, name, has_image, is_enabled FROM member_list_platforms()")).ToList();
    }

    private async Task RevokePlatformsViewAsync(Guid admin, string model, string role)
    {
        await using var app = await Db.OpenAppAsync(null, admin);
        await app.ExecuteScalarAsync<bool>(
            "SELECT site_admin_set_role_permission(@model, @role, 'platforms.view', false)", new { model, role });
    }

    // --- The permission and the page ---------------------------------------------------------

    [Fact]
    public async Task EveryRoleOfEveryModel_HoldsPlatformsViewByDefault_AndThePlatformPageIsSeeded()
    {
        await using var owner = await Db.OpenOwnerAsync();

        Assert.Equal(8, await owner.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM role_permissions WHERE permission_key = 'platforms.view' AND is_granted"));
        Assert.False(await owner.ExecuteScalarAsync<bool>("SELECT is_protected FROM permissions WHERE key = 'platforms.view'"));
        Assert.Equal(("Platform", "list", "/platforms", 25, true, false), await owner.QuerySingleAsync<(string, string, string, int, bool, bool)>(
            """
            SELECT i.label, i.icon, p.path, i.sort_order, i.is_visible, i.is_locked
            FROM menu_items i JOIN menu_pages p ON p.app = i.menu_key AND p.key = i.page_key
            WHERE i.menu_key = 'member' AND i.page_key = 'platforms' AND p.permission_key = 'platforms.view'
            """));
    }

    [Fact]
    public async Task ASiteAdminsRevoke_IsNotUndoneByARestart_AndADeletedItemStaysDeleted()
    {
        var admin = await SiteAdminAsync();
        await RevokePlatformsViewAsync(admin, "company", "marketingmanager");
        await using (var app = await Db.OpenAppAsync(null, admin))
        {
            await app.ExecuteScalarAsync<bool>(
                "SELECT site_admin_delete_menu_item(i.id) FROM menu_items i WHERE i.menu_key = 'member' AND i.page_key = 'platforms'");
        }

        await Db.MigrateAsync();
        await Db.MigrateAsync();

        await using var owner = await Db.OpenOwnerAsync();
        Assert.False(await owner.ExecuteScalarAsync<bool>(
            "SELECT is_granted FROM role_permissions WHERE business_model = 'company' AND role = 'marketingmanager' AND permission_key = 'platforms.view'"));
        Assert.Equal(7, await owner.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM role_permissions WHERE permission_key = 'platforms.view' AND is_granted"));
        Assert.Equal(0, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM menu_items WHERE menu_key = 'member' AND page_key = 'platforms'"));
    }

    // --- The list ----------------------------------------------------------------------------

    [Theory]
    [InlineData("company")]
    [InlineData("agency")]
    [InlineData("creator")]
    public async Task AMemberOfAnyKindOfOrganization_SeesEveryChannel_NoneEnabledToStartWith(string model)
    {
        var org = await NewOrganizationAsync(model);

        var platforms = await PlatformsAsync(org.AgencyId, org.AdminId);

        Assert.Equal(SeededChannels, platforms.Select(p => p.Name));
        Assert.All(platforms, p =>
        {
            Assert.False(p.IsEnabled);
            Assert.False(p.HasImage);
        });
    }

    [Fact]
    public async Task AChannelTheSiteAdminEnabledShowsAsEnabled_ForThatOrganizationOnly()
    {
        var admin = await SiteAdminAsync();
        var one = await NewOrganizationAsync("company");
        var two = await NewOrganizationAsync("agency");
        var writer = await AddMemberAsync(one, "contentwriter");
        await SetChannelAsync(admin, one.AgencyId, await ChannelIdAsync(admin, "Email"), true);
        await SetChannelAsync(admin, one.AgencyId, await ChannelIdAsync(admin, "SMS"), true);

        Assert.Equal(["Email", "SMS"], (await PlatformsAsync(one.AgencyId, one.AdminId)).Where(p => p.IsEnabled).Select(p => p.Name));
        Assert.Equal(["Email", "SMS"], (await PlatformsAsync(one.AgencyId, writer)).Where(p => p.IsEnabled).Select(p => p.Name));
        Assert.DoesNotContain(await PlatformsAsync(two.AgencyId, two.AdminId), p => p.IsEnabled);

        await SetChannelAsync(admin, one.AgencyId, await ChannelIdAsync(admin, "SMS"), false);

        Assert.Equal(["Email"], (await PlatformsAsync(one.AgencyId, one.AdminId)).Where(p => p.IsEnabled).Select(p => p.Name));
    }

    [Fact]
    public async Task AChannelTheSiteAdminAdded_IsListedLast_WithItsImageFlag()
    {
        var admin = await SiteAdminAsync();
        var org = await NewOrganizationAsync("company");
        var id = await CreateChannelAsync(admin, "Telegram", "Fast chat", null, SmallPng, "image/png");

        var platforms = await PlatformsAsync(org.AgencyId, org.AdminId);

        Assert.Equal((id, "Telegram", true, false), platforms.Last());
        Assert.Equal(8, platforms.Count);
    }

    // --- The icon ----------------------------------------------------------------------------

    [Fact]
    public async Task TheIcon_IsTheStoredImage_AndNothingForABuiltInOrUnknownChannel()
    {
        var admin = await SiteAdminAsync();
        var org = await NewOrganizationAsync("creator");
        var custom = await CreateChannelAsync(admin, "Telegram", "Fast chat", null, SmallPng, "image/png");
        var builtIn = await ChannelIdAsync(admin, "Email");

        await using var app = await Db.OpenAppAsync(org.AgencyId, org.AdminId);

        Assert.Equal(("image/png", SmallPng), await app.QuerySingleAsync<(string, byte[])>(
            "SELECT content_type, image FROM member_platform_icon(@custom)", new { custom }));
        Assert.Empty(await app.QueryAsync("SELECT * FROM member_platform_icon(@builtIn)", new { builtIn }));
        Assert.Empty(await app.QueryAsync("SELECT * FROM member_platform_icon(@id)", new { id = Guid.NewGuid() }));
    }

    // --- Who is refused ----------------------------------------------------------------------

    [Fact]
    public async Task ARequestWithNoUserOrNoOrganization_IsRefused_ByBothFunctions()
    {
        var admin = await SiteAdminAsync();
        var org = await NewOrganizationAsync("company");
        var channel = await ChannelIdAsync(admin, "Email");

        foreach (var (agencyId, userId) in new (Guid?, Guid?)[] { (null, null), (null, org.AdminId), (org.AgencyId, null) })
        {
            await using var app = await Db.OpenAppAsync(agencyId, userId);
            await AssertRefusedAsync(() => app.QueryAsync("SELECT * FROM member_list_platforms()"), PostgresErrorCodes.InsufficientPrivilege);
            await AssertRefusedAsync(() => app.QueryAsync("SELECT * FROM member_platform_icon(@channel)", new { channel }), PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    [Fact]
    public async Task ASiteAdminWhoIsNotAMember_AndAMemberOfAnotherOrganization_AreRefused()
    {
        var admin = await SiteAdminAsync();
        var org = await NewOrganizationAsync("company");
        var other = await NewOrganizationAsync("company");

        // A Site admin acts with no organization; and a user cannot borrow an organization they do not belong to.
        await using (var asAdmin = await Db.OpenAppAsync(null, admin))
        {
            await AssertRefusedAsync(() => asAdmin.QueryAsync("SELECT * FROM member_list_platforms()"), PostgresErrorCodes.InsufficientPrivilege);
        }

        await AssertRefusedAsync(() => PlatformsAsync(org.AgencyId, other.AdminId), PostgresErrorCodes.InsufficientPrivilege);
        await AssertRefusedAsync(() => PlatformsAsync(other.AgencyId, org.AdminId), PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task AMemberWhoseRoleLostThePermission_IsRefused_AndOthersOfThatOrganizationAreNot()
    {
        var admin = await SiteAdminAsync();
        var org = await NewOrganizationAsync("company");
        var manager = await AddMemberAsync(org, "marketingmanager");
        var writer = await AddMemberAsync(org, "contentwriter");

        await RevokePlatformsViewAsync(admin, "company", "marketingmanager");

        await AssertRefusedAsync(() => PlatformsAsync(org.AgencyId, manager), PostgresErrorCodes.InsufficientPrivilege);
        Assert.Equal(7, (await PlatformsAsync(org.AgencyId, writer)).Count);
        Assert.Equal(7, (await PlatformsAsync(org.AgencyId, org.AdminId)).Count);
    }

    [Fact]
    public async Task ADeactivatedMember_AndAMemberOfADeactivatedOrganization_AreRefused_UntilTheyAreActiveAgain()
    {
        var admin = await SiteAdminAsync();
        var org = await NewOrganizationAsync("company");
        var writer = await AddMemberAsync(org, "contentwriter");

        await using (var owner = await Db.OpenOwnerAsync())
        {
            await owner.ExecuteAsync(
                "UPDATE agency_members SET is_active = false WHERE agency_id = @agencyId AND user_id = @writer", new { agencyId = org.AgencyId, writer });
        }

        await AssertRefusedAsync(() => PlatformsAsync(org.AgencyId, writer), PostgresErrorCodes.InsufficientPrivilege);
        Assert.Equal(7, (await PlatformsAsync(org.AgencyId, org.AdminId)).Count);

        await SetActiveAsync(admin, org.AgencyId, false);

        await AssertRefusedAsync(() => PlatformsAsync(org.AgencyId, org.AdminId), PostgresErrorCodes.InsufficientPrivilege);

        await SetActiveAsync(admin, org.AgencyId, true);

        Assert.Equal(7, (await PlatformsAsync(org.AgencyId, org.AdminId)).Count);
    }

    // --- The shape of the functions ----------------------------------------------------------

    [Fact]
    public async Task TheFunctions_AreDefiners_WithAFixedSearchPath_NotForPublic_AndOnlyTheTwoReadsAreForTheApplicationRole()
    {
        await using var owner = await Db.OpenOwnerAsync();

        var rows = (await owner.QueryAsync<(string Name, bool Definer, string? Config, bool App, bool Public)>(
            """
            SELECT p.proname, p.prosecdef, array_to_string(p.proconfig, ','),
                   has_function_privilege('app_user', p.oid, 'EXECUTE'), has_function_privilege('public', p.oid, 'EXECUTE')
            FROM pg_proc p
            WHERE p.pronamespace = 'public'::regnamespace
              AND p.proname IN ('platform_caller_allowed', 'member_list_platforms', 'member_platform_icon')
            ORDER BY p.proname
            """)).ToList();

        Assert.Equal(["member_list_platforms", "member_platform_icon", "platform_caller_allowed"], rows.Select(r => r.Name));
        Assert.All(rows, r =>
        {
            Assert.True(r.Definer, r.Name);
            Assert.Equal("search_path=public, pg_temp", r.Config);
            Assert.False(r.Public, r.Name);
        });
        Assert.Equal([true, true, false], rows.Select(r => r.App));
    }

    [Fact]
    public async Task TheApplicationRole_CannotReadTheChannelsTableDirectly()
    {
        var org = await NewOrganizationAsync("company");

        await using var app = await Db.OpenAppAsync(org.AgencyId, org.AdminId);

        await AssertRefusedAsync(() => app.QueryAsync("SELECT id FROM channels"), PostgresErrorCodes.InsufficientPrivilege);
        await AssertRefusedAsync(() => app.ExecuteAsync("INSERT INTO organization_channels (agency_id, channel_id) SELECT @agencyId, id FROM member_list_platforms()", new { agencyId = org.AgencyId }), PostgresErrorCodes.InsufficientPrivilege);
    }
}
