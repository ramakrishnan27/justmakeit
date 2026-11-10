using Dapper;
using Npgsql;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>
/// <c>008_team_admin.sql</c>: deactivating and activating a member, activating an invited person by hand with a default
/// password, and the name on an invitation. A second part of <see cref="TeamMigrationTestsBase"/>, so every case runs
/// both on a database owned by a superuser and on one owned by an ordinary role that row-level security binds.
/// </summary>
public abstract partial class TeamMigrationTestsBase
{
    // --- Arranging ---------------------------------------------------------------------------

    private async Task<bool> IsActiveAsync(Guid agencyId, Guid userId)
    {
        await using var owner = await Db.OpenOwnerAsync();
        return await owner.ExecuteScalarAsync<bool>(
            "SELECT is_active FROM agency_members WHERE agency_id = @agencyId AND user_id = @userId", new { agencyId, userId });
    }

    /// <summary>An invitation with the name the admin typed (or none), written as the admin's own connection does. Returns the hash.</summary>
    private async Task<string> InviteNamedAsync(Org org, string email, string role, string? fullName)
    {
        var hash = "hash-" + Guid.NewGuid().ToString("N");
        await using var app = await Db.OpenAppAsync(org.AgencyId, org.AdminId);
        await app.ExecuteAsync(
            """
            INSERT INTO member_invitations (agency_id, business_model, full_name, email, role, token_hash, invited_by, expires_at)
            SELECT a.id, a.business_model, @fullName, lower(@email), @role, @hash, @invitedBy, now() + interval '7 days'
            FROM agencies a WHERE a.id = @agencyId
            """,
            new { agencyId = org.AgencyId, fullName, email, role, hash, invitedBy = org.AdminId });
        return hash;
    }

    private async Task<Guid> InvitationIdAsync(string hash)
    {
        await using var owner = await Db.OpenOwnerAsync();
        return await owner.ExecuteScalarAsync<Guid>("SELECT id FROM member_invitations WHERE token_hash = @hash", new { hash });
    }

    private static Task ActivateAsync(NpgsqlConnection connection, Guid invitationId, Guid newUserId, string? passwordHash = "pbkdf2$default") =>
        connection.ExecuteScalarAsync<object>(
            "SELECT team_activate_invitation(@invitationId, @newUserId, @passwordHash)", new { invitationId, newUserId, passwordHash });

    /// <summary>
    /// Runs the action while the Site admin lets a marketing manager of a company manage the team, so the caller can be
    /// someone other than an organization admin. The database is shared by the tests of the class: the grant is taken back.
    /// </summary>
    private async Task WithMarketingManagersManagingAsync(Func<Task> action)
    {
        await using var owner = await Db.OpenOwnerAsync();
        await owner.ExecuteAsync(
            "UPDATE role_permissions SET is_granted = true WHERE business_model = 'company' AND role = 'marketingmanager' AND permission_key = 'members.manage'");
        try
        {
            await action();
        }
        finally
        {
            await owner.ExecuteAsync(
                "UPDATE role_permissions SET is_granted = false WHERE business_model = 'company' AND role = 'marketingmanager' AND permission_key = 'members.manage'");
        }
    }

    // --- The columns -------------------------------------------------------------------------

    [Fact]
    public async Task EveryMember_IsActiveByDefault_AndTheColumnCannotBeNull()
    {
        var org = await NewOrganizationAsync("company");
        await AddMemberAsync(org, "contentwriter");

        await using var admin = await AsAsync(org.AgencyId, org.AdminId);
        var team = (await admin.QueryAsync<(Guid UserId, bool IsActive)>("SELECT user_id, is_active FROM team_members_v2()")).ToList();
        Assert.Equal(2, team.Count);
        Assert.All(team, member => Assert.True(member.IsActive));

        await using var owner = await Db.OpenOwnerAsync();
        var (nullable, defaultValue) = await owner.QuerySingleAsync<(string, string)>(
            "SELECT is_nullable, column_default FROM information_schema.columns WHERE table_name = 'agency_members' AND column_name = 'is_active'");
        Assert.Equal("NO", nullable);
        Assert.Equal("true", defaultValue);
    }

