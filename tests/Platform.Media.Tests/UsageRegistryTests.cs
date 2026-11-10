using NSubstitute;
using Platform.Media.Interfaces;
using Platform.Media.Services;

namespace Platform.Media.Tests;

public sealed class UsageRegistryTests
{
    private static readonly Guid Agency = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Entity = Guid.NewGuid();

    private readonly IMediaUsageRepository _usages = Substitute.For<IMediaUsageRepository>();
    private readonly MediaAssetUsageRegistry _registry;

    public UsageRegistryTests() => _registry = new MediaAssetUsageRegistry(_usages);

    [Fact]
    public async Task Replace_HandsTheRepositoryTheDistinctFiles()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await _registry.ReplaceAsync(Agency, User, "content_studio", "post", Entity, [a, b, a], CancellationToken.None);

        await _usages.Received(1).ReplaceAsync(
            Agency, User, "content_studio", "post", Entity, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(a) && ids.Contains(b)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Replace_WithNoFiles_ClearsTheUsagesOfTheThing()
    {
        await _registry.ReplaceAsync(Agency, User, "content_studio", "post", Entity, [], CancellationToken.None);

        await _usages.Received(1).ReplaceAsync(
            Agency, User, "content_studio", "post", Entity, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAll_IsAReplaceWithNothing()
    {
        await _registry.RemoveAllAsync(Agency, User, "content_studio", "post", Entity, CancellationToken.None);

        await _usages.Received(1).ReplaceAsync(
            Agency, User, "content_studio", "post", Entity, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("content_studio", true)]
    [InlineData("a", true)]
    [InlineData("module2", true)]
    [InlineData("Content", false)]
    [InlineData("content-studio", false)]
    [InlineData("1module", false)]
    [InlineData("_module", false)]
    [InlineData("with space", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ANameIsOneTheTableAccepts(string? name, bool valid) => Assert.Equal(valid, MediaAssetUsageRegistry.IsValidName(name));

    [Fact]
    public void ANameCannotBeLongerThanFiftyCharacters()
    {
        Assert.True(MediaAssetUsageRegistry.IsValidName("a" + new string('b', 49)));
        Assert.False(MediaAssetUsageRegistry.IsValidName("a" + new string('b', 50)));
    }

    [Theory]
    [InlineData("Bad", "post")]
    [InlineData("content_studio", "bad-type")]
    [InlineData("", "post")]
    public async Task ABadName_IsAMistakeOfTheCaller_AndNeverReachesTheDatabase(string module, string type)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _registry.ReplaceAsync(Agency, User, module, type, Entity, [Guid.NewGuid()], CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => _registry.RemoveAllAsync(Agency, User, module, type, Entity, CancellationToken.None));

        await _usages.DidNotReceiveWithAnyArgs().ReplaceAsync(default, default, default!, default!, default, default!, default);
    }

    [Fact]
    public async Task AnEmptyIdentity_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _registry.ReplaceAsync(Guid.Empty, User, "content_studio", "post", Entity, [], CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => _registry.ReplaceAsync(Agency, Guid.Empty, "content_studio", "post", Entity, [], CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => _registry.ReplaceAsync(Agency, User, "content_studio", "post", Guid.Empty, [], CancellationToken.None));
    }

    [Fact]
    public async Task AbsurdlyManyFiles_AreRefusedBeforeTheDatabase()
    {
        var many = Enumerable.Range(0, MediaAssetUsageRegistry.MaxAssetsPerEntity + 1).Select(_ => Guid.NewGuid()).ToArray();

        await Assert.ThrowsAsync<ArgumentException>(() => _registry.ReplaceAsync(Agency, User, "content_studio", "post", Entity, many, CancellationToken.None));
        await _usages.DidNotReceiveWithAnyArgs().ReplaceAsync(default, default, default!, default!, default, default!, default);
    }
}
