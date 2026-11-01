using Dapper;
using Npgsql;
using Platform.Auth.Models;
using Platform.Auth.Repositories;
using Platform.Infrastructure.Data;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.SiteAdmin;

/// <summary>Site admin refresh tokens as app_user: lookup before anyone is known, rotation, revocation, isolation.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class SiteAdminRefreshTokenRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private TestDatabase _db = null!;
    private SiteAdminRefreshTokenRepository _repository = null!;
    private Guid _admin, _otherAdmin, _ordinaryUser;

    public async Task InitializeAsync()
    {
        DapperConfiguration.Configure();
        _db = await postgres.CreateDatabaseAsync();
        _repository = new SiteAdminRefreshTokenRepository(_db.CreateFactory());

        await using var owner = await _db.OpenOwnerAsync();
        _admin = await DbTestSupport.InsertUserAsync(owner, "root@example.com");
        _otherAdmin = await DbTestSupport.InsertUserAsync(owner, "root2@example.com");
        _ordinaryUser = await DbTestSupport.InsertUserAsync(owner, "ordinary@example.com");
        await DbTestSupport.InsertSiteAdminAsync(owner, _admin);
        await DbTestSupport.InsertSiteAdminAsync(owner, _otherAdmin);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static NewSiteRefreshToken Token(string hash, Guid userId) => new(Guid.NewGuid(), userId, hash, Noon.AddDays(30));

    private async Task<NewSiteRefreshToken> StoreAsync(string hash, Guid userId)
    {
        var token = Token(hash, userId);
        await _repository.InsertAsync(token, CancellationToken.None);
        return token;
    }

    private async Task<(DateTimeOffset? RevokedAt, Guid? ReplacedById)> StateAsync(Guid tokenId)
    {
        await using var owner = await _db.OpenOwnerAsync();
        return await owner.QuerySingleAsync<(DateTimeOffset? RevokedAt, Guid? ReplacedById)>(
            "SELECT revoked_at, replaced_by_id FROM site_admin_refresh_tokens WHERE id = @tokenId", new { tokenId });
    }

    private async Task<long> CountAsync()
    {
        await using var owner = await _db.OpenOwnerAsync();
        return await DbTestSupport.CountAsync(owner, "site_admin_refresh_tokens");
    }

    // --- Insert and lookup -------------------------------------------------------------------

    [Fact]
    public async Task FindByHash_ReturnsTheTokenAndWhoseItIs_WithoutAnyUserOrAgency()
    {
        var token = await StoreAsync("hash-1", _admin);

        var found = await _repository.FindByHashAsync("hash-1", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(token.Id, found.Id);
        Assert.Equal(_admin, found.UserId);
        Assert.Equal(token.ExpiresAt, found.ExpiresAt);
        Assert.Null(found.RevokedAt);
        Assert.Null(found.ReplacedById);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("hash-%")]
    [InlineData("HASH-1")]
    [InlineData("hash-1' OR '1'='1")]
    public async Task FindByHash_MatchesOnlyTheExactHash(string hash)
    {
        await StoreAsync("hash-1", _admin);

        Assert.Null(await _repository.FindByHashAsync(hash, CancellationToken.None));
    }

    [Fact]
    public async Task WithoutTheLookupFunction_AppUserCannotSeeAnyToken()
    {
        await StoreAsync("hash-1", _admin);

        await using var anonymous = await _db.OpenAppAsync();
        Assert.Equal(0, await DbTestSupport.CountAsync(anonymous, "site_admin_refresh_tokens"));
    }

    [Fact]
    public async Task AnOrdinaryUser_CannotStoreASiteAdminToken_EvenForThemselves()
    {
        // The policy lets only a site admin write a token, so a stray row can never become a session.
        var attempt = await Assert.ThrowsAsync<PostgresException>(
            () => _repository.InsertAsync(Token("sneaky", _ordinaryUser), CancellationToken.None));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, attempt.SqlState);
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task ASiteAdmin_CannotStoreATokenForAnotherUser()
    {
        await using var asAdmin = await _db.OpenAppAsync(null, _admin);

        var attempt = await Assert.ThrowsAsync<PostgresException>(() => asAdmin.ExecuteAsync(
            "INSERT INTO site_admin_refresh_tokens (user_id, token_hash, expires_at) VALUES (@userId, 'stolen', now() + interval '1 day')",
            new { userId = _otherAdmin }));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, attempt.SqlState);
    }

    [Fact]
    public async Task EachSiteAdmin_SeesOnlyTheirOwnTokens_WithPlainSql()
    {
        await StoreAsync("mine-1", _admin);
        await StoreAsync("mine-2", _admin);
        await StoreAsync("theirs", _otherAdmin);

        await using var asAdmin = await _db.OpenAppAsync(null, _admin);
        await using var asOther = await _db.OpenAppAsync(null, _otherAdmin);

        Assert.Equal(2, await DbTestSupport.CountAsync(asAdmin, "site_admin_refresh_tokens"));
        Assert.Equal(1, await DbTestSupport.CountAsync(asOther, "site_admin_refresh_tokens"));
    }

    // --- Rotate ------------------------------------------------------------------------------

    [Fact]
    public async Task Rotate_RevokesTheOldToken_PointsItAtTheNewOne_AndStoresTheNewOne()
    {
        var old = await StoreAsync("old", _admin);
        var replacement = Token("new", _admin);

        Assert.True(await _repository.RotateAsync(old.Id, replacement, Noon, CancellationToken.None));

        Assert.Equal((Noon, replacement.Id), await StateAsync(old.Id));
        Assert.Equal((null, null), await StateAsync(replacement.Id));
        Assert.Equal(replacement.Id, (await _repository.FindByHashAsync("new", CancellationToken.None))!.Id);
        Assert.Equal(2, await CountAsync());
    }

    [Fact]
    public async Task Rotate_ASecondTimeForTheSameToken_ReturnsFalse_AndStoresNothing()
    {
        var old = await StoreAsync("old", _admin);
        await _repository.RotateAsync(old.Id, Token("first", _admin), Noon, CancellationToken.None);

        Assert.False(await _repository.RotateAsync(old.Id, Token("second", _admin), Noon.AddMinutes(1), CancellationToken.None));

        Assert.Null(await _repository.FindByHashAsync("second", CancellationToken.None));
        Assert.Equal(2, await CountAsync());
        Assert.Equal(Noon, (await StateAsync(old.Id)).RevokedAt);
    }

    [Fact]
    public async Task Rotate_WhenManyRequestsPresentTheSameToken_ExactlyOneSucceeds()
    {
        var old = await StoreAsync("old", _admin);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            Task.Run(() => _repository.RotateAsync(old.Id, Token($"new-{i}", _admin), Noon, CancellationToken.None))));

        Assert.Equal(1, results.Count(rotated => rotated));
        Assert.Equal(2, await CountAsync());
    }

    [Fact]
    public async Task Rotate_CannotRevokeAnotherSiteAdminsToken()
    {
        var victim = await StoreAsync("victim", _otherAdmin);

        var rotated = await _repository.RotateAsync(victim.Id, Token("mine", _admin), Noon, CancellationToken.None);

        Assert.False(rotated);
        Assert.Null((await StateAsync(victim.Id)).RevokedAt);
        Assert.Null(await _repository.FindByHashAsync("mine", CancellationToken.None));
    }

    // --- Revoke ------------------------------------------------------------------------------

    [Fact]
    public async Task Revoke_EndsOnlyThatToken_AndKeepsItsOriginalTimeOnARepeat()
    {
        var target = await StoreAsync("target", _admin);
        var sibling = await StoreAsync("sibling", _admin);

        await _repository.RevokeAsync(_admin, target.Id, Noon, CancellationToken.None);
        await _repository.RevokeAsync(_admin, target.Id, Noon.AddHours(5), CancellationToken.None);

        Assert.Equal(Noon, (await StateAsync(target.Id)).RevokedAt);
        Assert.Null((await StateAsync(sibling.Id)).RevokedAt);
    }

    [Fact]
    public async Task RevokeAll_EndsEveryLiveTokenOfThatUser_AndNothingElse()
    {
        var a1 = await StoreAsync("a1", _admin);
        var a2 = await StoreAsync("a2", _admin);
        var alreadyRevoked = await StoreAsync("old", _admin);
        await _repository.RevokeAsync(_admin, alreadyRevoked.Id, Noon.AddDays(-1), CancellationToken.None);
        var others = await StoreAsync("other", _otherAdmin);

        var revoked = await _repository.RevokeAllAsync(_admin, Noon, CancellationToken.None);

        Assert.Equal(2, revoked);
        Assert.Equal(Noon, (await StateAsync(a1.Id)).RevokedAt);
        Assert.Equal(Noon, (await StateAsync(a2.Id)).RevokedAt);
        Assert.Equal(Noon.AddDays(-1), (await StateAsync(alreadyRevoked.Id)).RevokedAt);
        Assert.Null((await StateAsync(others.Id)).RevokedAt);
        Assert.Equal(0, await _repository.RevokeAllAsync(_admin, Noon, CancellationToken.None));
    }

    [Fact]
    public async Task Revoking_AsAnotherUser_LeavesTheTokenAlone()
    {
        var token = await StoreAsync("token", _otherAdmin);

        await _repository.RevokeAsync(_admin, token.Id, Noon, CancellationToken.None);

        Assert.Null((await StateAsync(token.Id)).RevokedAt);
    }

    [Fact]
    public async Task ATokenOutlivesTheRole_ButCannotBeRenewedByANewInsert_AfterTheUserIsDemoted()
    {
        await StoreAsync("before", _admin);
        await using (var owner = await _db.OpenOwnerAsync())
        {
            await DbTestSupport.ExecuteAsync(owner, "DELETE FROM site_admins WHERE user_id = $1", _admin);
        }

        // The old row is still there to be revoked, but nothing new can be written for a demoted user.
        await Assert.ThrowsAsync<PostgresException>(() => _repository.InsertAsync(Token("after", _admin), CancellationToken.None));
        Assert.Equal(1, await _repository.RevokeAllAsync(_admin, Noon, CancellationToken.None));
    }
}