    [Fact]
    public async Task AnInvitation_KeepsTheNameTheAdminTyped_AFindReturnsIt_AndABlankOrOverlongNameIsRefused()
    {
        var org = await NewOrganizationAsync("company");
        var named = await InviteNamedAsync(org, NewEmail(), "contentwriter", "Dev Kumar");
        var unnamed = await InviteNamedAsync(org, NewEmail(), "contentwriter", null); // an invitation from before names were asked for

        await using var nobody = await Db.OpenAppAsync();
        Assert.Equal("Dev Kumar", await nobody.ExecuteScalarAsync<string?>("SELECT full_name FROM invitation_find_v3(@hash)", new { hash = named }));
        Assert.Null(await nobody.ExecuteScalarAsync<string?>("SELECT full_name FROM invitation_find_v3(@hash)", new { hash = unnamed }));

        await AssertRefusedAsync(
            () => InviteNamedAsync(org, NewEmail(), "contentwriter", "   "), PostgresErrorCodes.CheckViolation, "ck_member_invitations_name");
        await AssertRefusedAsync(
            () => InviteNamedAsync(org, NewEmail(), "contentwriter", new string('a', 201)), PostgresErrorCodes.CheckViolation, "ck_member_invitations_name");
        await InviteNamedAsync(org, NewEmail(), "contentwriter", new string('a', 200));
    }

    [Fact]
    public async Task TheInvitationPageFind_StillShowsOnlyAWaitingUnexpiredInvitation()
    {
        var org = await NewOrganizationAsync("company");
        var used = await InviteNamedAsync(org, NewEmail(), "contentwriter", "Used");
        var revoked = await InviteNamedAsync(org, NewEmail(), "contentwriter", "Revoked");
        var expired = await InviteNamedAsync(org, NewEmail(), "contentwriter", "Expired");
        await AcceptAsync(used);
        await using (var owner = await Db.OpenOwnerAsync())
        {
            await owner.ExecuteAsync("UPDATE member_invitations SET revoked_at = now() WHERE token_hash = @revoked", new { revoked });
            await owner.ExecuteAsync(
                "UPDATE member_invitations SET created_at = now() - interval '10 days', expires_at = now() - interval '1 day' WHERE token_hash = @expired",
                new { expired });
        }

        await using var nobody = await Db.OpenAppAsync();
        foreach (var hash in new[] { used, revoked, expired, "not-a-hash" })
        {
            Assert.Empty(await nobody.QueryAsync("SELECT * FROM invitation_find_v3(@hash)", new { hash }));
        }
    }

    // --- Who may call what -------------------------------------------------------------------

    [Fact]
    public async Task EveryNewFunction_RefusesAMemberWithoutThePermission_ANonMember_AndARequestWithNoUser()
    {
        var a = await NewOrganizationAsync("company");
        var b = await NewOrganizationAsync("company");
        var writer = await AddMemberAsync(a, "contentwriter");
        var target = await AddMemberAsync(a, "marketingmanager");
        var invitation = await InvitationIdAsync(await InviteNamedAsync(a, NewEmail(), "contentwriter", "Waiting Person"));

        string[] calls =
        [
            "SELECT * FROM team_members_v2()",
            "SELECT team_set_member_active(@target, false)",
            "SELECT team_activate_invitation(@invitation, gen_random_uuid(), 'pbkdf2$default')"
        ];

        foreach (var call in calls)
        {
            var arguments = new { target, invitation };

            await using (var asWriter = await AsAsync(a.AgencyId, writer))
            {
                await AssertRefusedAsync(() => asWriter.ExecuteScalarAsync<object>(call, arguments), PostgresErrorCodes.InsufficientPrivilege);
            }

            await using (var asOutsider = await AsAsync(a.AgencyId, b.AdminId))
            {
                await AssertRefusedAsync(() => asOutsider.ExecuteScalarAsync<object>(call, arguments), PostgresErrorCodes.InsufficientPrivilege);
            }

            await using (var asNobody = await Db.OpenAppAsync())
            {
                await AssertRefusedAsync(() => asNobody.ExecuteScalarAsync<object>(call, arguments), PostgresErrorCodes.InsufficientPrivilege);
            }

            await using (var noOrganization = await Db.OpenAppAsync(null, a.AdminId))
            {
                await AssertRefusedAsync(() => noOrganization.ExecuteScalarAsync<object>(call, arguments), PostgresErrorCodes.InsufficientPrivilege);
            }
        }

        Assert.True(await IsActiveAsync(a.AgencyId, target));
        Assert.Null(await RevokedAfterAsync(a.AgencyId, target));
        await using var admin = await AsAsync(a.AgencyId, a.AdminId);
        Assert.Equal(1, await admin.ExecuteScalarAsync<int>("SELECT count(*) FROM member_invitations WHERE accepted_at IS NULL AND revoked_at IS NULL"));
    }

