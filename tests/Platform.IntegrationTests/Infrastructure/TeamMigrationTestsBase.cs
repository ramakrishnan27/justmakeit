using Dapper;
using Npgsql;
using Platform.Auth.Services;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>
/// <c>007_team_members.sql</c>: invitations, the team functions and the session revocations. Everything is arranged
/// through <c>app_user</c> connections, the way the API does it (a new organization, its founder, an invitation, an
/// acceptance), so the same tests run on a database owned by a superuser and on one owned by an ordinary role that
/// row-level security binds (the two subclasses). Nothing is inserted behind the policies except where a test says so.
/// </summary>
public abstract class TeamMigrationTestsBase(PostgresFixture postgres) : IAsyncLifetime
{
    protected TestDatabase Db { get; private set; } = null!;

    protected abstract Task<TestDatabase> CreateDatabaseAsync(PostgresFixture fixture);

    public async Task InitializeAsync()
    {
        DapperConfiguration.Configure();
        Db = await CreateDatabaseAsync(postgres);
    }

    public async Task DisposeAsync() => await Db.DisposeAsync();

    // --- Arranging ---------------------------------------------------------------------------

    protected sealed record Org(Guid AgencyId, Guid AdminId, string AdminEmail, string Model);

    private static string NewEmail() => $"{Guid.NewGuid():N}@example.com";

    /// <summary>Registration as the API does it: the connection acts for the new ids, so every policy passes.</summary>
    protected async Task<Org> NewOrganizationAsync(string model = "company")
    {
        var agencyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var email = NewEmail();

        await using var app = await Db.OpenAppAsync(agencyId, userId);
        await using var transaction = await app.BeginTransactionAsync();
        await app.ExecuteAsync(
            "INSERT INTO agencies (id, name, business_model) VALUES (@agencyId, 'Org', @model)", new { agencyId, model }, transaction);
        await app.ExecuteAsync(
            "INSERT INTO users (id, email, password_hash, full_name) VALUES (@userId, @email, 'pbkdf2$x', 'Founder')", new { userId, email }, transaction);
        await app.ExecuteAsync(
            "INSERT INTO agency_members (agency_id, user_id, business_model, role) VALUES (@agencyId, @userId, @model, @role)",
            new { agencyId, userId, model, role = model == "creator" ? "contentwriter" : "organizationadmin" }, transaction);
        await transaction.CommitAsync();

        return new Org(agencyId, userId, email, model);
    }

    /// <summary>An invitation as the admin's own connection writes it (plain SQL under row-level security). Returns the hash.</summary>
    protected async Task<string> InviteAsync(Org org, string email, string role)
    {
        var hash = "hash-" + Guid.NewGuid().ToString("N");
        await using var app = await Db.OpenAppAsync(org.AgencyId, org.AdminId);
        await app.ExecuteAsync(
            """
            INSERT INTO member_invitations (agency_id, business_model, email, role, token_hash, invited_by, expires_at)
            SELECT a.id, a.business_model, lower(@email), @role, @hash, @invitedBy, now() + interval '7 days'
            FROM agencies a WHERE a.id = @agencyId
            """,
            new { agencyId = org.AgencyId, email, role, hash, invitedBy = org.AdminId });
        return hash;
    }

    /// <summary>
    /// Accepts as the API will: a connection acting for the NEW user (no organization yet), because the check that a user has a
    /// password or a linked sign-in runs at commit, as the connection's user, and only sees the user's own identity.
    /// </summary>
    protected async Task<(Guid AgencyId, string Role)> AcceptAsync(
        string hash, Guid? userId = null, string? passwordHash = "pbkdf2$new", string? provider = null, string? subject = null, string? providerEmail = null)
    {
        var id = userId ?? Guid.NewGuid();
        await using var app = await Db.OpenAppAsync(null, id);
        return await app.QuerySingleAsync<(Guid AgencyId, string Role)>(
            "SELECT agency_id, role FROM invitation_accept(@hash, @id, 'New Person', @passwordHash, @provider, @subject, @providerEmail)",
            new { hash, id, passwordHash, provider, subject, providerEmail });
    }

    /// <summary>Invites and accepts: a member with the role, signed up through the real flow. Returns their id.</summary>
    protected async Task<Guid> AddMemberAsync(Org org, string role, string? email = null)
    {
        var id = Guid.NewGuid();
        var hash = await InviteAsync(org, email ?? NewEmail(), role);
        await AcceptAsync(hash, id);
        return id;
    }

    protected async Task<NpgsqlConnection> AsAsync(Guid agencyId, Guid userId) => await Db.OpenAppAsync(agencyId, userId);

    protected static async Task<PostgresException> AssertRefusedAsync(Func<Task> action, string sqlState, string? constraintOrMessage = null)
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

    protected static Task<PostgresException> AssertRuleAsync(Func<Task> action, string code) =>
        AssertRefusedAsync(action, PostgresErrorCodes.RaiseException, code);

    protected async Task<string?> RoleOfAsync(Guid agencyId, Guid userId)
    {
        await using var owner = await Db.OpenOwnerAsync();
        return await owner.ExecuteScalarAsync<string?>(
            "SELECT role FROM agency_members WHERE agency_id = @agencyId AND user_id = @userId", new { agencyId, userId });
    }

