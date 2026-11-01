using Dapper;
using Npgsql;
using Platform.Auth.Models;
using Platform.Auth.Repositories;
using Platform.Auth.Services;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Auth;

/// <summary>
/// Registration and lookup queries, run as app_user through the connection factory, exactly as the API
/// runs them. Rows are checked as the database owner, who sees everything.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class AccountRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private TestDatabase _db = null!;
    private AccountRepository _repository = null!;

    public async Task InitializeAsync()
    {
        DapperConfiguration.Configure();
        _db = await postgres.CreateDatabaseAsync();
        _repository = new AccountRepository(_db.CreateFactory());
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static NewAccount Account(
        string email = "ana@example.com",
        string agencyName = "Acme",
        string hash = "pbkdf2$1$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==",
        BusinessModel model = BusinessModel.Agency) =>
        new(Guid.NewGuid(), email, hash, "Ana Smith", Guid.NewGuid(), agencyName, model, model.FounderRole(), Guid.NewGuid(), "Main workspace");

    private async Task<long> CountAsync(string table)
    {
        await using var owner = await _db.OpenOwnerAsync();
        return await DbTestSupport.CountAsync(owner, table);
    }

    // --- CreateAccountAsync ------------------------------------------------------------------

    [Fact]
    public async Task CreateAccount_StoresTheUserAgencyOwnerMembershipAndWorkspace()
    {
        var account = Account();

        Assert.True(await _repository.CreateAccountAsync(account, CancellationToken.None));

        await using var owner = await _db.OpenOwnerAsync();
        var agency = await owner.QuerySingleAsync<(string Name, string Plan)>(
            "SELECT name, plan FROM agencies WHERE id = @id", new { id = account.AgencyId });
        Assert.Equal(("Acme", "trial"), agency);

        var user = await owner.QuerySingleAsync<(string Email, string PasswordHash, string FullName)>(
            "SELECT email, password_hash, full_name FROM users WHERE id = @id", new { id = account.UserId });
        Assert.Equal(("ana@example.com", account.PasswordHash, "Ana Smith"), user);

        var role = await owner.QuerySingleAsync<string>(
            "SELECT role FROM agency_members WHERE agency_id = @agencyId AND user_id = @userId",
            new { agencyId = account.AgencyId, userId = account.UserId });
        Assert.Equal("organizationadmin", role);

        var workspace = await owner.QuerySingleAsync<(Guid AgencyId, string Name)>(
            "SELECT agency_id, name FROM workspaces WHERE id = @id", new { id = account.WorkspaceId });
        Assert.Equal((account.AgencyId, "Main workspace"), workspace);
    }

    [Theory]
    [InlineData(BusinessModel.Company, "company", "organizationadmin")]
    [InlineData(BusinessModel.Agency, "agency", "organizationadmin")]
    [InlineData(BusinessModel.Creator, "creator", "contentwriter")]
    public async Task CreateAccount_StoresTheBusinessModel_OnTheOrganizationAndOnTheFoundersMembership(
        BusinessModel model, string storedModel, string storedRole)
    {
        var account = Account(model: model);

        Assert.True(await _repository.CreateAccountAsync(account, CancellationToken.None));

        await using var owner = await _db.OpenOwnerAsync();
        Assert.Equal(storedModel, await owner.QuerySingleAsync<string>(
            "SELECT business_model FROM agencies WHERE id = @id", new { id = account.AgencyId }));
        Assert.Equal((storedModel, storedRole), await owner.QuerySingleAsync<(string, string)>(
            "SELECT business_model, role FROM agency_members WHERE agency_id = @id", new { id = account.AgencyId }));
    }

    [Theory]
    [InlineData(BusinessModel.Creator, OrganizationRole.OrganizationAdmin)]
    [InlineData(BusinessModel.Creator, OrganizationRole.MarketingManager)]
    [InlineData(BusinessModel.Creator, OrganizationRole.ClientManager)]
    [InlineData(BusinessModel.Company, OrganizationRole.ClientManager)]
    public async Task CreateAccount_WithARoleTheModelDoesNotAllow_IsRefusedByTheDatabase_AndCreatesNothing(
        BusinessModel model, OrganizationRole role)
    {
        var broken = Account(model: model) with { Role = role };

        var exception = await Assert.ThrowsAsync<PostgresException>(() => _repository.CreateAccountAsync(broken, CancellationToken.None));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_agency_members_role_model", exception.ConstraintName);
        Assert.Equal(0, await CountAsync("agencies"));
        Assert.Equal(0, await CountAsync("users"));
        Assert.Equal(0, await CountAsync("workspaces"));
    }

    [Fact]
    public async Task CreateAccount_WithATakenEmail_ReturnsFalse_AndCreatesNothing()
    {
        Assert.True(await _repository.CreateAccountAsync(Account("ana@example.com", "First agency"), CancellationToken.None));

        var second = Account("ana@example.com", "Second agency");
        Assert.False(await _repository.CreateAccountAsync(second, CancellationToken.None));

        // The second registration's agency was inserted before the email clash and must not survive it.
        Assert.Equal(1, await CountAsync("agencies"));
        Assert.Equal(1, await CountAsync("users"));
        Assert.Equal(1, await CountAsync("agency_members"));
        Assert.Equal(1, await CountAsync("workspaces"));
        await using var owner = await _db.OpenOwnerAsync();
        Assert.Equal(0, await owner.ExecuteScalarAsync<long>("SELECT count(*) FROM agencies WHERE id = @id", new { id = second.AgencyId }));
    }

    [Theory]
    [InlineData("ANA@EXAMPLE.COM")]
    [InlineData("Ana@Example.Com")]
    public async Task CreateAccount_TreatsEmailsAsCaseInsensitive(string differentCase)
    {
        Assert.True(await _repository.CreateAccountAsync(Account("ana@example.com"), CancellationToken.None));

        Assert.False(await _repository.CreateAccountAsync(Account(differentCase), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAccount_ForADifferentEmail_Works_NextToTheFirst()
    {
        Assert.True(await _repository.CreateAccountAsync(Account("one@example.com", "One"), CancellationToken.None));
        Assert.True(await _repository.CreateAccountAsync(Account("two@example.com", "Two"), CancellationToken.None));

        Assert.Equal(2, await CountAsync("agencies"));
        Assert.Equal(2, await CountAsync("users"));
    }

    [Fact]
    public async Task CreateAccount_WhenTwoRegistrationsRaceForOneEmail_ExactlyOneWins()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(i => Task.Run(() => _repository.CreateAccountAsync(Account("race@example.com", $"Agency {i}"), CancellationToken.None))));

        Assert.Equal(1, results.Count(created => created));
        Assert.Equal(1, await CountAsync("agencies"));
        Assert.Equal(1, await CountAsync("users"));
    }

    [Fact]
    public async Task CreateAccount_DoesNotMistakeAnotherConstraintViolationForATakenEmail()
    {
        // A name over 200 characters breaks a CHECK on agencies, not the email index: that must surface as an error.
        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => _repository.CreateAccountAsync(Account(agencyName: new string('a', 201)), CancellationToken.None));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(0, await CountAsync("agencies"));
        Assert.Equal(0, await CountAsync("users"));
    }

    [Fact]
    public async Task CreateAccount_ThatFailsPartWay_LeavesNoRowsBehind()
    {
        // The workspace name breaks its CHECK last, after the agency, user and membership were inserted.
        var broken = Account() with { WorkspaceName = new string('w', 201) };

        await Assert.ThrowsAsync<PostgresException>(() => _repository.CreateAccountAsync(broken, CancellationToken.None));

        Assert.Equal(0, await CountAsync("agencies"));
        Assert.Equal(0, await CountAsync("users"));
        Assert.Equal(0, await CountAsync("agency_members"));
    }

    [Fact]
    public async Task ARegisteredAccount_IsVisibleToItsOwnTenant_AndInvisibleToEveryoneElse()
    {
        var first = Account("one@example.com", "One");
        var second = Account("two@example.com", "Two");
        await _repository.CreateAccountAsync(first, CancellationToken.None);
        await _repository.CreateAccountAsync(second, CancellationToken.None);

        await using var asFirst = await _db.OpenAppAsync(first.AgencyId, first.UserId);
        Assert.Equal(1, await DbTestSupport.CountAsync(asFirst, "agencies"));
        Assert.Equal(1, await DbTestSupport.CountAsync(asFirst, "workspaces"));
        Assert.Equal(1, await DbTestSupport.CountAsync(asFirst, "users"));
        Assert.Equal(first.AgencyId, await DbTestSupport.ScalarAsync<Guid>(asFirst, "SELECT id FROM agencies"));
        Assert.Equal(first.UserId, await DbTestSupport.ScalarAsync<Guid>(asFirst, "SELECT id FROM users"));

        await using var asSecond = await _db.OpenAppAsync(second.AgencyId, second.UserId);
        Assert.Equal(second.AgencyId, await DbTestSupport.ScalarAsync<Guid>(asSecond, "SELECT id FROM agencies"));
        Assert.Equal(0, await asSecond.ExecuteScalarAsync<long>("SELECT count(*) FROM workspaces WHERE agency_id = @id", new { id = first.AgencyId }));
    }

    // --- FindLoginByEmailAsync ---------------------------------------------------------------

    [Fact]
    public async Task FindLogin_ReturnsTheUserWithTheirPasswordHash_WithoutAnyTenant()
    {
        var account = Account("ana@example.com", hash: "pbkdf2$600000$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==");
        await _repository.CreateAccountAsync(account, CancellationToken.None);

        var found = await _repository.FindLoginByEmailAsync("ana@example.com", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(account.UserId, found.Id);
        Assert.Equal("ana@example.com", found.Email);
        Assert.Equal("Ana Smith", found.FullName);
        Assert.Equal(account.PasswordHash, found.PasswordHash);
    }

    [Theory]
    [InlineData("ANA@EXAMPLE.COM")]
    [InlineData("Ana@example.com")]
    public async Task FindLogin_IsCaseInsensitive(string email)
    {
        var account = Account("ana@example.com");
        await _repository.CreateAccountAsync(account, CancellationToken.None);

        Assert.Equal(account.UserId, (await _repository.FindLoginByEmailAsync(email, CancellationToken.None))?.Id);
    }

    [Theory]
    [InlineData("nobody@example.com")]
    [InlineData("")]
    [InlineData("%")]
    [InlineData("%@example.com")]
    [InlineData("ana@example.co_")]
    [InlineData("ana@example.com' OR '1'='1")]
    public async Task FindLogin_MatchesOnlyTheExactEmail_NeverAPatternOrInjectedSql(string email)
    {
        await _repository.CreateAccountAsync(Account("ana@example.com"), CancellationToken.None);
        await _repository.CreateAccountAsync(Account("bob@example.com", "Bob"), CancellationToken.None);

        Assert.Null(await _repository.FindLoginByEmailAsync(email, CancellationToken.None));
    }

    [Fact]
    public async Task FindLogin_ReturnsOneUserOnly_EvenWithManyRegistered()
    {
        var wanted = Account("target@example.com", "Target");
        await _repository.CreateAccountAsync(Account("first@example.com", "First"), CancellationToken.None);
        await _repository.CreateAccountAsync(wanted, CancellationToken.None);
        await _repository.CreateAccountAsync(Account("last@example.com", "Last"), CancellationToken.None);

        Assert.Equal(wanted.UserId, (await _repository.FindLoginByEmailAsync("target@example.com", CancellationToken.None))!.Id);
    }

    [Fact]
    public async Task WithoutTheLookupFunction_AppUserCannotSeeAnyUser()
    {
        await _repository.CreateAccountAsync(Account(), CancellationToken.None);

        // The lookup works only through the function: a plain query with no tenant finds nobody.
        await using var noTenant = await _db.OpenAppAsync();
        Assert.Equal(0, await DbTestSupport.CountAsync(noTenant, "users"));
    }

    // --- GetUserAsync ------------------------------------------------------------------------

    [Fact]
    public async Task GetUser_ReturnsTheUsersOwnRecord_WithoutTheHash()
    {
        var account = Account();
        await _repository.CreateAccountAsync(account, CancellationToken.None);

        var user = await _repository.GetUserAsync(account.UserId, CancellationToken.None);

        Assert.NotNull(user);
        Assert.Equal(account.UserId, user.Id);
        Assert.Equal("ana@example.com", user.Email);
        Assert.Equal("Ana Smith", user.FullName);
    }

    [Fact]
    public async Task GetUser_ForAnUnknownUser_ReturnsNull()
    {
        Assert.Null(await _repository.GetUserAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