    [Fact]
    public async Task ADeactivatedAdmin_IsRefusedByEveryTeamFunction_EvenThoughTheirRoleHoldsThePermission()
    {
        var org = await NewOrganizationAsync("company");
        var second = await AddMemberAsync(org, "contentwriter");
        await using (var founder = await AsAsync(org.AgencyId, org.AdminId))
        {
            await founder.ExecuteScalarAsync<object>("SELECT team_change_role_v2(@second, 'organizationadmin')", new { second });
            await founder.ExecuteScalarAsync<object>("SELECT team_set_member_active(@second, false)", new { second });
        }

        var someone = await AddMemberAsync(org, "contentwriter");
        await using var deactivated = await AsAsync(org.AgencyId, second);
        foreach (var call in new[]
                 {
                     "SELECT * FROM team_members_v2()",
                     "SELECT team_set_member_active(@someone, false)",
                     "SELECT team_change_role_v2(@someone, 'marketingmanager')",
                     "SELECT team_remove_member_v2(@someone)",
                     "SELECT team_activate_invitation(gen_random_uuid(), gen_random_uuid(), 'pbkdf2$default')"
                 })
        {
            await AssertRefusedAsync(() => deactivated.ExecuteScalarAsync<object>(call, new { someone }), PostgresErrorCodes.InsufficientPrivilege);
        }

        Assert.Equal("contentwriter", await RoleOfAsync(org.AgencyId, someone));
    }

    // --- Deactivating and activating ---------------------------------------------------------

    [Fact]
    public async Task Deactivating_KeepsTheMember_EndsTheirSessions_AndActivatingBringsThemBackWithoutAnotherEnd()
    {
        var org = await NewOrganizationAsync("agency");
        var member = await AddMemberAsync(org, "marketingmanager");
        var bystander = await AddMemberAsync(org, "contentwriter");
        var before = DateTime.UtcNow.AddSeconds(-1);

        await using var admin = await AsAsync(org.AgencyId, org.AdminId);
        await admin.ExecuteScalarAsync<object>("SELECT team_set_member_active(@member, false)", new { member });

        Assert.False(await IsActiveAsync(org.AgencyId, member));
        Assert.Equal("marketingmanager", await RoleOfAsync(org.AgencyId, member));
        var revoked = await RevokedAfterAsync(org.AgencyId, member);
        Assert.NotNull(revoked);
        Assert.True(revoked >= before, "The sessions must end at the moment of the change.");
        Assert.Null(await RevokedAfterAsync(org.AgencyId, bystander));
        Assert.True(await IsActiveAsync(org.AgencyId, bystander));

        // Asking for the state they already have changes nothing, including the moment their sessions ended.
        await admin.ExecuteScalarAsync<object>("SELECT team_set_member_active(@member, false)", new { member });
        Assert.Equal(revoked, await RevokedAfterAsync(org.AgencyId, member));

        // Activating lets them sign in again (with the password they have); the earlier end stays, so no old token returns.
        await admin.ExecuteScalarAsync<object>("SELECT team_set_member_active(@member, true)", new { member });
        Assert.True(await IsActiveAsync(org.AgencyId, member));
        Assert.Equal(revoked, await RevokedAfterAsync(org.AgencyId, member));

        var listed = await admin.QuerySingleAsync<bool>("SELECT is_active FROM team_members_v2() WHERE user_id = @member", new { member });
        Assert.True(listed);
    }

    [Fact]
    public async Task SettingTheState_OfYourself_AnUnknownPerson_ANobodyElsesMember_OrNothing_IsRefusedWithARule()
    {
        var a = await NewOrganizationAsync("company");
        var b = await NewOrganizationAsync("company");
        var foreign = await AddMemberAsync(b, "contentwriter");

        await using var admin = await AsAsync(a.AgencyId, a.AdminId);
        await AssertRuleAsync(() => admin.ExecuteScalarAsync<object>("SELECT team_set_member_active(@id, false)", new { id = a.AdminId }), "cannot_change_self");
        await AssertRuleAsync(() => admin.ExecuteScalarAsync<object>("SELECT team_set_member_active(gen_random_uuid(), false)"), "member_not_found");
        await AssertRuleAsync(() => admin.ExecuteScalarAsync<object>("SELECT team_set_member_active(@foreign, false)", new { foreign }), "member_not_found");
        await AssertRefusedAsync(
            () => admin.ExecuteScalarAsync<object>("SELECT team_set_member_active(@id, NULL)", new { id = foreign }), PostgresErrorCodes.InvalidParameterValue);

        Assert.True(await IsActiveAsync(b.AgencyId, foreign));
        Assert.Null(await RevokedAfterAsync(b.AgencyId, foreign));
    }

