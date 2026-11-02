using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Services;
using Platform.Auth.Tests.Support;
using Platform.Helpers.Constants;

namespace Platform.Auth.Tests;

public sealed class PermissionServiceTests : IDisposable
{
    private readonly IPermissionRepository _repository = Substitute.For<IPermissionRepository>();
    private readonly FixedTimeProvider _time = FixedTimeProvider.Create();
    private readonly ServiceProvider _provider;
    private readonly PermissionService _service;

    public PermissionServiceTests()
    {
        // The repository is scoped in the application, so the service must open a scope for each load.
        _provider = new ServiceCollection().AddScoped(_ => _repository).BuildServiceProvider();
        _service = new PermissionService(_provider.GetRequiredService<IServiceScopeFactory>(), _time);
        _repository.LoadSnapshotAsync(Arg.Any<CancellationToken>()).Returns(_ => Snapshot(version: 4));
    }

    public void Dispose() => _provider.Dispose();

    private static PermissionSnapshot Snapshot(long version, params string[] companyAdminKeys) =>
        new(
            version,
            new Dictionary<(BusinessModel, OrganizationRole), IReadOnlyList<string>>
            {
                [(BusinessModel.Company, OrganizationRole.OrganizationAdmin)] =
                    companyAdminKeys.Length > 0 ? companyAdminKeys : [PermissionKeys.DashboardView, PermissionKeys.WorkspacesCreate],
                [(BusinessModel.Creator, OrganizationRole.ContentWriter)] = [PermissionKeys.DashboardView]
            });

    [Fact]
    public async Task GetGrants_ReturnsTheKeysOfTheSlot_AndTheVersionTheyWereReadUnder()
    {
        var grants = await _service.GetGrantsAsync(BusinessModel.Company, OrganizationRole.OrganizationAdmin, CancellationToken.None);

        Assert.Equal(4, grants.Version);
        Assert.Equal([PermissionKeys.DashboardView, PermissionKeys.WorkspacesCreate], grants.Keys);
    }

    [Fact]
    public async Task GetGrants_ForASlotWithNoGrants_ReturnsNoKeys_AndStillTheVersion()
    {
        var grants = await _service.GetGrantsAsync(BusinessModel.Agency, OrganizationRole.ClientManager, CancellationToken.None);

        Assert.Equal(4, grants.Version);
        Assert.Empty(grants.Keys);
    }

    [Fact]
    public async Task GetVersion_IsTheVersionOfTheSnapshot() =>
        Assert.Equal(4, await _service.GetVersionAsync(CancellationToken.None));

    [Fact]
    public async Task ManyCallsWithinTheLifetime_ReadTheDatabaseOnce()
    {
        for (var i = 0; i < 20; i++)
        {
            await _service.GetGrantsAsync(BusinessModel.Company, OrganizationRole.OrganizationAdmin, CancellationToken.None);
            await _service.GetVersionAsync(CancellationToken.None);
        }

        await _repository.Received(1).LoadSnapshotAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JustBeforeTheLifetimeEnds_NothingIsReadAgain_AndAtTheEnd_ItIs()
    {
        await _service.GetVersionAsync(CancellationToken.None);

        _time.Advance(PermissionService.CacheLifetime - TimeSpan.FromSeconds(1));
        await _service.GetVersionAsync(CancellationToken.None);
        await _repository.Received(1).LoadSnapshotAsync(Arg.Any<CancellationToken>());

        _time.Advance(TimeSpan.FromSeconds(1));
        await _service.GetVersionAsync(CancellationToken.None);
        await _repository.Received(2).LoadSnapshotAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void TheLifetime_IsThirtySeconds_TheBoundOnHowLateAnotherInstanceLearnsOfAChange() =>
        Assert.Equal(TimeSpan.FromSeconds(30), PermissionService.CacheLifetime);

    [Fact]
    public async Task AfterAnInvalidate_TheNextCallReadsAgain_AndSeesTheNewVersion()
    {
        Assert.Equal(4, await _service.GetVersionAsync(CancellationToken.None));
        _repository.LoadSnapshotAsync(Arg.Any<CancellationToken>()).Returns(_ => Snapshot(version: 5));

        _service.Invalidate();

        Assert.Equal(5, await _service.GetVersionAsync(CancellationToken.None));
        await _repository.Received(2).LoadSnapshotAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnInvalidateWithNothingHeldYet_IsHarmless()
    {
        _service.Invalidate();

        Assert.Equal(4, await _service.GetVersionAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ALoadThatBeganBeforeAnInvalidate_IsNeverServedAsCurrent()
    {
        // The race the generation guards: a load reads the old data, the change commits and Invalidate runs,
        // then the slow load finishes. Without the guard it would be stored as fresh and hide the change.
        var release = new TaskCompletionSource();
        var calls = 0;
        _repository.LoadSnapshotAsync(Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                await release.Task;
                return Snapshot(version: 4); // read before the change
            }

            return Snapshot(version: 5); // read after it
        });

        var slow = _service.GetVersionAsync(CancellationToken.None);
        await Task.Delay(50); // let the slow load start and wait
        _service.Invalidate(); // the change has committed
        release.SetResult();

        Assert.Equal(4, await slow); // the call that was already in flight gets what it read
        Assert.Equal(5, await _service.GetVersionAsync(CancellationToken.None)); // everyone after it sees the change
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task AFailedLoad_ReachesTheCaller_IsNotRemembered_AndTheNextCallTriesAgain()
    {
        _repository.LoadSnapshotAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("The database is down."));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetVersionAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetGrantsAsync(BusinessModel.Company, OrganizationRole.OrganizationAdmin, CancellationToken.None));

        _repository.LoadSnapshotAsync(Arg.Any<CancellationToken>()).Returns(_ => Snapshot(version: 6));

        Assert.Equal(6, await _service.GetVersionAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AFailedReload_DoesNotServeTheStaleSnapshotThatExpired()
    {
        await _service.GetVersionAsync(CancellationToken.None);
        _time.Advance(PermissionService.CacheLifetime);
        _repository.LoadSnapshotAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("The database is down."));

        // Failing closed: an answer that is no longer known to be current is not given.
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetVersionAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ManyCallsAtOnce_ShareOneLoad()
    {
        var release = new TaskCompletionSource();
        var calls = 0;
        _repository.LoadSnapshotAsync(Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            Interlocked.Increment(ref calls);
            await release.Task;
            return Snapshot(version: 4);
        });

        var all = Enumerable.Range(0, 25)
            .Select(_ => Task.Run(() => _service.GetVersionAsync(CancellationToken.None)))
            .ToArray();
        await Task.Delay(100);
        release.SetResult();

        Assert.All(await Task.WhenAll(all), version => Assert.Equal(4, version));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ACancelledCaller_DoesNotPoisonTheNextOne()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.GetVersionAsync(cancelled.Token));

        Assert.Equal(4, await _service.GetVersionAsync(CancellationToken.None));
    }
}
