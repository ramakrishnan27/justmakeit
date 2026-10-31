using Npgsql;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>
/// Proves tenant isolation for every table in 001_init.sql. Rows are arranged as the database owner
/// (a superuser in the test container); everything under test runs as app_user, like the API.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class RowLevelSecurityTests(PostgresFixture postgres) : IAsyncLifetime
{
    private TestDatabase _db = null!;
    private Guid _agencyA, _agencyB;
    private Guid _userA, _userB, _userBoth;
    private Guid _workspaceA, _workspaceB;
    private Guid _tokenA, _tokenB;

    public async Task InitializeAsync()
    {
        _db = await postgres.CreateDatabaseAsync();
        await using var owner = await _db.OpenOwnerAsync();

        _agencyA = await DbTestSupport.InsertAgencyAsync(owner, "Agency A");
        _agencyB = await DbTestSupport.InsertAgencyAsync(owner, "Agency B");
        _userA = await DbTestSupport.InsertUserAsync(owner, "a@example.com");
        _userB = await DbTestSupport.InsertUserAsync(owner, "b@example.com");
        _userBoth = await DbTestSupport.InsertUserAsync(owner, "both@example.com");

        const string member = "INSERT INTO agency_members (agency_id, user_id, role) VALUES ($1, $2, $3)";
        await DbTestSupport.ExecuteAsync(owner, member, _agencyA, _userA, "owner");
        await DbTestSupport.ExecuteAsync(owner, member, _agencyB, _userB, "owner");
        await DbTestSupport.ExecuteAsync(owner, member, _agencyA, _userBoth, "viewer");
        await DbTestSupport.ExecuteAsync(owner, member, _agencyB, _userBoth, "viewer");

        _workspaceA = await DbTestSupport.InsertWorkspaceAsync(owner, _agencyA, "Workspace A");
        _workspaceB = await DbTestSupport.InsertWorkspaceAsync(owner, _agencyB, "Workspace B");

        _tokenA = await DbTestSupport.InsertRefreshTokenAsync(owner, _agencyA, _userA, "hash-a");
        _tokenB = await DbTestSupport.InsertRefreshTokenAsync(owner, _agencyB, _userB, "hash-b");
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    // --- No tenant: nothing is visible ----------------------------------------------------------

    [Theory]
    [InlineData("agencies")]
    [InlineData("users")]
    [InlineData("agency_members")]
    [InlineData("workspaces")]
    [InlineData("refresh_tokens")]
    public async Task WithoutATenant_NoRowsAreVisible(string table)
    {
        await using var app = await _db.OpenAppAsync();

        Assert.Equal(0, await DbTestSupport.CountAsync(app, table));
    }

    // --- Reads: each agency sees only its own data ----------------------------------------------

    [Fact]
    public async Task Agencies_AreLimitedToTheCurrentAgency()
    {
        await using var app = await _db.OpenAppAsync(_agencyA, _userA);

        Assert.Equal(1, await DbTestSupport.CountAsync(app, "agencies"));
        Assert.Equal(_agencyA, await DbTestSupport.ScalarAsync<Guid>(app, "SELECT id FROM agencies"));
    }

    [Fact]
    public async Task Agencies_AlsoIncludeThoseTheUserBelongsTo_ForTheAgencySwitcher()
    {
        await using var app = await _db.OpenAppAsync(_agencyA, _userBoth);

        Assert.Equal(2, await DbTestSupport.CountAsync(app, "agencies"));
    }

    [Fact]
    public async Task Agencies_ForAUserOutsideTheCurrentAgency_ShowTheirOwnAgencyAndTheCurrentOneOnly()
    {
        // userB belongs to B only. With A as the current agency they see A (tenant) and B (membership).
        await using var member = await _db.OpenAppAsync(_agencyA, _userB);
        Assert.Equal(2, await DbTestSupport.CountAsync(member, "agencies"));

        // A user with no memberships at all sees just the current agency.
        await using var stranger = await _db.OpenAppAsync(_agencyA, Guid.NewGuid());
        Assert.Equal(1, await DbTestSupport.CountAsync(stranger, "agencies"));
    }

    [Fact]
    public async Task Workspaces_AgencyACannotReadAgencyB()
    {
        await using var app = await _db.OpenAppAsync(_agencyA, _userA);

        Assert.Equal(1, await DbTestSupport.CountAsync(app, "workspaces"));
        Assert.Equal(_workspaceA, await DbTestSupport.ScalarAsync<Guid>(app, "SELECT id FROM workspaces"));
        Assert.Equal(0, await DbTestSupport.ScalarAsync<long>(app, $"SELECT count(*) FROM workspaces WHERE id = '{_workspaceB}'"));
    }

    [Fact]
    public async Task Workspaces_AreVisibleWithoutAUser_BecauseTheyBelongToTheAgency()
    {
        await using var app = await _db.OpenAppAsync(_agencyB, null);

        Assert.Equal(_workspaceB, await DbTestSupport.ScalarAsync<Guid>(app, "SELECT id FROM workspaces"));
    }

    [Fact]
    public async Task AgencyMembers_ShowTheCurrentAgencyPlusTheCallersOwnMembershipsElsewhere()
    {
        // userBoth acts in A: sees A's two members, plus its own membership in B, but not userB's.
        await using var app = await _db.OpenAppAsync(_agencyA, _userBoth);

        Assert.Equal(3, await DbTestSupport.CountAsync(app, "agency_members"));
        Assert.Equal(0, await DbTestSupport.ScalarAsync<long>(app, $"SELECT count(*) FROM agency_members WHERE user_id = '{_userB}'"));
    }

    [Fact]
    public async Task AgencyMembers_ForAUserWithOneAgency_ShowOnlyThatAgency()
    {
        await using var app = await _db.OpenAppAsync(_agencyB, _userB);

        Assert.Equal(2, await DbTestSupport.CountAsync(app, "agency_members")); // userB and userBoth in B
        Assert.Equal(0, await DbTestSupport.ScalarAsync<long>(app, $"SELECT count(*) FROM agency_members WHERE user_id = '{_userA}'"));
    }

    [Fact]
    public async Task AgencyMembers_AtLoginTime_ShowOnlyTheUsersOwnMemberships()
    {
        await using var app = await _db.OpenAppAsync(null, _userBoth); // user known, agency not yet chosen

        Assert.Equal(2, await DbTestSupport.CountAsync(app, "agency_members"));
    }

    [Fact]
    public async Task Users_ShowOnlyTheCurrentUserRow()
    {
        await using var app = await _db.OpenAppAsync(_agencyA, _userA);

        Assert.Equal(1, await DbTestSupport.CountAsync(app, "users"));
        Assert.Equal(_userA, await DbTestSupport.ScalarAsync<Guid>(app, "SELECT id FROM users"));
    }

    [Fact]
    public async Task Users_AreHiddenWhenOnlyAnAgencyIsSet()
    {
        await using var app = await _db.OpenAppAsync(_agencyA, null);

        Assert.Equal(0, await DbTestSupport.CountAsync(app, "users"));
    }

    [Fact]
    public async Task RefreshTokens_ShowOnlyTheCallersOwnTokensInTheCurrentAgency()
    {
        await using var app = await _db.OpenAppAsync(_agencyA, _userA);

        Assert.Equal(1, await DbTestSupport.CountAsync(app, "refresh_tokens"));
        Assert.Equal(_tokenA, await DbTestSupport.ScalarAsync<Guid>(app, "SELECT id FROM refresh_tokens"));
    }

    [Fact]
    public async Task RefreshTokens_AreHiddenWhenTheAgencyDoesNotMatch()
    {
        await using var app = await _db.OpenAppAsync(_agencyA, _userB); // userB's token belongs to agency B

        Assert.Equal(0, await DbTestSupport.CountAsync(app, "refresh_tokens"));
    }

    [Fact]
    public async Task RefreshTokens_AreHiddenWhenOnlyAnAgencyIsSet()
    {
        await using var app = await _db.OpenAppAsync(_agencyA, null);

        Assert.Equal(0, await DbTestSupport.CountAsync(app, "refresh_tokens"));
    }

    // --- Writes: another tenant's rows can be neither created, changed nor deleted ----------------

    [Theory]
    [InlineData("INSERT INTO workspaces (agency_id, name) VALUES ('{B}', 'intruder')")]
    [InlineData("INSERT INTO agency_members (agency_id, user_id, role) VALUES ('{B}', '{A_USER}', 'owner')")]
    [InlineData("INSERT INTO agencies (id, name) VALUES (gen_random_uuid(), 'intruder')")]
    [InlineData("INSERT INTO users (id, email, password_hash) VALUES (gen_random_uuid(), 'intruder@example.com', 'x')")]
    [InlineData("INSERT INTO refresh_tokens (agency_id, user_id, token_hash, expires_at) VALUES ('{B}', '{A_USER}', 'x', now() + interval '1 day')")]
    [InlineData("INSERT INTO refresh_tokens (agency_id, user_id, token_hash, expires_at) VALUES ('{A}', '{B_USER}', 'x', now() + interval '1 day')")]
    public async Task Insert_ForAnotherTenant_IsRejectedByRowLevelSecurity(string sqlTemplate)
    {
        await using var app = await _db.OpenAppAsync(_agencyA, _userA);
        var sql = sqlTemplate
            .Replace("{A}", _agencyA.ToString()).Replace("{B}", _agencyB.ToString())
            .Replace("{A_USER}", _userA.ToString()).Replace("{B_USER}", _userB.ToString());

        var exception = await Assert.ThrowsAsync<PostgresException>(() => DbTestSupport.ExecuteAsync(app, sql));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Theory]
    [InlineData("UPDATE workspaces SET name = 'hacked' WHERE id = '{B_WS}'")]
    [InlineData("UPDATE agencies SET name = 'hacked' WHERE id = '{B}'")]
    [InlineData("UPDATE agency_members SET role = 'owner' WHERE agency_id = '{B}'")]
    [InlineData("UPDATE users SET full_name = 'hacked' WHERE id = '{B_USER}'")]
    [InlineData("UPDATE refresh_tokens SET revoked_at = now() WHERE id = '{B_TOKEN}'")]
    [InlineData("DELETE FROM workspaces WHERE id = '{B_WS}'")]
    [InlineData("DELETE FROM agencies WHERE id = '{B}'")]
    [InlineData("DELETE FROM agency_members WHERE agency_id = '{B}'")]
    [InlineData("DELETE FROM users WHERE id = '{B_USER}'")]
    [InlineData("DELETE FROM refresh_tokens WHERE id = '{B_TOKEN}'")]
    public async Task UpdateOrDelete_OnAnotherTenantsRows_AffectsNothing(string sqlTemplate)
    {
        await using var app = await _db.OpenAppAsync(_agencyA, _userA);
        var sql = sqlTemplate
            .Replace("{B}", _agencyB.ToString()).Replace("{B_USER}", _userB.ToString())
            .Replace("{B_WS}", _workspaceB.ToString()).Replace("{B_TOKEN}", _tokenB.ToString());

        Assert.Equal(0, await DbTestSupport.ExecuteAsync(app, sql));

        await using var owner = await _db.OpenOwnerAsync();
        Assert.Equal(1, await DbTestSupport.CountAsync(owner, "refresh_tokens WHERE revoked_at IS NULL AND id = '" + _tokenB + "'"));
        Assert.Equal(1, await DbTestSupport.CountAsync(owner, "workspaces WHERE name = 'Workspace B'"));
        Assert.Equal(1, await DbTestSupport.CountAsync(owner, "agencies WHERE name = 'Agency B'"));
        Assert.Equal(2, await DbTestSupport.CountAsync(owner, "agency_members WHERE agency_id = '" + _agencyB + "'"));
        Assert.Equal(1, await DbTestSupport.CountAsync(owner, "users WHERE id = '" + _userB + "' AND full_name = 'Test User'"));
    }

    [Fact]
    public async Task Update_CannotMoveARowToAnotherTenant()
    {
        await using var app = await _db.OpenAppAsync(_agencyA, _userA);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => DbTestSupport.ExecuteAsync(
            app, $"UPDATE workspaces SET agency_id = '{_agencyB}' WHERE id = '{_workspaceA}'"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    // --- Allowed paths -----------------------------------------------------------------------------

    [Fact]
    public async Task Workspaces_CanBeCreatedChangedAndDeletedInTheCurrentAgency()
    {
        await using var app = await _db.OpenAppAsync(_agencyA, _userA);

        Assert.Equal(1, await DbTestSupport.ExecuteAsync(app, "INSERT INTO workspaces (agency_id, name) VALUES ($1, 'Mine')", _agencyA));
        Assert.Equal(1, await DbTestSupport.ExecuteAsync(app, "UPDATE workspaces SET name = 'Renamed' WHERE name = 'Mine'"));
        Assert.Equal(1, await DbTestSupport.ExecuteAsync(app, "DELETE FROM workspaces WHERE name = 'Renamed'"));
    }

    [Fact]
    public async Task Registration_CanCreateAUserAgencyMembershipWorkspaceAndTokenBeforeAnyTenantExists()
    {
        // Registration picks the new ids first and opens the connection for them, so every policy passes.
        var agency = Guid.NewGuid();
        var user = Guid.NewGuid();
        await using var app = await _db.OpenAppAsync(agency, user);
        await using var transaction = await app.BeginTransactionAsync();

        await DbTestSupport.ExecuteAsync(app, "INSERT INTO users (id, email, password_hash, full_name) VALUES ($1, 'new@example.com', 'x', 'New')", user);
        await DbTestSupport.ExecuteAsync(app, "INSERT INTO agencies (id, name, plan) VALUES ($1, 'New agency', 'trial')", agency);
        await DbTestSupport.ExecuteAsync(app, "INSERT INTO agency_members (agency_id, user_id, role) VALUES ($1, $2, 'owner')", agency, user);
        await DbTestSupport.ExecuteAsync(app, "INSERT INTO workspaces (agency_id, name) VALUES ($1, 'First workspace')", agency);
        await DbTestSupport.ExecuteAsync(
            app,
            "INSERT INTO refresh_tokens (agency_id, user_id, token_hash, expires_at) VALUES ($1, $2, 'new-hash', now() + interval '30 days')",
            agency, user);
        await transaction.CommitAsync();

        Assert.Equal(1, await DbTestSupport.CountAsync(app, "workspaces"));
        Assert.Equal(1, await DbTestSupport.CountAsync(app, "refresh_tokens"));
    }

    [Fact]
    public async Task Users_EmailUniquenessIsEnforcedAcrossAllTenants_IgnoringCase()
    {
        // The unique index sees every row, so a duplicate email is reported even though the row is invisible.
        var user = Guid.NewGuid();
        await using var app = await _db.OpenAppAsync(Guid.NewGuid(), user);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => DbTestSupport.ExecuteAsync(
            app, "INSERT INTO users (id, email, password_hash) VALUES ($1, 'A@EXAMPLE.COM', 'x')", user));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task Refresh_RotationAndRevocationWork_ForTheOwnerInTheirAgency()
    {
        await using var app = await _db.OpenAppAsync(_agencyA, _userA);

        Assert.Equal(1, await DbTestSupport.ExecuteAsync(app, "UPDATE refresh_tokens SET revoked_at = now() WHERE id = $1", _tokenA));
        Assert.Equal(1, await DbTestSupport.ExecuteAsync(
            app,
            "INSERT INTO refresh_tokens (agency_id, user_id, token_hash, expires_at) VALUES ($1, $2, 'rotated', now() + interval '30 days')",
            _agencyA, _userA));
    }
}