    [Fact]
    public async Task TheLastActiveOrganizationAdmin_CannotBeDeactivatedDemotedOrRemoved_EvenWhenAnInactiveAdminExists()
    {
        var org = await NewOrganizationAsync("company");
        var manager = await AddMemberAsync(org, "marketingmanager");
        var second = await AddMemberAsync(org, "contentwriter");

        await WithMarketingManagersManagingAsync(async () =>
        {
            await using var asManager = await AsAsync(org.AgencyId, manager);

            // The founder is the only admin: nothing may take them out of service.
            await AssertRuleAsync(() => asManager.ExecuteScalarAsync<object>("SELECT team_set_member_active(@id, false)", new { id = org.AdminId }), "last_organization_admin");
            await AssertRuleAsync(() => asManager.ExecuteScalarAsync<object>("SELECT team_change_role_v2(@id, 'contentwriter')", new { id = org.AdminId }), "last_organization_admin");
            await AssertRuleAsync(() => asManager.ExecuteScalarAsync<object>("SELECT team_remove_member_v2(@id)", new { id = org.AdminId }), "last_organization_admin");
            Assert.True(await IsActiveAsync(org.AgencyId, org.AdminId));

            // A second admin, deactivated: the founder is still the only ACTIVE one, so the same three are still refused.
            await asManager.ExecuteScalarAsync<object>("SELECT team_change_role_v2(@second, 'organizationadmin')", new { second });
            await asManager.ExecuteScalarAsync<object>("SELECT team_set_member_active(@second, false)", new { second });
            await AssertRuleAsync(() => asManager.ExecuteScalarAsync<object>("SELECT team_set_member_active(@id, false)", new { id = org.AdminId }), "last_organization_admin");
            await AssertRuleAsync(() => asManager.ExecuteScalarAsync<object>("SELECT team_change_role_v2(@id, 'contentwriter')", new { id = org.AdminId }), "last_organization_admin");
            await AssertRuleAsync(() => asManager.ExecuteScalarAsync<object>("SELECT team_remove_member_v2(@id)", new { id = org.AdminId }), "last_organization_admin");

            // Once the second is active again the founder may be deactivated; and then the second is the only active one.
            await asManager.ExecuteScalarAsync<object>("SELECT team_set_member_active(@second, true)", new { second });
            await asManager.ExecuteScalarAsync<object>("SELECT team_set_member_active(@id, false)", new { id = org.AdminId });
            await AssertRuleAsync(() => asManager.ExecuteScalarAsync<object>("SELECT team_set_member_active(@second, false)", new { second }), "last_organization_admin");
            await AssertRuleAsync(() => asManager.ExecuteScalarAsync<object>("SELECT team_remove_member_v2(@second)", new { second }), "last_organization_admin");

            // An INACTIVE admin is not counted, so demoting or removing the deactivated founder is allowed.
            await asManager.ExecuteScalarAsync<object>("SELECT team_change_role_v2(@id, 'contentwriter')", new { id = org.AdminId });
            Assert.Equal("contentwriter", await RoleOfAsync(org.AgencyId, org.AdminId));
        });
    }

    [Fact]
    public async Task TheOldTeamFunctions_AreNoLongerCallableByTheApplicationRole_ButTheNewOnesStillEnforceTheirRules()
    {
        var org = await NewOrganizationAsync("company");
        var member = await AddMemberAsync(org, "contentwriter");

        await using var admin = await AsAsync(org.AgencyId, org.AdminId);
        foreach (var call in new[]
                 {
                     "SELECT * FROM team_list_members()",
                     "SELECT team_change_role(@member, 'marketingmanager')",
                     "SELECT team_remove_member(@member)"
                 })
        {
            await AssertRefusedAsync(() => admin.ExecuteScalarAsync<object>(call, new { member }), PostgresErrorCodes.InsufficientPrivilege);
        }

        Assert.Equal("contentwriter", await RoleOfAsync(org.AgencyId, member));
        await admin.ExecuteScalarAsync<object>("SELECT team_change_role_v2(@member, 'marketingmanager')", new { member });
        Assert.Equal("marketingmanager", await RoleOfAsync(org.AgencyId, member));
    }

