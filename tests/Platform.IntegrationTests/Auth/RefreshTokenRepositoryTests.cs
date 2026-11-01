using Dapper;
using Platform.Auth.Models;
using Platform.Auth.Repositories;
using Platform.Infrastructure.Data;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Auth;

/// <summary>Refresh token queries as app_user: lookup before a tenant is known, rotation, revocation and isolation.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class RefreshTokenRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private TestDatabase _db = null!;
    private RefreshTokenRepository _repository = null!;
    private Guid _agencyA, _agencyB;
    private Guid _userA, _userB;

    public async Task InitializeAsync()
    {
        DapperConfiguration.Configure();
        _db = await postgres.CreateDatabaseAsync();
        _repository = new RefreshTokenRepository(_db.CreateFactory());

        await using var owner = await _db.OpenOwnerAsync();
        _agencyA = await DbTestSupport.InsertAgencyAsync(owner, "Agency A");
        _agencyB = await DbTestSupport.InsertAgencyAsync(owner, "Agency B");
        _userA = await DbTestSupport.InsertUserAsync(owner, "a@example.com");
        _userB = await DbTestSupport.InsertUserAsync(owner, "b@example.com");
        await DbTestSupport.InsertMemberAsync(owner, _agencyA, _userA, "organizationadmin");
        await DbTestSupport.InsertMemberAsync(owner, _agencyB, _userB, "organizationadmin");
        // User A also works in agency B, so one user can hold tokens for two agencies.
        await DbTestSupport.InsertMemberAsync(owner, _agencyB, _userA, "clientmanager");
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private NewRefreshToken Token(string hash, Guid agencyId, Guid userId, DateTimeOffset? expiresAt = null) =>
        new(Guid.NewGuid(), agencyId, userId, hash, expiresAt ?? Noon.AddDays(30));

    private async Task<NewRefreshToken> StoreAsync(string hash, Guid agencyId, Guid userId)
    {
        var token = Token(hash, agencyId, userId);
        await _repository.InsertAsync(token, CancellationToken.None);
        return token;
    }

    private async Task<(DateTimeOffset? RevokedAt, Guid? ReplacedById)> StateAsync(Guid tokenId)
    {
        await using var owner = await _db.OpenOwnerAsync();
        return await owner.QuerySingleAsync<(DateTimeOffset? RevokedAt, Guid? ReplacedById)>(
            "SELECT revoked_at, replaced_by_id FROM refresh_tokens WHERE id = @tokenId", new { tokenId });
    }

    private async Task<long> CountAsync()
    {
        await using var owner = await _db.OpenOwnerAsync();
        return await DbTestSupport.CountAsync(owner, "refresh_tokens");
    }

    // --- InsertAsync and FindByHashAsync -----------------------------------------------------

    [Fact]
    public async Task FindByHash_ReturnsTheTokenAndWhoseItIs_WithoutAnyTenant()
    {
        var token = await StoreAsync("hash-1", _agencyA, _userA);

        var found = await _repository.FindByHashAsync("hash-1", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(token.Id, found.Id);
        Assert.Equal(_userA, found.UserId);
        Assert.Equal(_agencyA, found.AgencyId);
        Assert.Equal(token.ExpiresAt, found.ExpiresAt);
        Assert.Null(found.RevokedAt);
        Assert.Null(found.ReplacedById);
    }

    [Fact]
    public async Task FindByHash_FindsATokenOfAnyAgency_ByItsExactHash()
    {
        var inA = await StoreAsync("hash-a", _agencyA, _userA);
        var inB = await StoreAsync("hash-b", _agencyB, _userB);

        Assert.Equal(inA.Id, (await _repository.FindByHashAsync("hash-a", CancellationToken.None))!.Id);
        Assert.Equal(inB.Id, (await _repository.FindByHashAsync("hash-b", CancellationToken.None))!.Id);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("hash-%")]
    [InlineData("HASH-1")]
    [InlineData("hash-1' OR '1'='1")]
    public async Task FindByHash_MatchesOnlyTheExactHash(string hash)
    {
        await StoreAsync("hash-1", _agencyA, _userA);

        Assert.Null(await _repository.FindByHashAsync(hash, CancellationToken.None));
    }

    [Fact]
    public async Task FindByHash_ShowsARevokedTokenAsRevoked()
    {
        var token = await StoreAsync("hash-1", _agencyA, _userA);
        await _repository.RevokeAsync(_agencyA, _userA, token.Id, Noon, CancellationToken.None);

        var found = await _repository.FindByHashAsync("hash-1", CancellationToken.None);

        Assert.Equal(Noon, found!.RevokedAt);
    }

    [Fact]
    public async Task WithoutTheLookupFunction_AppUserCannotSeeAnyToken()
    {
        await StoreAsync("hash-1", _agencyA, _userA);

        await using var noTenant = await _db.OpenAppAsync();
        Assert.Equal(0, await DbTestSupport.CountAsync(noTenant, "refresh_tokens"));
    }

    // --- RotateAsync -------------------------------------------------------------------------

    [Fact]
    public async Task Rotate_RevokesTheOldToken_PointsItAtTheNewOne_AndStoresTheNewOne()
    {
        var old = await StoreAsync("old", _agencyA, _userA);
        var replacement = Token("new", _agencyA, _userA);

        Assert.True(await _repository.RotateAsync(old.Id, replacement, Noon, CancellationToken.None));

        Assert.Equal((Noon, replacement.Id), await StateAsync(old.Id));
        Assert.Equal((null, null), await StateAsync(replacement.Id));
        Assert.Equal(replacement.Id, (await _repository.FindByHashAsync("new", CancellationToken.None))!.Id);
        Assert.Equal(2, await CountAsync());
    }

    [Fact]
    public async Task Rotate_ASecondTimeForTheSameToken_ReturnsFalse_AndStoresNothing()
    {
        var old = await StoreAsync("old", _agencyA, _userA);
        await _repository.RotateAsync(old.Id, Token("first", _agencyA, _userA), Noon, CancellationToken.None);

        var second = Token("second", _agencyA, _userA);
        Assert.False(await _repository.RotateAsync(old.Id, second, Noon.AddMinutes(1), CancellationToken.None));

        Assert.Null(await _repository.FindByHashAsync("second", CancellationToken.None));
        Assert.Equal(2, await CountAsync());
        Assert.Equal(Noon, (await StateAsync(old.Id)).RevokedAt);
    }

    [Fact]
    public async Task Rotate_WhenManyRequestsPresentTheSameToken_ExactlyOneSucceeds()
    {
        var old = await StoreAsync("old", _agencyA, _userA);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            Task.Run(() => _repository.RotateAsync(old.Id, Token($"new-{i}", _agencyA, _userA), Noon, CancellationToken.None))));

        Assert.Equal(1, results.Count(rotated => rotated));
        Assert.Equal(2, await CountAsync());
    }

    [Fact]
    public async Task Rotate_CannotRevokeAnotherUsersToken()
    {
        var victim = await StoreAsync("victim", _agencyB, _userB);

        // The replacement claims user A in agency B; the old token belongs to user B.
        var rotated = await _repository.RotateAsync(victim.Id, Token("mine", _agencyB, _userA), Noon, CancellationToken.None);

        Assert.False(rotated);
        Assert.Null((await StateAsync(victim.Id)).RevokedAt);
        Assert.Null(await _repository.FindByHashAsync("mine", CancellationToken.None));
    }

    [Fact]
    public async Task Rotate_CannotRevokeATokenOfAnotherAgency()
    {
        var other = await StoreAsync("other-agency", _agencyB, _userA);

        var rotated = await _repository.RotateAsync(other.Id, Token("mine", _agencyA, _userA), Noon, CancellationToken.None);

        Assert.False(rotated);
        Assert.Null((await StateAsync(other.Id)).RevokedAt);
    }

    // --- RevokeAsync and RevokeAllAsync ------------------------------------------------------

    [Fact]
    public async Task Revoke_EndsOnlyThatToken()
    {
        var target = await StoreAsync("target", _agencyA, _userA);
        var sibling = await StoreAsync("sibling", _agencyA, _userA);

        await _repository.RevokeAsync(_agencyA, _userA, target.Id, Noon, CancellationToken.None);

        Assert.Equal(Noon, (await StateAsync(target.Id)).RevokedAt);
        Assert.Null((await StateAsync(sibling.Id)).RevokedAt);
    }

    [Fact]
    public async Task Revoke_ATokenAlreadyRevoked_KeepsItsOriginalTime()
    {
        var token = await StoreAsync("token", _agencyA, _userA);
        await _repository.RevokeAsync(_agencyA, _userA, token.Id, Noon, CancellationToken.None);

        await _repository.RevokeAsync(_agencyA, _userA, token.Id, Noon.AddHours(5), CancellationToken.None);

        Assert.Equal(Noon, (await StateAsync(token.Id)).RevokedAt);
    }

    [Fact]
    public async Task RevokeAll_EndsEveryLiveTokenOfThatUserInThatAgency_AndNothingElse()
    {
        var a1 = await StoreAsync("a1", _agencyA, _userA);
        var a2 = await StoreAsync("a2", _agencyA, _userA);
        var userAInB = await StoreAsync("a-in-b", _agencyB, _userA);
        var userBInB = await StoreAsync("b-in-b", _agencyB, _userB);
        var alreadyRevoked = await StoreAsync("old", _agencyA, _userA);
        await _repository.RevokeAsync(_agencyA, _userA, alreadyRevoked.Id, Noon.AddDays(-1), CancellationToken.None);

        var revoked = await _repository.RevokeAllAsync(_agencyA, _userA, Noon, CancellationToken.None);

        Assert.Equal(2, revoked);
        Assert.Equal(Noon, (await StateAsync(a1.Id)).RevokedAt);
        Assert.Equal(Noon, (await StateAsync(a2.Id)).RevokedAt);
        Assert.Equal(Noon.AddDays(-1), (await StateAsync(alreadyRevoked.Id)).RevokedAt);
        Assert.Null((await StateAsync(userAInB.Id)).RevokedAt);
        Assert.Null((await StateAsync(userBInB.Id)).RevokedAt);
    }

    [Fact]
    public async Task RevokeAll_WhenThereIsNothingToRevoke_ReturnsZero()
    {
        Assert.Equal(0, await _repository.RevokeAllAsync(_agencyA, _userA, Noon, CancellationToken.None));
    }

    [Fact]
    public async Task Revoking_WithAnotherUsersOrAgencysContext_LeavesTheTokenAlone()
    {
        var bs = await StoreAsync("bs-token", _agencyB, _userB);

        // User A has a membership in agency B, yet B's token is B's: the policy needs agency AND user to match.
        await _repository.RevokeAsync(_agencyB, _userA, bs.Id, Noon, CancellationToken.None);
        Assert.Equal(0, await _repository.RevokeAllAsync(_agencyB, _userA, Noon, CancellationToken.None));
        await _repository.RevokeAsync(_agencyA, _userB, bs.Id, Noon, CancellationToken.None);

        Assert.Null((await StateAsync(bs.Id)).RevokedAt);
    }

    [Fact]
    public async Task EachTenant_SeesOnlyItsOwnTokensWithPlainSql()
    {
        await StoreAsync("a-in-a", _agencyA, _userA);
        await StoreAsync("a-in-b", _agencyB, _userA);
        await StoreAsync("b-in-b", _agencyB, _userB);

        await using var asAInA = await _db.OpenAppAsync(_agencyA, _userA);
        await using var asBInB = await _db.OpenAppAsync(_agencyB, _userB);
        await using var asAInB = await _db.OpenAppAsync(_agencyB, _userA);

        Assert.Equal(1, await DbTestSupport.CountAsync(asAInA, "refresh_tokens"));
        Assert.Equal(1, await DbTestSupport.CountAsync(asBInB, "refresh_tokens"));
        Assert.Equal(1, await DbTestSupport.CountAsync(asAInB, "refresh_tokens"));
    }
}
