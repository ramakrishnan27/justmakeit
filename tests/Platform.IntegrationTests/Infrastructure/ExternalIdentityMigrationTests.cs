using Dapper;
using Npgsql;
using Platform.Auth.Models;
using Platform.Auth.Repositories;
using Platform.Auth.Services;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>
/// <c>004_external_identities.sql</c>: a user may have no password, an outside identity (Google) is
/// stored in its own table that the application role can read and add to but never change, the lookup
/// before any user is known, and the rule that every user has a way in.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class ExternalIdentityMigrationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync()
    {
        DapperConfiguration.Configure();
        _db = await postgres.CreateDatabaseAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static async Task<PostgresException> RefusedAsync(Func<Task> action, string sqlState, string? constraint = null)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(sqlState, exception.SqlState);
        if (constraint is not null)
        {
            Assert.Equal(constraint, exception.ConstraintName);
        }

        return exception;
    }

    private static Task<int> InsertUserAsync(NpgsqlConnection connection, Guid id, string email, string? passwordHash) =>
        DbTestSupport.ExecuteAsync(
            connection, "INSERT INTO users (id, email, password_hash) VALUES ($1, $2, $3)",
            id, email, passwordHash is null ? DBNull.Value : passwordHash);

    private static Task<int> InsertIdentityAsync(NpgsqlConnection connection, Guid userId, string subject, string provider = "google") =>
        DbTestSupport.ExecuteAsync(
            connection, "INSERT INTO user_identities (user_id, provider, subject, email) VALUES ($1, $2, $3, 'x@gmail.com')",
            userId, provider, subject);

    // --- Users may have no password, but not no way in ---------------------------------------

    [Fact]
    public async Task ThePasswordColumn_IsNoLongerRequired()
    {
        await using var owner = await _db.OpenOwnerAsync();

        Assert.Equal("YES", await DbTestSupport.ScalarAsync<string>(
            owner, "SELECT is_nullable FROM information_schema.columns WHERE table_name = 'users' AND column_name = 'password_hash'"));
    }

    [Fact]
    public async Task AUserWithAPasswordAndNoIdentity_IsFine()
    {
        await using var owner = await _db.OpenOwnerAsync();

        Assert.Equal(1, await InsertUserAsync(owner, Guid.NewGuid(), "pw@example.com", "pbkdf2$1$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA=="));
    }

    [Fact]
    public async Task AUserWithNoPasswordAndNoIdentity_IsRefusedWhenItCommits()
    {
        await using var owner = await _db.OpenOwnerAsync();

        await RefusedAsync(
            () => InsertUserAsync(owner, Guid.NewGuid(), "nobody@example.com", null), PostgresErrorCodes.CheckViolation, "ck_users_credential");
        Assert.Equal(0, await DbTestSupport.CountAsync(owner, "users"));
    }

    [Fact]
    public async Task AUserWithNoPassword_IsFine_WhenTheIdentityIsAddedInTheSameTransaction()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var id = Guid.NewGuid();

        await using (var transaction = await owner.BeginTransactionAsync())
        {
            await InsertUserAsync(owner, id, "google@example.com", null);
            await InsertIdentityAsync(owner, id, "sub-1");
            await transaction.CommitAsync();
        }

        Assert.Equal(1, await DbTestSupport.CountAsync(owner, "users WHERE password_hash IS NULL"));
    }

    [Fact]
    public async Task ClearingAPassword_IsRefused_ForAUserWithNoIdentity_AndAllowed_ForOneWithAnIdentity()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var plain = Guid.NewGuid();
        var google = Guid.NewGuid();
        await InsertUserAsync(owner, plain, "plain@example.com", "pbkdf2$1$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==");
        await InsertUserAsync(owner, google, "g@example.com", "pbkdf2$1$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==");
        await InsertIdentityAsync(owner, google, "sub-2");

        await RefusedAsync(
            () => DbTestSupport.ExecuteAsync(owner, "UPDATE users SET password_hash = NULL WHERE id = $1", plain),
            PostgresErrorCodes.CheckViolation, "ck_users_credential");
        Assert.Equal(1, await DbTestSupport.ExecuteAsync(owner, "UPDATE users SET password_hash = NULL WHERE id = $1", google));
    }

    [Fact]
    public async Task ChangingAUsersNameOrEmail_DoesNotTripTheCheck_EvenForAGoogleUser()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var google = Guid.NewGuid();
        await using (var transaction = await owner.BeginTransactionAsync())
        {
            await InsertUserAsync(owner, google, "g@example.com", null);
            await InsertIdentityAsync(owner, google, "sub-3");
            await transaction.CommitAsync();
        }

        Assert.Equal(1, await DbTestSupport.ExecuteAsync(owner, "UPDATE users SET full_name = 'New Name' WHERE id = $1", google));
    }

    // --- The identity table ------------------------------------------------------------------

    [Fact]
    public async Task OneGoogleAccount_CanBelongToOneUserOnly()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await InsertUserAsync(owner, first, "a@example.com", "pbkdf2$1$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==");
        await InsertUserAsync(owner, second, "b@example.com", "pbkdf2$1$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==");
        await InsertIdentityAsync(owner, first, "shared-subject");

        await RefusedAsync(
            () => InsertIdentityAsync(owner, second, "shared-subject"),
            PostgresErrorCodes.UniqueViolation, "uq_user_identities_provider_subject");
    }

    [Fact]
    public async Task AUser_HasAtMostOneIdentityPerProvider()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var user = Guid.NewGuid();
        await InsertUserAsync(owner, user, "a@example.com", "pbkdf2$1$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==");
        await InsertIdentityAsync(owner, user, "subject-1");

        await RefusedAsync(
            () => InsertIdentityAsync(owner, user, "subject-2"),
            PostgresErrorCodes.UniqueViolation, "uq_user_identities_user_provider");
    }

    [Theory]
    [InlineData("facebook")]
    [InlineData("Google")]
    [InlineData("")]
    public async Task OnlyKnownProvidersAreAccepted(string provider)
    {
        await using var owner = await _db.OpenOwnerAsync();
        var user = Guid.NewGuid();
        await InsertUserAsync(owner, user, "a@example.com", "pbkdf2$1$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==");

        await RefusedAsync(() => InsertIdentityAsync(owner, user, "s", provider), PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task DeletingAUser_RemovesTheirIdentity()
    {
        await using var owner = await _db.OpenOwnerAsync();
        var user = Guid.NewGuid();
        await using (var transaction = await owner.BeginTransactionAsync())
        {
            await InsertUserAsync(owner, user, "g@example.com", null);
            await InsertIdentityAsync(owner, user, "sub-del");
            await transaction.CommitAsync();
        }

        await DbTestSupport.ExecuteAsync(owner, "DELETE FROM users WHERE id = $1", user);

        Assert.Equal(0, await DbTestSupport.CountAsync(owner, "user_identities"));
    }

    // --- What the application role may do ----------------------------------------------------

    [Fact]
    public async Task TheApplicationRole_CanReadAndAddItsOwnIdentity_ButNeverChangeOrDeleteOne()
    {
        var user = Guid.NewGuid();
        var agency = Guid.NewGuid();
        await using (var app = await _db.OpenAppAsync(agency, user))
        {
            await using var transaction = await app.BeginTransactionAsync();
            await DbTestSupport.ExecuteAsync(app, "INSERT INTO users (id, email, password_hash) VALUES ($1, 'mine@example.com', NULL)", user);
            await DbTestSupport.ExecuteAsync(app, "INSERT INTO agencies (id, name, business_model) VALUES ($1, 'Mine', 'creator')", agency);
            await InsertIdentityAsync(app, user, "mine-subject");
            await transaction.CommitAsync();
        }

        await using var asUser = await _db.OpenAppAsync(agency, user);
        Assert.Equal(1, await DbTestSupport.CountAsync(asUser, "user_identities"));
        await RefusedAsync(
            () => DbTestSupport.ExecuteAsync(asUser, "UPDATE user_identities SET subject = 'stolen'"),
            PostgresErrorCodes.InsufficientPrivilege);
        await RefusedAsync(
            () => DbTestSupport.ExecuteAsync(asUser, "DELETE FROM user_identities"),
            PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task TheApplicationRole_CannotAddAnIdentityForAnotherUser_OrSeeAnotherUsersIdentity()
    {
        var other = Guid.NewGuid();
        await using (var owner = await _db.OpenOwnerAsync())
        {
            await InsertUserAsync(owner, other, "other@example.com", "pbkdf2$1$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==");
            await InsertIdentityAsync(owner, other, "other-subject");
        }

        var me = Guid.NewGuid();
        await using var app = await _db.OpenAppAsync(null, me);

        Assert.Equal(0, await DbTestSupport.CountAsync(app, "user_identities"));
        await RefusedAsync(() => InsertIdentityAsync(app, other, "planted"), PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task WithNoUserAtAll_TheApplicationRoleSeesNoIdentities_AndCannotAddOne()
    {
        await using (var owner = await _db.OpenOwnerAsync())
        {
            var user = Guid.NewGuid();
            await InsertUserAsync(owner, user, "someone@example.com", "pbkdf2$1$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==");
            await InsertIdentityAsync(owner, user, "some-subject");
        }

        await using var app = await _db.OpenAppAsync();

        Assert.Equal(0, await DbTestSupport.CountAsync(app, "user_identities"));
        await RefusedAsync(() => InsertIdentityAsync(app, Guid.NewGuid(), "x"), PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task RowLevelSecurity_StaysEnabledAndForced_OnTheIdentityTable()
    {
        await using var owner = await _db.OpenOwnerAsync();

        Assert.True(await owner.ExecuteScalarAsync<bool>(
            "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = 'user_identities'"));
    }

    // --- The lookup before any user is known -------------------------------------------------

    [Fact]
    public async Task TheLookup_IsNotOpenToPublic_ButIsToTheApplicationRole()
    {
        await using var owner = await _db.OpenOwnerAsync();

        Assert.False(await owner.ExecuteScalarAsync<bool>(
            "SELECT has_function_privilege('public', 'auth_find_user_by_identity(text, text)', 'EXECUTE')"));
        Assert.True(await owner.ExecuteScalarAsync<bool>(
            "SELECT has_function_privilege('app_user', 'auth_find_user_by_identity(text, text)', 'EXECUTE')"));
    }

    [Fact]
    public async Task TheLookup_ReturnsTheUserId_OfOneExactProviderAndSubject_AndNothingElse()
    {
        var user = Guid.NewGuid();
        await using (var owner = await _db.OpenOwnerAsync())
        {
            await using var transaction = await owner.BeginTransactionAsync();
            await InsertUserAsync(owner, user, "g@example.com", null);
            await InsertIdentityAsync(owner, user, "exact-subject");
            await transaction.CommitAsync();
        }

        await using var app = await _db.OpenAppAsync();

        const string lookup = "SELECT user_id FROM auth_find_user_by_identity('{0}', '{1}')";
        Assert.Equal([user], (await app.QueryAsync<Guid>(string.Format(lookup, "google", "exact-subject"))).ToList());
        Assert.Empty(await app.QueryAsync<Guid>(string.Format(lookup, "google", "exact")));
        Assert.Empty(await app.QueryAsync<Guid>(string.Format(lookup, "google", "%")));
        Assert.Empty(await app.QueryAsync<Guid>(string.Format(lookup, "facebook", "exact-subject")));
    }

    // --- Running it again, and as an owner that row-level security binds ---------------------

    [Fact]
    public async Task RunningTheMigrationsAgain_ChangesNothing_AndLeavesOneCheck()
    {
        await _db.MigrateAsync();
        await _db.MigrateAsync();

        await using var owner = await _db.OpenOwnerAsync();
        Assert.Equal(1, await DbTestSupport.CountAsync(owner, "pg_trigger WHERE tgname = 'ck_users_credential' AND NOT tgisinternal"));
        Assert.Equal(1, await DbTestSupport.CountAsync(owner, "pg_policies WHERE tablename = 'user_identities' AND policyname = 'own_select'"));
    }

    [Fact]
    public async Task ForAnOwnerThatRowLevelSecurityBinds_TheLookupStillFindsTheUser()
    {
        // A superuser skips every policy, which would hide a lookup that sees nothing. A limited owner is the proof.
        await using var limited = await postgres.CreateDatabaseWithLimitedOwnerAsync();
        var account = new NewAccount(
            Guid.NewGuid(), "maya@gmail.com", null, "Maya", Guid.NewGuid(), "Maya Makes Videos",
            BusinessModel.Creator, BusinessModel.Creator.FounderRole(), Guid.NewGuid(), "Main workspace",
            new ExternalIdentity("google", "limited-subject", "maya@gmail.com"));
        var repository = new AccountRepository(limited.CreateFactory());
        Assert.True(await repository.CreateAccountAsync(account, CancellationToken.None));

        var found = await repository.FindUserIdByIdentityAsync("google", "limited-subject", CancellationToken.None);

        Assert.Equal(account.UserId, found);
    }
}