    // --- Activating an invited person by hand ------------------------------------------------

    [Fact]
    public async Task ActivatingAnInvitation_MakesTheNamedPersonAnActiveMember_UsesTheStoredHash_AndRetiresTheLink()
    {
        var org = await NewOrganizationAsync("agency");
        var email = $"dev.{Guid.NewGuid():N}@example.com";
        var hash = await InviteNamedAsync(org, email.ToUpperInvariant(), "clientmanager", "Dev Kumar");
        var invitation = await InvitationIdAsync(hash);
        var newUser = Guid.NewGuid();

        await using (var admin = await AsAsync(org.AgencyId, org.AdminId))
        {
            await ActivateAsync(admin, invitation, newUser, "pbkdf2$the-default-password-hash");
        }

        await using var owner = await Db.OpenOwnerAsync();
        var user = await owner.QuerySingleAsync<(string Email, string FullName, string PasswordHash)>(
            "SELECT email, full_name, password_hash FROM users WHERE id = @newUser", new { newUser });
        Assert.Equal(email, user.Email); // lower-cased, as the invitation keeps it
        Assert.Equal("Dev Kumar", user.FullName);
        Assert.Equal("pbkdf2$the-default-password-hash", user.PasswordHash);

        var member = await owner.QuerySingleAsync<(string Role, string BusinessModel, bool IsActive)>(
            "SELECT role, business_model, is_active FROM agency_members WHERE agency_id = @agencyId AND user_id = @newUser",
            new { agencyId = org.AgencyId, newUser });
        Assert.Equal(("clientmanager", "agency", true), member);

        Assert.NotNull(await owner.ExecuteScalarAsync<DateTime?>("SELECT accepted_at FROM member_invitations WHERE id = @invitation", new { invitation }));

        // The link no longer works, and the person no longer waits in the list of invitations.
        await using var nobody = await Db.OpenAppAsync();
        Assert.Empty(await nobody.QueryAsync("SELECT * FROM invitation_find_v3(@hash)", new { hash }));
        await AssertRuleAsync(() => AcceptAsync(hash), "invitation_invalid");
        await using var admin2 = await AsAsync(org.AgencyId, org.AdminId);
        Assert.Equal(0, await admin2.ExecuteScalarAsync<int>("SELECT count(*) FROM member_invitations WHERE accepted_at IS NULL AND revoked_at IS NULL"));
        Assert.Contains(
            await admin2.QueryAsync<Guid>("SELECT user_id FROM team_members_v2()"),
            id => id == newUser);
    }

    [Fact]
    public async Task ActivatingAnInvitationWithNoName_FallsBackToTheStartOfTheAddress_AndAnExpiredInvitationCanStillBeActivated()
    {
        var org = await NewOrganizationAsync("company");
        var local = $"old.{Guid.NewGuid():N}";
        var hash = await InviteNamedAsync(org, $"{local}@example.com", "contentwriter", null);
        await using (var owner = await Db.OpenOwnerAsync())
        {
            await owner.ExecuteAsync(
                "UPDATE member_invitations SET created_at = now() - interval '10 days', expires_at = now() - interval '1 day' WHERE token_hash = @hash", new { hash });
        }

        var newUser = Guid.NewGuid();
        await using (var admin = await AsAsync(org.AgencyId, org.AdminId))
        {
            await ActivateAsync(admin, await InvitationIdAsync(hash), newUser);
        }

        await using var check = await Db.OpenOwnerAsync();
        Assert.Equal(local, await check.ExecuteScalarAsync<string>("SELECT full_name FROM users WHERE id = @newUser", new { newUser }));
    }

