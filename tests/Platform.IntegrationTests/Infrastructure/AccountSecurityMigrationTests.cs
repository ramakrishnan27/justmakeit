using Dapper;
using Npgsql;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>
/// <c>009_account_security.sql</c>: <c>auth_end_own_sessions()</c>, which a password change calls to end the person's other
/// sessions. A third part of <see cref="TeamMigrationTestsBase"/> (it needs the same arranging, and so runs on both a
/// database owned by a superuser and one owned by an ordinary role that row-level security binds).
/// </summary>
public abstract partial class TeamMigrationTestsBase
{
    private async Task ExecuteAsOwnerAsync(string sql, object? parameters = null)
    {
        await using var owner = await Db.OpenOwnerAsync();
        await owner.ExecuteAsync(sql, parameters);
    }

    [Fact]
    public async Task EndingYourOwnSessions_RecordsAnEndForEveryOrganizationYouBelongTo_AboutASecondAgo_AndTouchesNobodyElse()
    {
        var a = await NewOrganizationAsync("company");
        var b = await NewOrganizationAsync("agency");
        var colleague = await AddMemberAsync(a, "contentwriter");
        var stranger = await AddMemberAsync(b, "contentwriter");
        // The founder of a also works in b (as an invited person does when the organization switcher exists).
        await ExecuteAsOwnerAsync(
            "INSERT INTO agency_members (agency_id, user_id, business_model, role) VALUES (@agencyId, @userId, 'agency', 'contentwriter')",
            new { agencyId = b.AgencyId, userId = a.AdminId });
        var before = DateTime.UtcNow;

        await using (var asUser = await Db.OpenAppAsync(null, a.AdminId))
        {
            await asUser.ExecuteScalarAsync<object>("SELECT auth_end_own_sessions()");
        }

        var after = DateTime.UtcNow;
        foreach (var agencyId in new[] { a.AgencyId, b.AgencyId })
        {
            var validAfter = await RevokedAfterAsync(agencyId, a.AdminId);
            Assert.NotNull(validAfter);
            // One second before the change, so the token issued right after it, for the session that asked, is not void.
            Assert.InRange(validAfter.Value, before.AddSeconds(-1.5), after.AddSeconds(-0.5));
        }

        Assert.Null(await RevokedAfterAsync(a.AgencyId, colleague));
        Assert.Null(await RevokedAfterAsync(b.AgencyId, stranger));
        Assert.Null(await RevokedAfterAsync(b.AgencyId, b.AdminId));
    }

    [Fact]
    public async Task EndingYourOwnSessions_OnlyEverMovesAnEndForward()
    {
        var org = await NewOrganizationAsync("company");
        var later = await AddMemberAsync(org, "contentwriter");
        await ExecuteAsOwnerAsync(
            "INSERT INTO session_revocations (agency_id, user_id, valid_after) VALUES (@agencyId, @userId, now() + interval '1 hour')",
            new { agencyId = org.AgencyId, userId = later });
        await ExecuteAsOwnerAsync(
            "INSERT INTO session_revocations (agency_id, user_id, valid_after) VALUES (@agencyId, @userId, now() - interval '2 days')",
            new { agencyId = org.AgencyId, userId = org.AdminId });

        await using (var asLater = await Db.OpenAppAsync(null, later))
        {
            await asLater.ExecuteScalarAsync<object>("SELECT auth_end_own_sessions()");
        }

        await using (var asAdmin = await Db.OpenAppAsync(null, org.AdminId))
        {
            await asAdmin.ExecuteScalarAsync<object>("SELECT auth_end_own_sessions()");
        }

        // The end that was already recorded for later is not taken back; the old one is brought up to date.
        Assert.True(await RevokedAfterAsync(org.AgencyId, later) > DateTime.UtcNow.AddMinutes(30));
        Assert.True(await RevokedAfterAsync(org.AgencyId, org.AdminId) > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task EndingYourOwnSessions_NeedsASignedInUser_AndForSomeoneWithNoOrganizationDoesNothing()
    {
        var org = await NewOrganizationAsync("company");

        await using (var nobody = await Db.OpenAppAsync())
        {
            await AssertRefusedAsync(() => nobody.ExecuteScalarAsync<object>("SELECT auth_end_own_sessions()"), PostgresErrorCodes.InsufficientPrivilege);
        }

        await using (var noMemberships = await Db.OpenAppAsync(null, Guid.NewGuid()))
        {
            await noMemberships.ExecuteScalarAsync<object>("SELECT auth_end_own_sessions()");
        }

        await using var owner = await Db.OpenOwnerAsync();
        Assert.Equal(0, await owner.ExecuteScalarAsync<int>("SELECT count(*) FROM session_revocations WHERE agency_id = @agencyId", new { agencyId = org.AgencyId }));
    }

    [Fact]
    public async Task TheApplicationRole_CanChangeOnlyItsOwnPassword_AndCannotWriteRevocationsItself()
    {
        var org = await NewOrganizationAsync("company");
        var colleague = await AddMemberAsync(org, "contentwriter");

        await using var asAdmin = await Db.OpenAppAsync(org.AgencyId, org.AdminId);
        // The row is theirs (self_access) ...
        Assert.Equal(1, await asAdmin.ExecuteAsync("UPDATE users SET password_hash = 'pbkdf2$changed' WHERE id = @id", new { id = org.AdminId }));
        // ... somebody else's is not, whatever the organization, and the table that ends sessions is closed to them.
        Assert.Equal(0, await asAdmin.ExecuteAsync("UPDATE users SET password_hash = 'pbkdf2$hijacked' WHERE id = @id", new { id = colleague }));
        await AssertRefusedAsync(
            () => asAdmin.ExecuteAsync("INSERT INTO session_revocations (agency_id, user_id, valid_after) VALUES (@a, @u, now())", new { a = org.AgencyId, u = org.AdminId }),
            PostgresErrorCodes.InsufficientPrivilege);
    }
}
