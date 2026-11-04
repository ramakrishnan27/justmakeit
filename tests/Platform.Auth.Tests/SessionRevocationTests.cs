using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Services;
using Platform.Auth.Tests.Support;

namespace Platform.Auth.Tests;

public sealed class SessionRevocationTests : IDisposable
{
    private static readonly Guid Agency = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    // 2026-10-31T12:00:00Z as seconds since 1970.
    private static readonly DateTimeOffset RevokedAt = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly long RevokedSecond = RevokedAt.ToUnixTimeSeconds();

    private readonly ISessionRevocationRepository _repository = Substitute.For<ISessionRevocationRepository>();
    private readonly FixedTimeProvider _time = FixedTimeProvider.Create();
    private readonly ServiceProvider _provider;
    private readonly SessionRevocationService _service;

    public SessionRevocationTests()
    {
        _provider = new ServiceCollection().AddScoped(_ => _repository).BuildServiceProvider();
        _service = new SessionRevocationService(_provider.GetRequiredService<IServiceScopeFactory>(), _time);
        _repository.LoadSnapshotAsync(Arg.Any<CancellationToken>()).Returns(_ => Snapshot());
    }

    public void Dispose() => _provider.Dispose();

    private static SessionRevocationSnapshot Snapshot(DateTimeOffset? validAfter = null) =>
        SessionRevocationSnapshot.From(
            [new SessionRevocationRecord { AgencyId = Agency, UserId = User, ValidAfter = validAfter ?? RevokedAt }]);

    // --- The rule ----------------------------------------------------------------------------

    [Theory]
    [InlineData(-3600, true)]  // issued long before
    [InlineData(-1, true)]
    [InlineData(0, true)]      // in the same second: refused, the person simply signs in again
    [InlineData(1, false)]     // issued after
    [InlineData(3600, false)]
    public void ATokenIsVoid_WhenItWasIssuedAtOrBeforeTheEndedSecond(long secondsFromRevocation, bool revoked) =>
        Assert.Equal(revoked, Snapshot().IsRevoked(Agency, User, RevokedSecond + secondsFromRevocation));

    [Fact]
    public void ATokenIssuedLaterInTheSameSecondAsAFractionalRevocation_IsStillRefused()
    {
        // 12:00:00.900: the whole second is what a token's iat can say, so the rule rounds toward refusing.
        var snapshot = Snapshot(RevokedAt.AddMilliseconds(900));

        Assert.True(snapshot.IsRevoked(Agency, User, RevokedSecond));
        Assert.False(snapshot.IsRevoked(Agency, User, RevokedSecond + 1));
    }

    [Fact]
    public void ItConcernsOnlyThatPersonInThatOrganization()
    {
        var snapshot = Snapshot();

        Assert.False(snapshot.IsRevoked(Agency, Guid.NewGuid(), RevokedSecond - 100)); // someone else
        Assert.False(snapshot.IsRevoked(Guid.NewGuid(), User, RevokedSecond - 100)); // the same person elsewhere
    }

    [Fact]
    public void NothingIsRevoked_WhenNothingWasEnded() =>
        Assert.False(SessionRevocationSnapshot.Empty.IsRevoked(Agency, User, 0));

    // --- The service holds it in memory ------------------------------------------------------

    [Fact]
    public async Task ManyChecksWithinTheLifetime_ReadTheDatabaseOnce()
    {
        for (var i = 0; i < 25; i++)
        {
            await _service.IsRevokedAsync(Agency, User, RevokedSecond - 1, CancellationToken.None);
        }

        await _repository.Received(1).LoadSnapshotAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAnswerComesFromTheSnapshot()
    {
        Assert.True(await _service.IsRevokedAsync(Agency, User, RevokedSecond - 1, CancellationToken.None));
        Assert.False(await _service.IsRevokedAsync(Agency, User, RevokedSecond + 1, CancellationToken.None));
    }

    [Fact]
    public async Task AfterTheLifetime_ItIsReadAgain()
    {
        await _service.IsRevokedAsync(Agency, User, 0, CancellationToken.None);

        _time.Advance(SessionRevocationService.CacheLifetime + TimeSpan.FromSeconds(1));
        await _service.IsRevokedAsync(Agency, User, 0, CancellationToken.None);

        await _repository.Received(2).LoadSnapshotAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invalidate_MakesTheNextCheckSeeAChangeMadeJustNow()
    {
        var token = RevokedSecond + 5;
        var changed = false;
        _repository.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(_ => changed ? Snapshot(RevokedAt.AddSeconds(10)) : SessionRevocationSnapshot.Empty);

        Assert.False(await _service.IsRevokedAsync(Agency, User, token, CancellationToken.None));

        changed = true; // what removing the member does in the database ...
        _service.Invalidate(); // ... followed by what the team service does

        Assert.True(await _service.IsRevokedAsync(Agency, User, token, CancellationToken.None));
    }

    [Fact]
    public async Task WithoutInvalidate_AChangeIsSeenOnlyAfterTheLifetime()
    {
        var token = RevokedSecond + 5;
        var changed = false;
        _repository.LoadSnapshotAsync(Arg.Any<CancellationToken>())
            .Returns(_ => changed ? Snapshot(RevokedAt.AddSeconds(10)) : SessionRevocationSnapshot.Empty);
        await _service.IsRevokedAsync(Agency, User, token, CancellationToken.None);

        changed = true;
        Assert.False(await _service.IsRevokedAsync(Agency, User, token, CancellationToken.None));

        _time.Advance(SessionRevocationService.CacheLifetime + TimeSpan.FromSeconds(1));
        Assert.True(await _service.IsRevokedAsync(Agency, User, token, CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheListCannotBeRead_TheFailureReachesTheCaller_NotAnAnswerOfNo()
    {
        _repository.LoadSnapshotAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("The database is not reachable."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.IsRevokedAsync(Agency, User, 0, CancellationToken.None));
    }

    [Fact]
    public void TheCacheLifetime_IsThirtySeconds() => Assert.Equal(TimeSpan.FromSeconds(30), SessionRevocationService.CacheLifetime);
}