    [Fact]
    public async Task ActivatingAnInvitation_ThatIsNotWaitingHere_IsInvitationNotFound_AndAnEmptyPasswordHashIsRefused()
    {
        var a = await NewOrganizationAsync("company");
        var b = await NewOrganizationAsync("company");
        var foreign = await InvitationIdAsync(await InviteNamedAsync(b, NewEmail(), "contentwriter", "Elsewhere"));
        var usedHash = await InviteNamedAsync(a, NewEmail(), "contentwriter", "Used");
        await AcceptAsync(usedHash);
        var revokedHash = await InviteNamedAsync(a, NewEmail(), "contentwriter", "Revoked");
        await using (var owner = await Db.OpenOwnerAsync())
        {
            await owner.ExecuteAsync("UPDATE member_invitations SET revoked_at = now() WHERE token_hash = @revokedHash", new { revokedHash });
        }

        var waiting = await InvitationIdAsync(await InviteNamedAsync(a, NewEmail(), "contentwriter", "Waiting"));

        await using var admin = await AsAsync(a.AgencyId, a.AdminId);
        foreach (var id in new[] { foreign, await InvitationIdAsync(usedHash), await InvitationIdAsync(revokedHash), Guid.NewGuid() })
        {
            await AssertRuleAsync(() => ActivateAsync(admin, id, Guid.NewGuid()), "invitation_not_found");
        }

        foreach (var empty in new[] { null, string.Empty })
        {
            await AssertRefusedAsync(() => ActivateAsync(admin, waiting, Guid.NewGuid(), empty), PostgresErrorCodes.InvalidParameterValue);
        }

        // The other organization's invitation is untouched and still waiting.
        await using var owner2 = await Db.OpenOwnerAsync();
        Assert.Null(await owner2.ExecuteScalarAsync<DateTime?>("SELECT accepted_at FROM member_invitations WHERE id = @foreign", new { foreign }));
    }

    [Fact]
    public async Task ActivatingAnInvitation_ForAnAddressThatHasAnAccountMeanwhile_IsEmailTaken_AndLeavesEverythingAsItWas()
    {
        var a = await NewOrganizationAsync("company");
        var b = await NewOrganizationAsync("company");
        var invitation = await InvitationIdAsync(await InviteNamedAsync(a, b.AdminEmail, "contentwriter", "Taken"));
        var newUser = Guid.NewGuid();

        await using var admin = await AsAsync(a.AgencyId, a.AdminId);
        await AssertRuleAsync(() => ActivateAsync(admin, invitation, newUser), "email_taken");

        await using var owner = await Db.OpenOwnerAsync();
        Assert.Equal(0, await owner.ExecuteScalarAsync<int>("SELECT count(*) FROM users WHERE id = @newUser", new { newUser }));
        Assert.Null(await owner.ExecuteScalarAsync<DateTime?>("SELECT accepted_at FROM member_invitations WHERE id = @invitation", new { invitation }));
    }

    [Fact]
    public async Task TwoActivationsOfTheSameInvitationAtOnce_MakeOnePerson_AndTheOtherIsInvitationNotFound()
    {
        var org = await NewOrganizationAsync("company");
        var invitation = await InvitationIdAsync(await InviteNamedAsync(org, NewEmail(), "contentwriter", "Twice"));

        async Task<string> TryAsync()
        {
            await using var admin = await AsAsync(org.AgencyId, org.AdminId);
            try
            {
                await ActivateAsync(admin, invitation, Guid.NewGuid());
                return "activated";
            }
            catch (PostgresException exception)
            {
                return exception.MessageText;
            }
        }

        var results = await Task.WhenAll(TryAsync(), TryAsync());

        Assert.Equal(["activated", "invitation_not_found"], results.Order());
        await using var owner = await Db.OpenOwnerAsync();
        Assert.Equal(2, await owner.ExecuteScalarAsync<int>("SELECT count(*) FROM agency_members WHERE agency_id = @agencyId", new { agencyId = org.AgencyId }));
    }

    [Fact]
    public async Task TheActivatedPerson_IsAnOrdinaryMember_WhoCanBeDeactivatedChangedAndRemovedLikeAnyOther()
    {
        var org = await NewOrganizationAsync("agency");
        var newUser = Guid.NewGuid();
        await using var admin = await AsAsync(org.AgencyId, org.AdminId);
        await ActivateAsync(admin, await InvitationIdAsync(await InviteNamedAsync(org, NewEmail(), "contentwriter", "Ordinary")), newUser);

        await admin.ExecuteScalarAsync<object>("SELECT team_set_member_active(@newUser, false)", new { newUser });
        Assert.False(await IsActiveAsync(org.AgencyId, newUser));
        await admin.ExecuteScalarAsync<object>("SELECT team_set_member_active(@newUser, true)", new { newUser });
        await admin.ExecuteScalarAsync<object>("SELECT team_change_role_v2(@newUser, 'clientmanager')", new { newUser });
        await admin.ExecuteScalarAsync<object>("SELECT team_remove_member_v2(@newUser)", new { newUser });

        await using var owner = await Db.OpenOwnerAsync();
        Assert.Equal(0, await owner.ExecuteScalarAsync<int>("SELECT count(*) FROM users WHERE id = @newUser", new { newUser }));
    }
}
