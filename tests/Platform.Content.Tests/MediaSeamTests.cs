using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Platform.Content.Features.Media;
using Platform.Content.Interfaces;
using Platform.Content.Tests.Support;
using Platform.Helpers.Media;

namespace Platform.Content.Tests;

/// <summary>
/// Content Studio reaches the Media Library through the contract in Core, because it may not reference Platform.Media. The seam is a
/// real library when a module registered the contract, and "there is none" when nobody did, whichever module was added first.
/// </summary>
public sealed class MediaSeamTests
{
    private static readonly Guid Asset = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static MediaAssetSummary Summary(Guid id) => new(id, TestData.WorkspaceId, "photo.png", "image", "image/png", 1234, 640, 480);

    [Fact]
    public async Task WithNoLibrary_NothingIsAvailable_AndNoFileIsFound()
    {
        IContentMediaLibrary library = new NoMediaLibrary();

        Assert.False(library.IsAvailable);
        Assert.Empty(await library.FindAsync(TestData.AgencyId, TestData.UserId, [Asset], default));
    }

    [Fact]
    public async Task TheCatalogLibrary_IsAvailable_AndAsksTheCatalogForTheOrganizationAndUser()
    {
        var catalog = Substitute.For<IMediaAssetCatalog>();
        catalog.FindAsync(default, default, default!, default).ReturnsForAnyArgs([Summary(Asset)]);
        IContentMediaLibrary library = new CatalogMediaLibrary(catalog);

        var found = await library.FindAsync(TestData.AgencyId, TestData.UserId, [Asset], default);

        Assert.True(library.IsAvailable);
        Assert.Equal(Asset, Assert.Single(found).Id);
        await catalog.Received(1).FindAsync(
            TestData.AgencyId, TestData.UserId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { Asset })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AskingForNoFiles_DoesNotAskTheCatalog()
    {
        var catalog = Substitute.For<IMediaAssetCatalog>();

        var found = await new CatalogMediaLibrary(catalog).FindAsync(TestData.AgencyId, TestData.UserId, [], default);

        Assert.Empty(found);
        Assert.Empty(catalog.ReceivedCalls());
    }

    [Fact]
    public async Task AFileTheCatalogDoesNotKnow_IsNotInTheAnswer_SoACallerCanTellByTheCount()
    {
        var catalog = Substitute.For<IMediaAssetCatalog>();
        catalog.FindAsync(default, default, default!, default).ReturnsForAnyArgs([]);

        var found = await new CatalogMediaLibrary(catalog).FindAsync(TestData.AgencyId, TestData.UserId, [Asset, Guid.NewGuid()], default);

        Assert.Empty(found);
    }

    [Fact]
    public void WhenAModuleRegisteredTheContract_ContentUsesIt()
    {
        var catalog = Substitute.For<IMediaAssetCatalog>();
        var services = new ServiceCollection();
        services.AddSingleton(catalog);   // the Media module, added BEFORE Content
        services.AddContentModule();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var library = scope.ServiceProvider.GetRequiredService<IContentMediaLibrary>();
        Assert.IsType<CatalogMediaLibrary>(library);
        Assert.True(library.IsAvailable);
    }

    [Fact]
    public void TheOrderOfTheModules_DoesNotMatter()
    {
        var catalog = Substitute.For<IMediaAssetCatalog>();
        var services = new ServiceCollection();
        services.AddContentModule();
        services.AddSingleton(catalog);   // the Media module, added AFTER Content

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<CatalogMediaLibrary>(scope.ServiceProvider.GetRequiredService<IContentMediaLibrary>());
    }

    [Fact]
    public void WithoutAMediaModule_ThereIsNoLibrary_AndTheCapabilityStaysOff()
    {
        var services = new ServiceCollection();
        services.AddContentModule();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var library = scope.ServiceProvider.GetRequiredService<IContentMediaLibrary>();
        Assert.IsType<NoMediaLibrary>(library);
        Assert.False(library.IsAvailable);
    }

    // --- Telling the library where a post uses a file --------------------------------------------

    [Fact]
    public async Task TheCatalogUsage_NamesTheModuleAndTheKindOfThing_SoThePostIsWhatTheLibraryRecords()
    {
        var registry = Substitute.For<IMediaAssetUsageRegistry>();
        IContentMediaUsage usage = new CatalogMediaUsage(registry);
        var post = Guid.NewGuid();

        await usage.ReplaceAsync(TestData.AgencyId, TestData.UserId, post, [Asset], default);
        await usage.RemoveAllAsync(TestData.AgencyId, TestData.UserId, post, default);

        await registry.Received(1).ReplaceAsync(
            TestData.AgencyId, TestData.UserId, "content_studio", "post", post,
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { Asset })), Arg.Any<CancellationToken>());
        await registry.Received(1).RemoveAllAsync(TestData.AgencyId, TestData.UserId, "content_studio", "post", post, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNoLibrary_TellingItIsANoOp()
    {
        IContentMediaUsage usage = new NoMediaUsage();

        await usage.ReplaceAsync(TestData.AgencyId, TestData.UserId, Guid.NewGuid(), [Asset], default);
        await usage.RemoveAllAsync(TestData.AgencyId, TestData.UserId, Guid.NewGuid(), default);
    }

    [Fact]
    public void TheUsageNames_AreOnesTheLibraryAccepts()
    {
        // The table's own CHECK: a lower-case word of letters, digits and underscores, starting with a letter, at most 50 characters.
        var pattern = new System.Text.RegularExpressions.Regex("^[a-z][a-z0-9_]{0,49}$");

        Assert.Matches(pattern, MediaPolicy.UsageModule);
        Assert.Matches(pattern, MediaPolicy.UsageEntityType);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheUsageSeam_FollowsTheRegistry_InEitherOrderOfTheModules(bool mediaFirst)
    {
        var registry = Substitute.For<IMediaAssetUsageRegistry>();
        var services = new ServiceCollection();
        if (mediaFirst)
        {
            services.AddSingleton(registry);
        }

        services.AddContentModule();
        if (!mediaFirst)
        {
            services.AddSingleton(registry);
        }

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<CatalogMediaUsage>(scope.ServiceProvider.GetRequiredService<IContentMediaUsage>());
    }

    [Fact]
    public void WithoutAMediaModule_NobodyIsTold()
    {
        var services = new ServiceCollection();
        services.AddContentModule();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<NoMediaUsage>(scope.ServiceProvider.GetRequiredService<IContentMediaUsage>());
    }
}
