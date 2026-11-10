using Dapper;
using Npgsql;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>
/// <c>011_channels.sql</c>: the channels, which organization has which, deactivating an organization, and the invitation functions
/// that know about a deactivated organization. A fourth part of <see cref="TeamMigrationTestsBase"/> (it needs the same arranging, and
/// so runs on both a database owned by a superuser and one owned by an ordinary role that row-level security binds).
/// </summary>
public abstract partial class TeamMigrationTestsBase
{
    private static readonly byte[] SmallPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 8, 7];

    private static readonly string[] SeededChannels = ["Instagram", "Facebook", "YouTube", "Email", "SMS", "WhatsApp", "LinkedIn"];

    // A class with settable properties: Dapper matches columns to those, not to constructor parameters.
    private sealed class ChannelRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? IconKey { get; set; }
        public bool HasImage { get; set; }
        public string? CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; }
        public long OrganizationCount { get; set; }
    }

    private async Task<Guid> SiteAdminAsync(string name = "Root Admin")
    {
        await using var owner = await Db.OpenOwnerAsync();
        var id = await DbTestSupport.InsertUserAsync(owner, $"{Guid.NewGuid():N}@example.com", name);
        await DbTestSupport.InsertSiteAdminAsync(owner, id);
        return id;
    }

    private async Task<List<ChannelRow>> ChannelsAsync(Guid admin)
    {
        await using var app = await Db.OpenAppAsync(null, admin);
        return (await app.QueryAsync<ChannelRow>(
            "SELECT id, name, description, icon_key, has_image, created_by_name, created_at, organization_count FROM site_admin_list_channels()")).ToList();
    }

    private async Task<Guid> ChannelIdAsync(Guid admin, string name) => (await ChannelsAsync(admin)).Single(c => c.Name == name).Id;

    private async Task<Guid> CreateChannelAsync(
        Guid admin, string name, string description = "", string? iconKey = "broadcast", byte[]? image = null, string? contentType = null)
    {
        await using var app = await Db.OpenAppAsync(null, admin);
        return await app.ExecuteScalarAsync<Guid>(
            "SELECT id FROM site_admin_create_channel(@name, @description, @iconKey, @image, @contentType)",
            new { name, description, iconKey, image, contentType });
    }

    private async Task<bool> SetChannelAsync(Guid admin, Guid agencyId, Guid channelId, bool enabled)
    {
        await using var app = await Db.OpenAppAsync(null, admin);
        return await app.ExecuteScalarAsync<bool>("SELECT site_admin_set_organization_channel(@agencyId, @channelId, @enabled)", new { agencyId, channelId, enabled });
    }

    private async Task<bool> SetActiveAsync(Guid admin, Guid agencyId, bool active)
    {
        await using var app = await Db.OpenAppAsync(null, admin);
        return await app.ExecuteScalarAsync<bool>("SELECT site_admin_set_organization_active(@agencyId, @active)", new { agencyId, active });
    }

    private async Task<bool> IsActiveAsync(Guid agencyId)
    {
        await using var owner = await Db.OpenOwnerAsync();
        return await owner.ExecuteScalarAsync<bool>("SELECT is_active FROM agencies WHERE id = @agencyId", new { agencyId });
    }

    private async Task<List<(string Name, bool Enabled)>> OrganizationChannelsAsync(Guid admin, Guid agencyId)
    {
        await using var app = await Db.OpenAppAsync(null, admin);
        return (await app.QueryAsync<(string, bool)>(
            "SELECT name, is_enabled FROM site_admin_organization_channels(@agencyId)", new { agencyId })).ToList();
    }

    // --- The channels ------------------------------------------------------------------------

    [Fact]
    public async Task TheSevenChannelsAreSeeded_BuiltIn_InOrder_WithNoCreator_AndAStartNeverBringsThemBackOrDuplicatesThem()
    {
        var admin = await SiteAdminAsync();

        var channels = await ChannelsAsync(admin);

        Assert.Equal(SeededChannels, channels.Select(c => c.Name));
        Assert.Equal(["instagram", "facebook", "youtube", "email", "sms", "whatsapp", "linkedin"], channels.Select(c => c.IconKey));
        Assert.All(channels, c =>
        {
            Assert.False(c.HasImage);
            Assert.Null(c.CreatedByName);
            Assert.NotEmpty(c.Description);
            Assert.Equal(0, c.OrganizationCount);
        });

        await CreateChannelAsync(admin, "Telegram");
        await Db.MigrateAsync();
        await Db.MigrateAsync();

        Assert.Equal([.. SeededChannels, "Telegram"], (await ChannelsAsync(admin)).Select(c => c.Name));
    }

    [Fact]
    public async Task CreatingAChannel_StoresTheImage_NamesTheCreator_AndPutsItAfterTheOthers()
    {
        var admin = await SiteAdminAsync("Rama Admin");

        var id = await CreateChannelAsync(admin, "  Telegram  ", "Fast chat", null, SmallPng, "image/png");

        var channel = (await ChannelsAsync(admin)).Last();
        Assert.Equal((id, "Telegram", "Fast chat", (string?)null, true, "Rama Admin"), (channel.Id, channel.Name, channel.Description, channel.IconKey, channel.HasImage, channel.CreatedByName));
        await using var app = await Db.OpenAppAsync(null, admin);
        var icon = await app.QuerySingleAsync<(string ContentType, byte[] Image)>("SELECT content_type, image FROM site_admin_channel_icon(@id)", new { id });
        Assert.Equal(("image/png", SmallPng), icon);
    }

    [Fact]
    public async Task ACreatorWithNoName_IsShownByAddress()
    {
        var admin = await SiteAdminAsync(name: "");
        await CreateChannelAsync(admin, "Telegram");

        var channel = (await ChannelsAsync(admin)).Last();

        Assert.Contains("@example.com", channel.CreatedByName);
    }

    [Fact]
    public async Task ABuiltInChannel_HasNoImageToServe_AndNeitherDoesAnUnknownChannel()
    {
        var admin = await SiteAdminAsync();
        var email = await ChannelIdAsync(admin, "Email");

        await using var app = await Db.OpenAppAsync(null, admin);
        Assert.Empty(await app.QueryAsync("SELECT * FROM site_admin_channel_icon(@id)", new { id = email }));
        Assert.Empty(await app.QueryAsync("SELECT * FROM site_admin_channel_icon(@id)", new { id = Guid.NewGuid() }));
    }

    [Fact]
    public async Task AChannelName_IsUnique_WhateverTheLetterCase_AndStoredTrimmed()
    {
        var admin = await SiteAdminAsync();
        await CreateChannelAsync(admin, "Telegram");

        await AssertRefusedAsync(() => CreateChannelAsync(admin, "telegram"), PostgresErrorCodes.UniqueViolation, "ux_channels_name");
        await AssertRefusedAsync(() => CreateChannelAsync(admin, "  TELEGRAM "), PostgresErrorCodes.UniqueViolation, "ux_channels_name");
        // The seeded names count too.
        await AssertRefusedAsync(() => CreateChannelAsync(admin, "email"), PostgresErrorCodes.UniqueViolation, "ux_channels_name");
        Assert.Single(await ChannelsAsync(admin), c => c.Name == "Telegram");
    }

    [Fact]
    public async Task TheDatabaseRefusesABadChannel_WhateverTheApiChecked()
    {
        var admin = await SiteAdminAsync();

        await AssertRefusedAsync(() => CreateChannelAsync(admin, ""), PostgresErrorCodes.CheckViolation, "ck_channels_name");
        await AssertRefusedAsync(() => CreateChannelAsync(admin, "   "), PostgresErrorCodes.CheckViolation, "ck_channels_name");
        await AssertRefusedAsync(() => CreateChannelAsync(admin, new string('a', 61)), PostgresErrorCodes.CheckViolation, "ck_channels_name");
        await AssertRefusedAsync(() => CreateChannelAsync(admin, "Line\nbreak"), PostgresErrorCodes.CheckViolation, "ck_channels_name");
        await AssertRefusedAsync(() => CreateChannelAsync(admin, "Fine", new string('a', 301)), PostgresErrorCodes.CheckViolation, "ck_channels_description");
        await AssertRefusedAsync(() => CreateChannelAsync(admin, "Fine", "one\ntwo"), PostgresErrorCodes.CheckViolation, "ck_channels_description");
        await AssertRefusedAsync(() => CreateChannelAsync(admin, "Fine", "", "Not A Key"), PostgresErrorCodes.CheckViolation, "ck_channels_icon_key");
        await AssertRefusedAsync(() => CreateChannelAsync(admin, "Fine", "", null, [], "image/png"), PostgresErrorCodes.CheckViolation, "ck_channels_icon_image");
        await AssertRefusedAsync(() => CreateChannelAsync(admin, "Fine", "", null, new byte[102_401], "image/png"), PostgresErrorCodes.CheckViolation, "ck_channels_icon_image");
        await AssertRefusedAsync(() => CreateChannelAsync(admin, "Fine", "", null, SmallPng, "image/gif"), PostgresErrorCodes.CheckViolation, "ck_channels_icon_type");
        await AssertRefusedAsync(() => CreateChannelAsync(admin, "Fine", "", null, SmallPng, "image/svg+xml"), PostgresErrorCodes.CheckViolation, "ck_channels_icon_type");
        // A key or an image, never both and never neither; an image needs its type.
        await AssertRefusedAsync(() => CreateChannelAsync(admin, "Fine", "", "broadcast", SmallPng, "image/png"), PostgresErrorCodes.CheckViolation, "ck_channels_icon_one_of");
        await AssertRefusedAsync(() => CreateChannelAsync(admin, "Fine", "", null, null, null), PostgresErrorCodes.CheckViolation, "ck_channels_icon_one_of");
        await AssertRefusedAsync(() => CreateChannelAsync(admin, "Fine", "", null, SmallPng, null), PostgresErrorCodes.CheckViolation, "ck_channels_icon_one_of");

        // The longest everything is accepted, including an image of exactly 100 KB.
        await CreateChannelAsync(admin, new string('a', 60), new string('b', 300), null, new byte[102_400], "image/webp");
        Assert.Equal(SeededChannels.Length + 1, (await ChannelsAsync(admin)).Count);
    }

    // --- Who may ------------------------------------------------------------------------------

    [Fact]
    public async Task TheApplicationRole_HasNoAccessToTheChannelsTable_NotEvenASiteAdmin_AndOnlyReadsTheMapping()
    {
        var org = await NewOrganizationAsync("company");
        var admin = await SiteAdminAsync();
        await using var owner = await Db.OpenOwnerAsync();

        foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
        {
            Assert.False(
                await owner.ExecuteScalarAsync<bool>("SELECT has_table_privilege('app_user', 'channels', @privilege)", new { privilege }),
                $"app_user must not have {privilege} on channels.");
        }

        Assert.True(await owner.ExecuteScalarAsync<bool>("SELECT has_table_privilege('app_user', 'organization_channels', 'SELECT')"));
        foreach (var privilege in new[] { "INSERT", "UPDATE", "DELETE" })
        {
            Assert.False(
                await owner.ExecuteScalarAsync<bool>("SELECT has_table_privilege('app_user', 'organization_channels', @privilege)", new { privilege }),
                $"app_user must not have {privilege} on organization_channels.");
        }

        await using var asMember = await AsAsync(org.AgencyId, org.AdminId);
        await using var asSiteAdmin = await Db.OpenAppAsync(null, admin);
        foreach (var connection in new[] { asMember, asSiteAdmin })
        {
            await AssertRefusedAsync(() => connection.QueryAsync("SELECT * FROM channels"), PostgresErrorCodes.InsufficientPrivilege);
            await AssertRefusedAsync(() => connection.ExecuteAsync("INSERT INTO channels (name, icon_key) VALUES ('Sneaky', 'broadcast')"), PostgresErrorCodes.InsufficientPrivilege);
            await AssertRefusedAsync(() => connection.ExecuteAsync("UPDATE channels SET name = 'Hijacked'"), PostgresErrorCodes.InsufficientPrivilege);
            await AssertRefusedAsync(() => connection.ExecuteAsync("DELETE FROM channels"), PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    public static TheoryData<string> SiteAdminCalls() => new()
    {
        "SELECT * FROM site_admin_list_channels()",
        "SELECT * FROM site_admin_create_channel('Sneaky', '', 'broadcast', NULL, NULL)",
        "SELECT * FROM site_admin_channel_icon(gen_random_uuid())",
        "SELECT * FROM site_admin_organization_channels(gen_random_uuid())",
        "SELECT site_admin_set_organization_channel(gen_random_uuid(), gen_random_uuid(), true)",
        "SELECT * FROM site_admin_list_agencies_v3(10, 0)",
        "SELECT site_admin_set_organization_active(gen_random_uuid(), false)"
    };

    [Theory]
    [MemberData(nameof(SiteAdminCalls))]
    public async Task EveryChannelFunction_RefusesAMemberAndAnAnonymousCaller_AndWritesNothing(string call)
    {
        var org = await NewOrganizationAsync("company");

        await using var asMember = await AsAsync(org.AgencyId, org.AdminId);
        await AssertRefusedAsync(() => asMember.QueryAsync(call), PostgresErrorCodes.InsufficientPrivilege);

        await using var anonymous = await Db.OpenAppAsync();
        await AssertRefusedAsync(() => anonymous.QueryAsync(call), PostgresErrorCodes.InsufficientPrivilege);

        await using var owner = await Db.OpenOwnerAsync();
        Assert.Equal(SeededChannels.Length, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM channels"));
        Assert.True(await IsActiveAsync(org.AgencyId));
    }

    [Fact]
    public async Task ADemotedSiteAdmin_IsRefusedAtOnce()
    {
        var admin = await SiteAdminAsync();
        Assert.NotEmpty(await ChannelsAsync(admin));

        await using (var owner = await Db.OpenOwnerAsync())
        {
            await owner.ExecuteAsync("DELETE FROM site_admins WHERE user_id = @admin", new { admin });
        }

        await using var app = await Db.OpenAppAsync(null, admin);
        await AssertRefusedAsync(() => app.QueryAsync("SELECT * FROM site_admin_list_channels()"), PostgresErrorCodes.InsufficientPrivilege);
    }

    // --- Which organization has which channel -------------------------------------------------

    [Fact]
    public async Task AChannelCanBeEnabledAndDisabled_ForAnyKindOfOrganization_AndRepeatingACallChangesNothing()
    {
        var admin = await SiteAdminAsync();
        var email = await ChannelIdAsync(admin, "Email");
        var company = await NewOrganizationAsync("company");
        var agency = await NewOrganizationAsync("agency");
        var creator = await NewOrganizationAsync("creator");

        foreach (var org in new[] { company, agency, creator })
        {
            Assert.True(await SetChannelAsync(admin, org.AgencyId, email, true));
            Assert.False(await SetChannelAsync(admin, org.AgencyId, email, true));
            Assert.Equal(("Email", true), (await OrganizationChannelsAsync(admin, org.AgencyId)).Single(c => c.Name == "Email"));
        }

        Assert.Equal(3, (await ChannelsAsync(admin)).Single(c => c.Name == "Email").OrganizationCount);

        Assert.True(await SetChannelAsync(admin, agency.AgencyId, email, false));
        Assert.False(await SetChannelAsync(admin, agency.AgencyId, email, false));
        Assert.Equal(2, (await ChannelsAsync(admin)).Single(c => c.Name == "Email").OrganizationCount);
        Assert.False((await OrganizationChannelsAsync(admin, agency.AgencyId)).Single(c => c.Name == "Email").Enabled);
        Assert.True((await OrganizationChannelsAsync(admin, company.AgencyId)).Single(c => c.Name == "Email").Enabled);
    }

    [Fact]
    public async Task AnOrganizationsChannelList_HasEveryChannel_InOrder_WithOnlyItsOwnEnabled()
    {
        var admin = await SiteAdminAsync();
        var a = await NewOrganizationAsync("company");
        var b = await NewOrganizationAsync("company");
        var telegram = await CreateChannelAsync(admin, "Telegram");
        await SetChannelAsync(admin, a.AgencyId, await ChannelIdAsync(admin, "SMS"), true);
        await SetChannelAsync(admin, a.AgencyId, telegram, true);
        await SetChannelAsync(admin, b.AgencyId, await ChannelIdAsync(admin, "YouTube"), true);

        var forA = await OrganizationChannelsAsync(admin, a.AgencyId);
        var forB = await OrganizationChannelsAsync(admin, b.AgencyId);

        Assert.Equal([.. SeededChannels, "Telegram"], forA.Select(c => c.Name));
        Assert.Equal(["SMS", "Telegram"], forA.Where(c => c.Enabled).Select(c => c.Name));
        Assert.Equal(["YouTube"], forB.Where(c => c.Enabled).Select(c => c.Name));
    }

    [Fact]
    public async Task AnUnknownOrganizationOrChannel_IsARuleError_AndANullChoiceIsRefused()
    {
        var admin = await SiteAdminAsync();
        var org = await NewOrganizationAsync("company");
        var email = await ChannelIdAsync(admin, "Email");
        await using var app = await Db.OpenAppAsync(null, admin);

        await AssertRuleAsync(() => app.QueryAsync("SELECT * FROM site_admin_organization_channels(@id)", new { id = Guid.NewGuid() }), "organization_not_found");
        await AssertRuleAsync(() => SetChannelAsync(admin, Guid.NewGuid(), email, true), "organization_not_found");
        await AssertRuleAsync(() => SetChannelAsync(admin, org.AgencyId, Guid.NewGuid(), true), "channel_not_found");
        await AssertRefusedAsync(
            () => app.ExecuteScalarAsync<bool>("SELECT site_admin_set_organization_channel(@a, @c, NULL)", new { a = org.AgencyId, c = email }),
            PostgresErrorCodes.InvalidParameterValue);
    }

    [Fact]
    public async Task TheMapping_IsIsolatedPerOrganization_AndAMemberCanOnlyReadTheirOwn()
    {
        var admin = await SiteAdminAsync();
        var a = await NewOrganizationAsync("company");
        var b = await NewOrganizationAsync("agency");
        var email = await ChannelIdAsync(admin, "Email");
        var sms = await ChannelIdAsync(admin, "SMS");
        await SetChannelAsync(admin, a.AgencyId, email, true);
        await SetChannelAsync(admin, b.AgencyId, email, true);
        await SetChannelAsync(admin, b.AgencyId, sms, true);

        await using var asA = await AsAsync(a.AgencyId, a.AdminId);
        await using var asB = await AsAsync(b.AgencyId, b.AdminId);
        await using var nobody = await Db.OpenAppAsync();

        Assert.Equal([email], await asA.QueryAsync<Guid>("SELECT channel_id FROM organization_channels"));
        Assert.Equal(2, await DbTestSupport.CountAsync(asB, "organization_channels"));
        Assert.Equal(0, await DbTestSupport.CountAsync(nobody, "organization_channels"));

        // Read only: not even their own rows can be written or taken away, and nothing can be added for another organization.
        await AssertRefusedAsync(() => asA.ExecuteAsync("DELETE FROM organization_channels"), PostgresErrorCodes.InsufficientPrivilege);
        await AssertRefusedAsync(() => asA.ExecuteAsync("UPDATE organization_channels SET channel_id = @sms", new { sms }), PostgresErrorCodes.InsufficientPrivilege);
        await AssertRefusedAsync(
            () => asA.ExecuteAsync("INSERT INTO organization_channels (agency_id, channel_id) VALUES (@a, @sms)", new { a = a.AgencyId, sms }),
            PostgresErrorCodes.InsufficientPrivilege);
        await AssertRefusedAsync(
            () => asA.ExecuteAsync("INSERT INTO organization_channels (agency_id, channel_id) VALUES (@b, @sms)", new { b = b.AgencyId, sms }),
            PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task AnOrganizationHasAChannelOnce_AndWhoAssignedItIsKept()
    {
        var admin = await SiteAdminAsync();
        var org = await NewOrganizationAsync("company");
        var email = await ChannelIdAsync(admin, "Email");
        await SetChannelAsync(admin, org.AgencyId, email, true);

        await using var owner = await Db.OpenOwnerAsync();
        Assert.Equal(admin, await owner.ExecuteScalarAsync<Guid>("SELECT assigned_by FROM organization_channels WHERE agency_id = @a", new { a = org.AgencyId }));
        await AssertRefusedAsync(
            () => owner.ExecuteAsync("INSERT INTO organization_channels (agency_id, channel_id) VALUES (@a, @email)", new { a = org.AgencyId, email }),
            PostgresErrorCodes.UniqueViolation, "uq_organization_channels");
    }

    [Fact]
    public async Task DeletingAChannel_TakesItsAssignmentsWithIt_AndDeletingItsCreator_OnlyForgetsWhoMadeIt()
    {
        var admin = await SiteAdminAsync();
        var org = await NewOrganizationAsync("company");
        var telegram = await CreateChannelAsync(admin, "Telegram");
        var email = await ChannelIdAsync(admin, "Email");
        await SetChannelAsync(admin, org.AgencyId, telegram, true);
        await SetChannelAsync(admin, org.AgencyId, email, true);

        await using var owner = await Db.OpenOwnerAsync();
        await owner.ExecuteAsync("DELETE FROM channels WHERE id = @telegram", new { telegram });
        Assert.Equal([email], await owner.QueryAsync<Guid>("SELECT channel_id FROM organization_channels WHERE agency_id = @a", new { a = org.AgencyId }));

        var other = await CreateChannelAsync(admin, "Signal");
        await owner.ExecuteAsync("DELETE FROM site_admins WHERE user_id = @admin", new { admin });
        await owner.ExecuteAsync("DELETE FROM users WHERE id = @admin", new { admin });
        Assert.Null(await owner.ExecuteScalarAsync<Guid?>("SELECT created_by FROM channels WHERE id = @other", new { other }));
        Assert.Null(await owner.ExecuteScalarAsync<Guid?>("SELECT assigned_by FROM organization_channels WHERE channel_id = @email", new { email }));
        Assert.Equal(1, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM channels WHERE id = @other", new { other }));
    }

    // --- The list of organizations ------------------------------------------------------------

    [Fact]
    public async Task TheOrganizationList_ShowsWhetherEachIsActive_AndHowManyChannelsItHas()
    {
        var admin = await SiteAdminAsync();
        var a = await NewOrganizationAsync("company");
        var b = await NewOrganizationAsync("agency");
        await SetChannelAsync(admin, a.AgencyId, await ChannelIdAsync(admin, "Email"), true);
        await SetChannelAsync(admin, a.AgencyId, await ChannelIdAsync(admin, "SMS"), true);
        await SetActiveAsync(admin, b.AgencyId, false);

        await using var app = await Db.OpenAppAsync(null, admin);
        var rows = (await app.QueryAsync<(Guid Id, bool IsActive, long ChannelCount, long MemberCount, long Total)>(
            "SELECT id, is_active, channel_count, member_count, total_count FROM site_admin_list_agencies_v3(20, 0)")).ToList();

        Assert.Equal((true, 2L, 1L, 2L), (rows.Single(r => r.Id == a.AgencyId).IsActive, rows.Single(r => r.Id == a.AgencyId).ChannelCount, rows.Single(r => r.Id == a.AgencyId).MemberCount, rows.Count));
        Assert.Equal((false, 0L), (rows.Single(r => r.Id == b.AgencyId).IsActive, rows.Single(r => r.Id == b.AgencyId).ChannelCount));
        await AssertRefusedAsync(() => app.QueryAsync("SELECT * FROM site_admin_list_agencies_v3(0, 0)"), PostgresErrorCodes.InvalidParameterValue);
        await AssertRefusedAsync(() => app.QueryAsync("SELECT * FROM site_admin_list_agencies_v3(101, 0)"), PostgresErrorCodes.InvalidParameterValue);
        await AssertRefusedAsync(() => app.QueryAsync("SELECT * FROM site_admin_list_agencies_v3(10, -1)"), PostgresErrorCodes.InvalidParameterValue);
    }

    // --- Deactivating an organization ---------------------------------------------------------

    [Fact]
    public async Task EveryOrganizationStartsActive()
    {
        var org = await NewOrganizationAsync("company");

        Assert.True(await IsActiveAsync(org.AgencyId));
    }

    [Fact]
    public async Task Deactivating_EndsEveryMembersSessions_OfThatOrganizationOnly_AndKeepsTheirRows()
    {
        var admin = await SiteAdminAsync();
        var org = await NewOrganizationAsync("company");
        var other = await NewOrganizationAsync("company");
        var writer = await AddMemberAsync(org, "contentwriter");
        var manager = await AddMemberAsync(org, "marketingmanager");
        var before = DateTime.UtcNow;

        Assert.True(await SetActiveAsync(admin, org.AgencyId, false));

        var after = DateTime.UtcNow;
        Assert.False(await IsActiveAsync(org.AgencyId));
        foreach (var userId in new[] { org.AdminId, writer, manager })
        {
            var validAfter = await RevokedAfterAsync(org.AgencyId, userId);
            Assert.NotNull(validAfter);
            Assert.InRange(validAfter.Value, before.AddSeconds(-1), after.AddSeconds(1));
        }

        Assert.Null(await RevokedAfterAsync(other.AgencyId, other.AdminId));
        Assert.True(await IsActiveAsync(other.AgencyId));
        // Nothing is deleted: the people and their roles are still there.
        Assert.Equal("contentwriter", await RoleOfAsync(org.AgencyId, writer));
        Assert.Equal("organizationadmin", await RoleOfAsync(org.AgencyId, org.AdminId));
    }

    [Fact]
    public async Task DeactivatingAgain_ChangesNothing_AndActivatingAgainEndsNoSessionAndTouchesNoRevocation()
    {
        var admin = await SiteAdminAsync();
        var org = await NewOrganizationAsync("company");

        Assert.True(await SetActiveAsync(admin, org.AgencyId, false));
        var first = await RevokedAfterAsync(org.AgencyId, org.AdminId);
        Assert.False(await SetActiveAsync(admin, org.AgencyId, false));
        Assert.Equal(first, await RevokedAfterAsync(org.AgencyId, org.AdminId));

        Assert.True(await SetActiveAsync(admin, org.AgencyId, true));
        Assert.False(await SetActiveAsync(admin, org.AgencyId, true));
        Assert.True(await IsActiveAsync(org.AgencyId));
        Assert.Equal(first, await RevokedAfterAsync(org.AgencyId, org.AdminId));
    }

    [Fact]
    public async Task DeactivatingAnUnknownOrganization_IsARuleError_AndANullChoiceIsRefused()
    {
        var admin = await SiteAdminAsync();
        var org = await NewOrganizationAsync("company");
        await using var app = await Db.OpenAppAsync(null, admin);

        await AssertRuleAsync(() => SetActiveAsync(admin, Guid.NewGuid(), false), "organization_not_found");
        await AssertRefusedAsync(
            () => app.ExecuteScalarAsync<bool>("SELECT site_admin_set_organization_active(@a, NULL)", new { a = org.AgencyId }),
            PostgresErrorCodes.InvalidParameterValue);
        Assert.True(await IsActiveAsync(org.AgencyId));
    }

    [Fact]
    public async Task ADeactivatedOrganization_KeepsItsChannels_AndTheirAssignmentsAreStillEditable()
    {
        var admin = await SiteAdminAsync();
        var org = await NewOrganizationAsync("company");
        var email = await ChannelIdAsync(admin, "Email");
        await SetChannelAsync(admin, org.AgencyId, email, true);

        await SetActiveAsync(admin, org.AgencyId, false);

        Assert.True((await OrganizationChannelsAsync(admin, org.AgencyId)).Single(c => c.Name == "Email").Enabled);
        Assert.True(await SetChannelAsync(admin, org.AgencyId, await ChannelIdAsync(admin, "SMS"), true));
    }

    [Fact]
    public async Task AMemberCannotDeactivateTheirOwnOrganization_NotEvenTheAdmin()
    {
        var org = await NewOrganizationAsync("company");
        await using var asAdmin = await AsAsync(org.AgencyId, org.AdminId);

        await AssertRefusedAsync(
            () => asAdmin.ExecuteScalarAsync<bool>("SELECT site_admin_set_organization_active(@a, false)", new { a = org.AgencyId }),
            PostgresErrorCodes.InsufficientPrivilege);
        // Not by SQL either: the tenant policy would let them touch their own row, the column privilege does not let them touch this column.
        await AssertRefusedAsync(
            () => asAdmin.ExecuteAsync("UPDATE agencies SET is_active = false WHERE id = @a", new { a = org.AgencyId }),
            PostgresErrorCodes.InsufficientPrivilege);
        Assert.True(await IsActiveAsync(org.AgencyId));

        // What the application role could always change on its own organization it still can.
        Assert.Equal(1, await asAdmin.ExecuteAsync("UPDATE agencies SET name = 'Renamed' WHERE id = @a", new { a = org.AgencyId }));
    }

    [Fact]
    public async Task ADeactivatedOrganization_CannotBeReactivatedByItsOwnMembers_BySql()
    {
        var admin = await SiteAdminAsync();
        var org = await NewOrganizationAsync("company");
        await SetActiveAsync(admin, org.AgencyId, false);

        await using var asAdmin = await AsAsync(org.AgencyId, org.AdminId);
        await AssertRefusedAsync(
            () => asAdmin.ExecuteAsync("UPDATE agencies SET is_active = true WHERE id = @a", new { a = org.AgencyId }),
            PostgresErrorCodes.InsufficientPrivilege);
        Assert.False(await IsActiveAsync(org.AgencyId));
    }

    // --- Invitations into a deactivated organization ------------------------------------------

    [Fact]
    public async Task AnInvitationIntoADeactivatedOrganization_StopsWorking_AndWorksAgainWhenItIsActivated()
    {
        var admin = await SiteAdminAsync();
        var org = await NewOrganizationAsync("company");
        var hash = await InviteAsync(org, "waiting@example.com", "contentwriter");
        await using var nobody = await Db.OpenAppAsync();
        Assert.Single(await nobody.QueryAsync("SELECT * FROM invitation_find_v3(@hash)", new { hash }));

        await SetActiveAsync(admin, org.AgencyId, false);

        Assert.Empty(await nobody.QueryAsync("SELECT * FROM invitation_find_v3(@hash)", new { hash }));
        await AssertRuleAsync(() => AcceptAsync(hash), "invitation_invalid");
        await using (var owner = await Db.OpenOwnerAsync())
        {
            Assert.Equal(0, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM users WHERE email = 'waiting@example.com'"));
            Assert.Null(await owner.ExecuteScalarAsync<DateTime?>("SELECT accepted_at FROM member_invitations WHERE token_hash = @hash", new { hash }));
        }

        await SetActiveAsync(admin, org.AgencyId, true);

        Assert.Single(await nobody.QueryAsync("SELECT * FROM invitation_find_v3(@hash)", new { hash }));
        var (agencyId, role) = await AcceptAsync(hash);
        Assert.Equal((org.AgencyId, "contentwriter"), (agencyId, role));
    }

    [Fact]
    public async Task AnInvitationThatDoesNotExist_StillAnswersTheSameInvalidError_OnTheNewAcceptFunction()
    {
        await AssertRuleAsync(() => AcceptAsync("no-such-hash"), "invitation_invalid");
    }

    // --- The functions and policies themselves ------------------------------------------------

    private static readonly string[] ChannelFunctionsCallableByApp =
    [
        "site_admin_list_channels()", "site_admin_create_channel(text, text, text, bytea, text)", "site_admin_channel_icon(uuid)",
        "site_admin_organization_channels(uuid)", "site_admin_set_organization_channel(uuid, uuid, boolean)",
        "site_admin_list_agencies_v3(integer, integer)", "site_admin_set_organization_active(uuid, boolean)"
    ];

    private static readonly string[] ChannelFunctionsInternal = ["site_admin_require()", "channel_rows(uuid)"];

    [Fact]
    public async Task TheChannelFunctions_AreDefinerFunctionsWithAFixedSearchPath_NotCallableByPublic_AndOnlyTheRightOnesByAppUser()
    {
        await using var owner = await Db.OpenOwnerAsync();

        foreach (var signature in ChannelFunctionsCallableByApp.Concat(ChannelFunctionsInternal))
        {
            var (isDefiner, config) = await owner.QuerySingleAsync<(bool, string)>(
                "SELECT prosecdef, array_to_string(proconfig, ',') FROM pg_proc WHERE oid = to_regprocedure(@signature)", new { signature });

            Assert.True(isDefiner, $"{signature} must be SECURITY DEFINER.");
            Assert.Contains("search_path=public, pg_temp", config);
            Assert.False(
                await owner.ExecuteScalarAsync<bool>("SELECT has_function_privilege('public', to_regprocedure(@signature), 'EXECUTE')", new { signature }),
                $"{signature} must not be callable by PUBLIC.");
        }

        foreach (var signature in ChannelFunctionsCallableByApp)
        {
            Assert.True(
                await owner.ExecuteScalarAsync<bool>("SELECT has_function_privilege('app_user', to_regprocedure(@signature), 'EXECUTE')", new { signature }),
                $"app_user must be able to call {signature}.");
        }

        foreach (var signature in ChannelFunctionsInternal)
        {
            Assert.False(
                await owner.ExecuteScalarAsync<bool>("SELECT has_function_privilege('app_user', to_regprocedure(@signature), 'EXECUTE')", new { signature }),
                $"app_user must not be able to call {signature}.");
        }
    }

    [Fact]
    public async Task TheOwnerPolicies_OnTheChannelTablesAndAgencies_BelongToTheMigratingRoleOnly_AndRowLevelSecurityIsEnabledAndForced()
    {
        await using var owner = await Db.OpenOwnerAsync();
        var policies = (await owner.QueryAsync<(string Table, string Name, string[] Roles)>(
            """
            SELECT tablename, policyname, roles::text[]
            FROM pg_policies
            WHERE (tablename IN ('channels', 'organization_channels') AND policyname = 'owner_manage')
               OR (tablename = 'agencies' AND policyname = 'owner_site_update')
            """)).ToList();

        Assert.Equal(["agencies", "channels", "organization_channels"], policies.Select(p => p.Table).Order());
        var current = (await owner.ExecuteScalarAsync<string>("SELECT current_user"))!;
        Assert.All(policies, p => Assert.Equal(new[] { current }, p.Roles));

        foreach (var table in new[] { "channels", "organization_channels", "agencies" })
        {
            Assert.Equal((true, true), await owner.QuerySingleAsync<(bool, bool)>(
                "SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE oid = to_regclass(@t)", new { t = "public." + table }));
        }

        // The mapping keeps the one rule every tenant table has.
        Assert.Equal(1, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_policies WHERE tablename = 'organization_channels' AND policyname = 'tenant_isolation'"));
    }

    [Fact]
    public async Task ARestart_KeepsEverythingTheSiteAdminDid()
    {
        var admin = await SiteAdminAsync();
        var org = await NewOrganizationAsync("company");
        var telegram = await CreateChannelAsync(admin, "Telegram", "Fast chat", null, SmallPng, "image/png");
        await SetChannelAsync(admin, org.AgencyId, telegram, true);
        await SetActiveAsync(admin, org.AgencyId, false);

        await Db.MigrateAsync();
        await Db.MigrateAsync();

        Assert.False(await IsActiveAsync(org.AgencyId));
        Assert.Equal(("Telegram", "Fast chat", true, 1L), ((await ChannelsAsync(admin)).Last() is var c ? (c.Name, c.Description, c.HasImage, c.OrganizationCount) : default));
        Assert.Equal(SeededChannels.Length + 1, (await ChannelsAsync(admin)).Count);
    }
}