    // Npgsql hands a timestamptz back as a UTC DateTime.
    protected async Task<DateTime?> RevokedAfterAsync(Guid agencyId, Guid userId)
    {
        await using var owner = await Db.OpenOwnerAsync();
        return await owner.ExecuteScalarAsync<DateTime?>(
            "SELECT valid_after FROM session_revocations WHERE agency_id = @agencyId AND user_id = @userId", new { agencyId, userId });
    }

    // --- The permission and the page ---------------------------------------------------------

    [Fact]
    public async Task OnlyTheOrganizationAdminsOfACompanyAndAnAgency_HoldMembersManageByDefault()
    {
        await using var owner = await Db.OpenOwnerAsync();
        var granted = (await owner.QueryAsync<(string Model, string Role)>(
            "SELECT business_model, role FROM role_permissions WHERE permission_key = 'members.manage' AND is_granted ORDER BY 1, 2")).ToList();

        Assert.Equal([("agency", "organizationadmin"), ("company", "organizationadmin")], granted);
        Assert.Equal(8, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM role_permissions WHERE permission_key = 'members.manage'"));
        Assert.Equal(("Team", "users", "/team"), await owner.QuerySingleAsync<(string, string, string)>(
            "SELECT i.label, i.icon, p.path FROM menu_items i JOIN menu_pages p ON p.app = i.menu_key AND p.key = i.page_key WHERE p.key = 'team' AND p.permission_key = 'members.manage'"));
    }

    [Fact]
    public async Task ARestart_ChangesNoGrant_AndDoesNotMoveThePermissionVersion()
    {
        await using var owner = await Db.OpenOwnerAsync();
        var version = await owner.ExecuteScalarAsync<long>("SELECT version FROM permission_state");

        await Db.MigrateAsync();
        await Db.MigrateAsync();

        Assert.Equal(version, await owner.ExecuteScalarAsync<long>("SELECT version FROM permission_state"));
        Assert.Equal(8, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM role_permissions WHERE permission_key = 'members.manage'"));
        Assert.Equal(1, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM menu_items WHERE page_key = 'team'"));
    }

    // --- Invitations: the table --------------------------------------------------------------

    [Fact]
    public async Task AnAdmin_SeesAndWritesOnlyTheirOwnOrganizationsInvitations()
    {
        var a = await NewOrganizationAsync("company");
        var b = await NewOrganizationAsync("agency");
        await InviteAsync(a, "one@example.com", "contentwriter");
        await InviteAsync(b, "two@example.com", "clientmanager");

        await using var asA = await AsAsync(a.AgencyId, a.AdminId);
        Assert.Equal(["one@example.com"], await asA.QueryAsync<string>("SELECT email FROM member_invitations"));

        // Writing for another organization is refused by row-level security, whatever the row says.
        await AssertRefusedAsync(
            () => asA.ExecuteAsync(
                """
                INSERT INTO member_invitations (agency_id, business_model, email, role, token_hash, expires_at)
                VALUES (@agencyId, 'agency', 'x@example.com', 'contentwriter', 'h-x', now() + interval '1 day')
                """,
                new { agencyId = b.AgencyId }),
            PostgresErrorCodes.InsufficientPrivilege);

        // Nor can it be reached by updating one's own row onto another organization.
        await AssertRefusedAsync(
            () => asA.ExecuteAsync("UPDATE member_invitations SET agency_id = @other", new { other = b.AgencyId }),
            PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task NoOrganization_SeesNoInvitation_AndAppUserCannotDeleteOne()
    {
        var org = await NewOrganizationAsync("company");
        await InviteAsync(org, "one@example.com", "contentwriter");

        await using (var nobody = await Db.OpenAppAsync())
        {
            Assert.Equal(0, await DbTestSupport.CountAsync(nobody, "member_invitations"));
        }

        await using var admin = await AsAsync(org.AgencyId, org.AdminId);
        await AssertRefusedAsync(
            () => admin.ExecuteAsync("DELETE FROM member_invitations"), PostgresErrorCodes.InsufficientPrivilege);
    }

    [Theory]
    [InlineData("creator", "contentwriter")]
    [InlineData("company", "clientmanager")]
    [InlineData("company", "nonsense")]
    public async Task AnInvitation_NeedsAModelAndRoleThatMakeSense(string model, string role)
    {
        var org = await NewOrganizationAsync(model);

        await AssertRefusedAsync(
            () => InviteAsync(org, "one@example.com", role), PostgresErrorCodes.CheckViolation, "ck_member_invitations_role_model");
    }

    [Fact]
    public async Task AnInvitation_MustRepeatTheModelOfItsOrganization()
    {
        var org = await NewOrganizationAsync("company");
        await using var admin = await AsAsync(org.AgencyId, org.AdminId);

        await AssertRefusedAsync(
            () => admin.ExecuteAsync(
                """
                INSERT INTO member_invitations (agency_id, business_model, email, role, token_hash, expires_at)
                VALUES (@agencyId, 'agency', 'x@example.com', 'clientmanager', 'h-m', now() + interval '1 day')
                """,
                new { agencyId = org.AgencyId }),
            PostgresErrorCodes.ForeignKeyViolation, "fk_member_invitations_business_model");
    }

    [Fact]
    public async Task OnlyOneInvitationPerOrganizationAndEmailIsPending_UntilItIsRevoked()
    {
        var org = await NewOrganizationAsync("company");
        await InviteAsync(org, "One@Example.com", "contentwriter");

        await AssertRefusedAsync(
            () => InviteAsync(org, "one@example.com", "marketingmanager"), PostgresErrorCodes.UniqueViolation, "ux_member_invitations_pending");

        await using var admin = await AsAsync(org.AgencyId, org.AdminId);
        await admin.ExecuteAsync("UPDATE member_invitations SET revoked_at = now()");
        await InviteAsync(org, "one@example.com", "marketingmanager");

        // Another organization may invite the same address.
        var other = await NewOrganizationAsync("company");
        await InviteAsync(other, "one@example.com", "contentwriter");
    }

    // --- Invitations: finding and accepting --------------------------------------------------

    [Fact]
    public async Task FindingAnInvitation_NeedsNoUser_AndShowsOnlyWhatThePageNeeds()
    {
        var org = await NewOrganizationAsync("agency");
        var hash = await InviteAsync(org, "new@example.com", "clientmanager");

        await using var nobody = await Db.OpenAppAsync();
        var found = await nobody.QuerySingleAsync<(string Organization, string Model, string Email, string Role)>(
            "SELECT organization_name, business_model, email, role FROM invitation_find(@hash)", new { hash });

        Assert.Equal(("Org", "agency", "new@example.com", "clientmanager"), found);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("accepted")]
    [InlineData("revoked")]
    [InlineData("expired")]
    public async Task AnInvitationThatIsNotPending_IsNotFound_AndCannotBeAccepted(string state)
    {
        var org = await NewOrganizationAsync("company");
        var hash = await InviteAsync(org, "new@example.com", "contentwriter");

        await using var owner = await Db.OpenOwnerAsync();
        switch (state)
        {
            case "unknown":
                hash = "no-such-hash";
                break;
            case "accepted":
                await AcceptAsync(hash);
                break;
            case "revoked":
                await owner.ExecuteAsync("UPDATE member_invitations SET revoked_at = now() WHERE token_hash = @hash", new { hash });
                break;
            case "expired":
                await owner.ExecuteAsync(
                    "UPDATE member_invitations SET created_at = now() - interval '8 days', expires_at = now() - interval '1 day' WHERE token_hash = @hash", new { hash });
                break;
        }

        await using var nobody = await Db.OpenAppAsync();
        Assert.Empty(await nobody.QueryAsync("SELECT * FROM invitation_find(@hash)", new { hash }));
        await AssertRuleAsync(() => AcceptAsync(hash), "invitation_invalid");
    }

    [Fact]
    public async Task Accepting_CreatesTheUserWithTheInvitedEmailTheMembershipAndMarksItUsed()
    {
        var org = await NewOrganizationAsync("agency");
        var hash = await InviteAsync(org, "New.Person@Example.com", "clientmanager");
        var userId = Guid.NewGuid();

        var result = await AcceptAsync(hash, userId);

        Assert.Equal((org.AgencyId, "clientmanager"), result);
        await using var owner = await Db.OpenOwnerAsync();
        Assert.Equal(("new.person@example.com", "pbkdf2$new", "New Person"), await owner.QuerySingleAsync<(string, string, string)>(
            "SELECT email, password_hash, full_name FROM users WHERE id = @userId", new { userId }));
        Assert.Equal(("agency", "clientmanager"), await owner.QuerySingleAsync<(string, string)>(
            "SELECT business_model, role FROM agency_members WHERE agency_id = @agencyId AND user_id = @userId", new { agencyId = org.AgencyId, userId }));
        Assert.NotNull(await owner.ExecuteScalarAsync<DateTime?>("SELECT accepted_at FROM member_invitations WHERE token_hash = @hash", new { hash }));
    }

    [Fact]
    public async Task AnInvitationWorksOnce_AndTwoAtTheSameTimeLeaveExactlyOneAccount()
    {
        var org = await NewOrganizationAsync("company");
        var hash = await InviteAsync(org, "new@example.com", "contentwriter");

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try
            {
                await AcceptAsync(hash);
                return "accepted";
            }
            catch (PostgresException exception)
            {
                return exception.MessageText;
            }
        }));

        Assert.Equal(1, results.Count(r => r == "accepted"));
        Assert.All(results.Where(r => r != "accepted"), r => Assert.Equal("invitation_invalid", r));
        await using var owner = await Db.OpenOwnerAsync();
        Assert.Equal(2, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM agency_members WHERE agency_id = @agencyId", new { agencyId = org.AgencyId }));
        Assert.Equal(1, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM users WHERE email = 'new@example.com'"));
    }

    [Fact]
    public async Task AnEmailThatBecameAnAccountInTheMeantime_IsRefused_AndTheInvitationStaysPending()
    {
        var org = await NewOrganizationAsync("company");
        var other = await NewOrganizationAsync("company");
        var first = await InviteAsync(org, "same@example.com", "contentwriter");
        var second = await InviteAsync(other, "same@example.com", "marketingmanager");

        await AcceptAsync(first);
        await AssertRuleAsync(() => AcceptAsync(second), "email_taken");

        await using var nobody = await Db.OpenAppAsync();
        Assert.Single(await nobody.QueryAsync("SELECT * FROM invitation_find(@hash)", new { hash = second }));
        await using var owner = await Db.OpenOwnerAsync();
        Assert.Equal(1, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM users WHERE email = 'same@example.com'"));
    }

    [Fact]
    public async Task Accepting_NeedsAPasswordOrALinkedSignIn()
    {
        var org = await NewOrganizationAsync("company");
        var hash = await InviteAsync(org, "new@example.com", "contentwriter");

        await AssertRefusedAsync(() => AcceptAsync(hash, passwordHash: null), PostgresErrorCodes.InvalidParameterValue);
        await AssertRefusedAsync(() => AcceptAsync(hash, passwordHash: ""), PostgresErrorCodes.InvalidParameterValue);

        await using var owner = await Db.OpenOwnerAsync();
        Assert.Equal(0, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM users WHERE email = 'new@example.com'"));
    }

    [Fact]
    public async Task AcceptingWithGoogle_NeedsTheVerifiedEmailOfTheInvitation_AndLeavesNoPassword()
    {
        var org = await NewOrganizationAsync("company");
        var hash = await InviteAsync(org, "new@example.com", "contentwriter");

        await AssertRuleAsync(
            () => AcceptAsync(hash, passwordHash: null, provider: "google", subject: "sub-1", providerEmail: "someone.else@example.com"),
            "invitation_invalid");

        var userId = Guid.NewGuid();
        await AcceptAsync(hash, userId, passwordHash: null, provider: "google", subject: "sub-1", providerEmail: "NEW@example.com");

        await using var owner = await Db.OpenOwnerAsync();
        Assert.Null(await owner.ExecuteScalarAsync<string?>("SELECT password_hash FROM users WHERE id = @userId", new { userId }));
        Assert.Equal(("google", "sub-1", "new@example.com"), await owner.QuerySingleAsync<(string, string, string)>(
            "SELECT provider, subject, email FROM user_identities WHERE user_id = @userId", new { userId }));
    }

    [Fact]
    public async Task AuthEmailRegistered_IgnoresCase_AndAnswersForAnyConnection()
    {
        var org = await NewOrganizationAsync("company");

        await using var nobody = await Db.OpenAppAsync();
        Assert.True(await nobody.ExecuteScalarAsync<bool>("SELECT auth_email_registered(@email)", new { email = org.AdminEmail.ToUpperInvariant() }));
        Assert.False(await nobody.ExecuteScalarAsync<bool>("SELECT auth_email_registered('nobody@example.com')"));
    }

    // --- The team ----------------------------------------------------------------------------

    [Fact]
    public async Task TheList_ShowsTheWholeTeamOfTheOrganization_AndNobodyElse()
    {
        var a = await NewOrganizationAsync("agency");
        var b = await NewOrganizationAsync("company");
        var writer = await AddMemberAsync(a, "contentwriter", "writer@example.com");
        await AddMemberAsync(b, "marketingmanager", "other.org@example.com");

        await using var admin = await AsAsync(a.AgencyId, a.AdminId);
        var team = (await admin.QueryAsync<(Guid UserId, string Email, string FullName, string Role)>(
            "SELECT user_id, email, full_name, role FROM team_list_members()")).ToList();

        Assert.Equal(2, team.Count);
        Assert.Contains(team, m => m.UserId == a.AdminId && m.Email == a.AdminEmail && m.Role == "organizationadmin" && m.FullName == "Founder");
        Assert.Contains(team, m => m.UserId == writer && m.Email == "writer@example.com" && m.Role == "contentwriter");
    }

    [Fact]
    public async Task EveryTeamFunction_RefusesAMemberWithoutThePermission_ANonMember_AndARequestWithNoUser()
    {
        var a = await NewOrganizationAsync("company");
        var b = await NewOrganizationAsync("company");
        var writer = await AddMemberAsync(a, "contentwriter");
        var target = await AddMemberAsync(a, "marketingmanager");

        string[] calls =
        [
            "SELECT * FROM team_list_members()",
            "SELECT team_change_role(@target, 'contentwriter')",
            "SELECT team_remove_member(@target)"
        ];

        foreach (var call in calls)
        {
            // A member whose role does not hold members.manage.
            await using (var asWriter = await AsAsync(a.AgencyId, writer))
            {
                await AssertRefusedAsync(() => asWriter.ExecuteScalarAsync<object>(call, new { target }), PostgresErrorCodes.InsufficientPrivilege);
            }

            // The admin of ANOTHER organization, acting for this one (a forged or stale token).
            await using (var asOutsider = await AsAsync(a.AgencyId, b.AdminId))
            {
                await AssertRefusedAsync(() => asOutsider.ExecuteScalarAsync<object>(call, new { target }), PostgresErrorCodes.InsufficientPrivilege);
            }

            await using (var asNobody = await Db.OpenAppAsync())
            {
                await AssertRefusedAsync(() => asNobody.ExecuteScalarAsync<object>(call, new { target }), PostgresErrorCodes.InsufficientPrivilege);
            }

            await using (var noOrganization = await Db.OpenAppAsync(null, a.AdminId))
            {
                await AssertRefusedAsync(() => noOrganization.ExecuteScalarAsync<object>(call, new { target }), PostgresErrorCodes.InsufficientPrivilege);
            }
        }

        Assert.Equal("marketingmanager", await RoleOfAsync(a.AgencyId, target));
        Assert.Null(await RevokedAfterAsync(a.AgencyId, target));
    }

    [Fact]
    public async Task ThePermission_NotTheRole_DecidesWhoMayManageTheTeam()
    {
        var org = await NewOrganizationAsync("company");
        var manager = await AddMemberAsync(org, "marketingmanager");
        var writer = await AddMemberAsync(org, "contentwriter");

        await using var owner = await Db.OpenOwnerAsync();
        await owner.ExecuteAsync(
            "UPDATE role_permissions SET is_granted = true WHERE business_model = 'company' AND role = 'marketingmanager' AND permission_key = 'members.manage'");

        await using (var asManager = await AsAsync(org.AgencyId, manager))
        {
            await asManager.ExecuteScalarAsync<object>("SELECT team_change_role(@writer, 'marketingmanager')", new { writer });
        }

        Assert.Equal("marketingmanager", await RoleOfAsync(org.AgencyId, writer));

        // The Site admin takes it away again: the very next call is refused, with no token involved.
        await owner.ExecuteAsync(
            "UPDATE role_permissions SET is_granted = false WHERE business_model = 'company' AND role = 'marketingmanager' AND permission_key = 'members.manage'");
        await using var again = await AsAsync(org.AgencyId, manager);
        await AssertRefusedAsync(() => again.ExecuteScalarAsync<object>("SELECT * FROM team_list_members()"), PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task ChangingARole_UpdatesIt_AndEndsThatPersonsSessions()
    {
        var org = await NewOrganizationAsync("agency");
        var member = await AddMemberAsync(org, "marketingmanager");
        var bystander = await AddMemberAsync(org, "contentwriter");
        var before = DateTime.UtcNow.AddSeconds(-1);

        await using (var admin = await AsAsync(org.AgencyId, org.AdminId))
        {
            await admin.ExecuteScalarAsync<object>("SELECT team_change_role(@member, 'clientmanager')", new { member });
        }

        Assert.Equal("clientmanager", await RoleOfAsync(org.AgencyId, member));
        Assert.True(await RevokedAfterAsync(org.AgencyId, member) > before);
        Assert.Null(await RevokedAfterAsync(org.AgencyId, bystander));
    }

    [Fact]
    public async Task ChangingARole_ToTheSameRole_ChangesNothingAndEndsNoSession()
    {
        var org = await NewOrganizationAsync("company");
        var member = await AddMemberAsync(org, "contentwriter");

        await using var admin = await AsAsync(org.AgencyId, org.AdminId);
        await admin.ExecuteScalarAsync<object>("SELECT team_change_role(@member, 'contentwriter')", new { member });

        Assert.Null(await RevokedAfterAsync(org.AgencyId, member));
    }

    [Fact]
    public async Task ChangingARole_RefusesYourselfAStrangerAndARoleTheModelDoesNotHave()
    {
        var a = await NewOrganizationAsync("company");
        var b = await NewOrganizationAsync("company");
        var member = await AddMemberAsync(a, "contentwriter");
        var stranger = await AddMemberAsync(b, "contentwriter");

        await using var admin = await AsAsync(a.AgencyId, a.AdminId);
        await AssertRuleAsync(() => admin.ExecuteScalarAsync<object>("SELECT team_change_role(@id, 'contentwriter')", new { id = a.AdminId }), "cannot_change_self");
        await AssertRuleAsync(() => admin.ExecuteScalarAsync<object>("SELECT team_change_role(@id, 'contentwriter')", new { id = stranger }), "member_not_found");
        await AssertRuleAsync(() => admin.ExecuteScalarAsync<object>("SELECT team_change_role(gen_random_uuid(), 'contentwriter')"), "member_not_found");
        await AssertRefusedAsync(
            () => admin.ExecuteScalarAsync<object>("SELECT team_change_role(@member, 'clientmanager')", new { member }),
            PostgresErrorCodes.CheckViolation, "ck_agency_members_role_model");

        Assert.Equal("contentwriter", await RoleOfAsync(a.AgencyId, member));
        Assert.Equal("contentwriter", await RoleOfAsync(b.AgencyId, stranger));
    }

    [Fact]
    public async Task TheLastOrganizationAdmin_CannotBeDemotedOrRemoved_ByAnotherRoleThatHoldsThePermission()
    {
        var org = await NewOrganizationAsync("company");
        var manager = await AddMemberAsync(org, "marketingmanager");
        var writer = await AddMemberAsync(org, "contentwriter");

        // Yourself is refused on its own, so the only way to reach the last admin is a different role that holds the permission.
        await using var owner = await Db.OpenOwnerAsync();
        await owner.ExecuteAsync(
            "UPDATE role_permissions SET is_granted = true WHERE business_model = 'company' AND role = 'marketingmanager' AND permission_key = 'members.manage'");
        try
        {
            await using var asManager = await AsAsync(org.AgencyId, manager);
            await AssertRuleAsync(
                () => asManager.ExecuteScalarAsync<object>("SELECT team_change_role(@id, 'contentwriter')", new { id = org.AdminId }), "last_organization_admin");
            await AssertRuleAsync(
                () => asManager.ExecuteScalarAsync<object>("SELECT team_remove_member(@id)", new { id = org.AdminId }), "last_organization_admin");
            Assert.Equal("organizationadmin", await RoleOfAsync(org.AgencyId, org.AdminId));
            Assert.Null(await RevokedAfterAsync(org.AgencyId, org.AdminId));

            // With a second admin the first may be demoted, and then removing the second is the refused one.
            await asManager.ExecuteScalarAsync<object>("SELECT team_change_role(@writer, 'organizationadmin')", new { writer });
            await asManager.ExecuteScalarAsync<object>("SELECT team_change_role(@id, 'contentwriter')", new { id = org.AdminId });
            await AssertRuleAsync(
                () => asManager.ExecuteScalarAsync<object>("SELECT team_remove_member(@writer)", new { writer }), "last_organization_admin");
            Assert.Equal("contentwriter", await RoleOfAsync(org.AgencyId, org.AdminId));
        }
        finally
        {
            // The database is shared by the tests of this class.
            await owner.ExecuteAsync(
                "UPDATE role_permissions SET is_granted = false WHERE business_model = 'company' AND role = 'marketingmanager' AND permission_key = 'members.manage'");
        }
    }

    [Fact]
    public async Task ARemovedMember_IsGone_TheirAccountIsDeleted_TheEmailIsFree_AndTheOldTokenIsVoid()
    {
        var org = await NewOrganizationAsync("company");
        var hash = await InviteAsync(org, "leaver@example.com", "contentwriter");
        var member = Guid.NewGuid();
        await AcceptAsync(hash, member, passwordHash: null, provider: "google", subject: "sub-leaver", providerEmail: "leaver@example.com");
        var before = DateTime.UtcNow.AddSeconds(-1);

        await using (var admin = await AsAsync(org.AgencyId, org.AdminId))
        {
            await admin.ExecuteScalarAsync<object>("SELECT team_remove_member(@member)", new { member });
        }

        await using var owner = await Db.OpenOwnerAsync();
        Assert.Equal(0, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM users WHERE id = @member", new { member }));
        Assert.Equal(0, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM agency_members WHERE user_id = @member", new { member }));
        Assert.Equal(0, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM user_identities WHERE user_id = @member", new { member }));
        // The revocation outlives the account, or the old token would work again.
        Assert.True(await RevokedAfterAsync(org.AgencyId, member) > before);

        await using var nobody = await Db.OpenAppAsync();
        Assert.False(await nobody.ExecuteScalarAsync<bool>("SELECT auth_email_registered('leaver@example.com')"));

        // The same address can be invited again and accepts as a new person.
        var again = await InviteAsync(org, "leaver@example.com", "marketingmanager");
        Assert.Equal((org.AgencyId, "marketingmanager"), await AcceptAsync(again));
    }

    [Fact]
    public async Task ARemovedMember_KeepsTheirAccount_WhenTheyBelongElsewhereOrAreASiteAdmin()
    {
        var org = await NewOrganizationAsync("company");
        var elsewhere = await NewOrganizationAsync("company");
        var siteAdminMember = await AddMemberAsync(org, "contentwriter");

        await using var owner = await Db.OpenOwnerAsync();
        // A second membership for the founder of another organization: not reachable through the API today, but the rule must hold.
        await owner.ExecuteAsync(
            "INSERT INTO agency_members (agency_id, user_id, business_model, role) SELECT id, @user, business_model, 'marketingmanager' FROM agencies WHERE id = @agency",
            new { user = elsewhere.AdminId, agency = org.AgencyId });
        await owner.ExecuteAsync("INSERT INTO site_admins (user_id) VALUES (@siteAdminMember)", new { siteAdminMember });

        await using var admin = await AsAsync(org.AgencyId, org.AdminId);
        await admin.ExecuteScalarAsync<object>("SELECT team_remove_member(@user)", new { user = elsewhere.AdminId });
        await admin.ExecuteScalarAsync<object>("SELECT team_remove_member(@siteAdminMember)", new { siteAdminMember });

        Assert.Equal(1, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM users WHERE id = @user", new { user = elsewhere.AdminId }));
        Assert.Equal(1, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM users WHERE id = @siteAdminMember", new { siteAdminMember }));
        Assert.Equal(1, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM agency_members WHERE agency_id = @agency AND user_id = @user", new { agency = elsewhere.AgencyId, user = elsewhere.AdminId }));
    }

    [Fact]
    public async Task Removing_RefusesYourselfAStrangerAndANonMember_AndOneOfTwoAdminsMayRemoveTheOther()
    {
        var a = await NewOrganizationAsync("company");
        var b = await NewOrganizationAsync("company");
        var stranger = await AddMemberAsync(b, "contentwriter");

        await using var admin = await AsAsync(a.AgencyId, a.AdminId);
        await AssertRuleAsync(() => admin.ExecuteScalarAsync<object>("SELECT team_remove_member(@id)", new { id = a.AdminId }), "cannot_change_self");
        await AssertRuleAsync(() => admin.ExecuteScalarAsync<object>("SELECT team_remove_member(@id)", new { id = stranger }), "member_not_found");
        await AssertRuleAsync(() => admin.ExecuteScalarAsync<object>("SELECT team_remove_member(gen_random_uuid())"), "member_not_found");
        Assert.Equal("contentwriter", await RoleOfAsync(b.AgencyId, stranger));

        var second = await AddMemberAsync(a, "contentwriter");
        await admin.ExecuteScalarAsync<object>("SELECT team_change_role(@second, 'organizationadmin')", new { second });
        await using var asSecond = await AsAsync(a.AgencyId, second);
        await asSecond.ExecuteScalarAsync<object>("SELECT team_remove_member(@id)", new { id = a.AdminId });

        Assert.Null(await RoleOfAsync(a.AgencyId, a.AdminId));
        Assert.Equal("organizationadmin", await RoleOfAsync(a.AgencyId, second));
    }

    [Fact]
    public async Task TwoAdminsRemovingEachOtherAtTheSameTime_NeverLeaveTheOrganizationWithoutOne()
    {
        for (var round = 0; round < 5; round++)
        {
            var org = await NewOrganizationAsync("company");
            var second = await AddMemberAsync(org, "contentwriter");
            await using (var founder = await AsAsync(org.AgencyId, org.AdminId))
            {
                await founder.ExecuteScalarAsync<object>("SELECT team_change_role(@second, 'organizationadmin')", new { second });
            }

            async Task<string> Remove(Guid caller, Guid target)
            {
                try
                {
                    await using var app = await AsAsync(org.AgencyId, caller);
                    await app.ExecuteScalarAsync<object>("SELECT team_remove_member(@target)", new { target });
                    return "removed";
                }
                catch (PostgresException exception)
                {
                    // The loser is either no longer a member (so no longer allowed) or would remove the last admin.
                    return exception.SqlState == PostgresErrorCodes.InsufficientPrivilege ? "not allowed" : exception.MessageText;
                }
            }

            var outcomes = await Task.WhenAll(Remove(org.AdminId, second), Remove(second, org.AdminId));

            Assert.Equal(1, outcomes.Count(o => o == "removed"));
            Assert.All(outcomes.Where(o => o != "removed"), o => Assert.Contains(o, new[] { "not allowed", "last_organization_admin" }));
            await using var owner = await Db.OpenOwnerAsync();
            Assert.Equal(1, await owner.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM agency_members WHERE agency_id = @agencyId AND role = 'organizationadmin'", new { agencyId = org.AgencyId }));
        }
    }

    // --- Session revocations -----------------------------------------------------------------

    [Fact]
    public async Task TheSnapshot_NeedsNoUser_ShowsRecentRevocations_AndDropsOldOnes()
    {
        var org = await NewOrganizationAsync("company");
        var recent = Guid.NewGuid();
        var old = Guid.NewGuid();
        await using var owner = await Db.OpenOwnerAsync();
        await owner.ExecuteAsync(
            "INSERT INTO session_revocations (agency_id, user_id, valid_after) VALUES (@a, @recent, now() - interval '2 days'), (@a, @old, now() - interval '100 days')",
            new { a = org.AgencyId, recent, old });

        await using var nobody = await Db.OpenAppAsync();
        var rows = (await nobody.QueryAsync<(Guid AgencyId, Guid UserId)>("SELECT agency_id, user_id FROM session_revocations_snapshot()")).ToList();

        // The database is shared by the tests of this class, so look only at this organization's rows.
        Assert.Equal([(org.AgencyId, recent)], rows.Where(r => r.AgencyId == org.AgencyId));
    }

    [Fact]
    public async Task OldRevocations_AreDeleted_WhenANewOneIsWritten()
    {
        var org = await NewOrganizationAsync("company");
        var member = await AddMemberAsync(org, "contentwriter");
        var old = Guid.NewGuid();
        await using var owner = await Db.OpenOwnerAsync();
        await owner.ExecuteAsync(
            "INSERT INTO session_revocations (agency_id, user_id, valid_after) VALUES (@a, @old, now() - interval '100 days')", new { a = org.AgencyId, old });

        await using var admin = await AsAsync(org.AgencyId, org.AdminId);
        await admin.ExecuteScalarAsync<object>("SELECT team_change_role(@member, 'marketingmanager')", new { member });

        Assert.Equal(0, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM session_revocations WHERE user_id = @old", new { old }));
        Assert.Equal(1, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM session_revocations WHERE user_id = @member", new { member }));
    }

    [Fact]
    public async Task AppUser_CannotTouchTheRevocationsTable_OnlyTheFunctionsDo()
    {
        await using var owner = await Db.OpenOwnerAsync();

        foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "TRUNCATE" })
        {
            Assert.False(
                await owner.ExecuteScalarAsync<bool>("SELECT has_table_privilege('app_user', 'public.session_revocations', @p)", new { p = privilege }),
                $"app_user must not have {privilege} on session_revocations.");
        }

        var org = await NewOrganizationAsync("company");
        await using var admin = await AsAsync(org.AgencyId, org.AdminId);
        await AssertRefusedAsync(() => admin.ExecuteAsync("INSERT INTO session_revocations (agency_id, user_id, valid_after) VALUES (@a, @u, now())", new { a = org.AgencyId, u = Guid.NewGuid() }),
            PostgresErrorCodes.InsufficientPrivilege);
    }

    // --- The functions and policies themselves -----------------------------------------------

    private static readonly string[] CallableByApp =
    [
        "team_list_members()", "team_change_role(uuid, text)", "team_remove_member(uuid)", "auth_email_registered(text)",
        "invitation_find(text)", "invitation_accept(text, uuid, text, text, text, text, text)", "session_revocations_snapshot()"
    ];

    private static readonly string[] InternalOnly = ["org_caller_holds(text)", "team_end_sessions(uuid, uuid)"];

    [Fact]
    public async Task TheFunctions_AreDefinerFunctionsWithAFixedSearchPath_NotCallableByPublic()
    {
        await using var owner = await Db.OpenOwnerAsync();

        foreach (var signature in CallableByApp.Concat(InternalOnly))
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
    public async Task AppUser_CanCallTheTeamFunctions_ButNotTheInternalHelpers()
    {
        await using var owner = await Db.OpenOwnerAsync();

        foreach (var signature in CallableByApp)
        {
            Assert.True(
                await owner.ExecuteScalarAsync<bool>("SELECT has_function_privilege('app_user', to_regprocedure(@signature), 'EXECUTE')", new { signature }),
                $"app_user must be able to call {signature}.");
        }

        foreach (var signature in InternalOnly)
        {
            Assert.False(
                await owner.ExecuteScalarAsync<bool>("SELECT has_function_privilege('app_user', to_regprocedure(@signature), 'EXECUTE')", new { signature }),
                $"app_user must not be able to call {signature}.");
        }
    }

    [Fact]
    public async Task TheOwnerPolicies_BelongToTheMigratingRoleOnly_NeverToAppUserOrPublic()
    {
        await using var owner = await Db.OpenOwnerAsync();
        var policies = (await owner.QueryAsync<(string Table, string Name, string[] Roles)>(
            "SELECT tablename, policyname, roles::text[] FROM pg_policies WHERE policyname LIKE 'owner\\_%' AND tablename IN ('users', 'agency_members', 'user_identities', 'member_invitations', 'session_revocations')")).ToList();

        Assert.NotEmpty(policies);
        var current = (await owner.ExecuteScalarAsync<string>("SELECT current_user"))!;
        Assert.All(policies, p => Assert.Equal(new[] { current }, p.Roles));
        Assert.DoesNotContain(policies, p => p.Roles.Contains("app_user") || p.Roles.Contains("public"));

        // Row-level security stays enabled and forced on every table the script touches.
        foreach (var table in new[] { "users", "agency_members", "user_identities", "member_invitations", "session_revocations" })
        {
            Assert.Equal((true, true), await owner.QuerySingleAsync<(bool, bool)>(
                "SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE oid = to_regclass(@t)", new { t = "public." + table }));
        }
    }

    [Fact]
    public async Task AppUser_StillCannotReadOrWriteOtherPeoplesRows_OnTheTablesTheFunctionsTouch()
    {
        var a = await NewOrganizationAsync("company");
        var b = await NewOrganizationAsync("company");
        await AddMemberAsync(a, "contentwriter", "inside@example.com");

        await using var asB = await AsAsync(b.AgencyId, b.AdminId);
        Assert.Equal([b.AdminEmail], await asB.QueryAsync<string>("SELECT email FROM users"));
        Assert.Equal(1, await DbTestSupport.CountAsync(asB, "agency_members"));
        await AssertRefusedAsync(
            () => asB.ExecuteAsync("INSERT INTO users (email, password_hash) VALUES ('sneaky@example.com', 'x')"), PostgresErrorCodes.InsufficientPrivilege);
        await AssertRefusedAsync(
            () => asB.ExecuteAsync("INSERT INTO agency_members (agency_id, user_id, business_model, role) SELECT @a, @u, 'company', 'contentwriter'", new { a = a.AgencyId, u = b.AdminId }),
            PostgresErrorCodes.InsufficientPrivilege);
    }
}

[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class TeamMigrationTests(PostgresFixture postgres) : TeamMigrationTestsBase(postgres)
{
    protected override Task<TestDatabase> CreateDatabaseAsync(PostgresFixture fixture) => fixture.CreateDatabaseAsync();
}

/// <summary>
/// The same on a database owned by an ordinary role. Row-level security is FORCED, so even the owner sees and writes
/// no rows without a policy: without the owner policies of <c>007_team_members.sql</c> the functions would be blind
/// here, and would work only for a superuser.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class TeamMigrationWithLimitedOwnerTests(PostgresFixture postgres) : TeamMigrationTestsBase(postgres)
{
    protected override Task<TestDatabase> CreateDatabaseAsync(PostgresFixture fixture) => fixture.CreateDatabaseWithLimitedOwnerAsync();

    [Fact]
    public async Task TheOwner_IsReallyNotASuperuser_AndCannotBypassRowLevelSecurity()
    {
        await using var owner = await Db.OpenOwnerAsync();

        Assert.Equal((false, false), await owner.QuerySingleAsync<(bool, bool)>(
            "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user"));
    }
}
