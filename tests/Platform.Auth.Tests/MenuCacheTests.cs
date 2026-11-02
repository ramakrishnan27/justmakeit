using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Services;
using Platform.Auth.Tests.Support;

namespace Platform.Auth.Tests;

public sealed class MenuCacheTests : IDisposable
{
    private readonly IMenuRepository _repository = Substitute.For<IMenuRepository>();
    private readonly FixedTimeProvider _time = FixedTimeProvider.Create();
    private readonly ServiceProvider _provider;
    private readonly MenuCache _cache;

    public MenuCacheTests()
    {
        // The repository is scoped in the application, so every load must open a scope of its own.
        _provider = new ServiceCollection().AddScoped(_ => _repository).BuildServiceProvider();
        _cache = new MenuCache(_provider.GetRequiredService<IServiceScopeFactory>(), _time);
        _repository.LoadSnapshotAsync(Arg.Any<CancellationToken>()).Returns(_ => SnapshotOf("First"));
    }

    public void Dispose() => _provider.Dispose();

    private static MenuSnapshot SnapshotOf(string label) =>
        new([new MenuItemRecord { MenuKey = MenuKeys.Member, Id = Guid.NewGuid(), PageKey = "dashboard", Label = label }]);

    [Fact]
    public async Task ManyReadsWithinTheLifetime_ReadTheDatabaseOnce()
    {
        for (var i = 0; i < 20; i++)
        {
            await _cache.GetAsync(CancellationToken.None);
        }

        await _repository.Received(1).LoadSnapshotAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void TheLifetime_IsSixtySeconds() => Assert.Equal(TimeSpan.FromSeconds(60), MenuCache.CacheLifetime);

    [Fact]
    public async Task AtTheEndOfTheLifetime_TheMenuIsReadAgain_AndNotBefore()
    {
        await _cache.GetAsync(CancellationToken.None);

        _time.Advance(MenuCache.CacheLifetime - TimeSpan.FromSeconds(1));
        await _cache.GetAsync(CancellationToken.None);
        await _repository.Received(1).LoadSnapshotAsync(Arg.Any<CancellationToken>());

        _time.Advance(TimeSpan.FromSeconds(1));
        await _cache.GetAsync(CancellationToken.None);
        await _repository.Received(2).LoadSnapshotAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AfterAnInvalidate_TheNextReadSeesTheEdit()
    {
        Assert.Equal("First", (await _cache.GetAsync(CancellationToken.None)).Items[0].Label);
        _repository.LoadSnapshotAsync(Arg.Any<CancellationToken>()).Returns(_ => SnapshotOf("Renamed"));

        _cache.Invalidate();

        Assert.Equal("Renamed", (await _cache.GetAsync(CancellationToken.None)).Items[0].Label);
    }

    [Fact]
    public async Task WithoutAnInvalidate_AnEditIsNotSeenUntilTheLifetimeEnds()
    {
        await _cache.GetAsync(CancellationToken.None);
        _repository.LoadSnapshotAsync(Arg.Any<CancellationToken>()).Returns(_ => SnapshotOf("Renamed"));

        Assert.Equal("First", (await _cache.GetAsync(CancellationToken.None)).Items[0].Label);

        _time.Advance(MenuCache.CacheLifetime);
        Assert.Equal("Renamed", (await _cache.GetAsync(CancellationToken.None)).Items[0].Label);
    }

    [Fact]
    public async Task AFailedRead_ReachesTheCaller_AndIsNotRemembered()
    {
        _repository.LoadSnapshotAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("down"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _cache.GetAsync(CancellationToken.None));

        _repository.LoadSnapshotAsync(Arg.Any<CancellationToken>()).Returns(_ => SnapshotOf("Back"));

        Assert.Equal("Back", (await _cache.GetAsync(CancellationToken.None)).Items[0].Label);
    }
}
