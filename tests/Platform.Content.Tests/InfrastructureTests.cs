using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Platform.Content.Data;
using Platform.Content.DTOs;
using Platform.Content.Enums;
using Platform.Content.Features.AI;
using Platform.Content.Features.Campaigns;
using Platform.Content.Features.Media;
using Platform.Content.Features.Publishing;
using Platform.Content.Interfaces;
using Platform.Content.Settings;
using Platform.Content.Tests.Support;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Publishing;
using Platform.Helpers.Tenancy;
using Platform.Infrastructure.Data;

namespace Platform.Content.Tests;

public sealed class InfrastructureTests
{
    private static IOptions<ContentOptions> Options(bool fake = false) =>
        Microsoft.Extensions.Options.Options.Create(new ContentOptions { Publishing = { UseFakePublisher = fake } });

    // --- Publisher catalog ------------------------------------------------------------------------

    private static ISocialPublisher Real(string platform)
    {
        var publisher = Substitute.For<ISocialPublisher>();
        publisher.Platform.Returns(platform);
        return publisher;
    }

    [Fact]
    public void WithNoPublisherAndNoFake_NothingCanPublish()
    {
        var catalog = new PublisherCatalog([], Options());

        Assert.Equal("none", catalog.Mode);
        Assert.All(DbEnum<SocialPlatform>.All, platform => Assert.Null(catalog.Find(platform)));
    }

    [Fact]
    public void WithTheFake_EveryPlatformHasOne_AndTheModeSaysSo()
    {
        var catalog = new PublisherCatalog([], Options(fake: true));

        Assert.Equal("fake", catalog.Mode);
        Assert.All(DbEnum<SocialPlatform>.All, platform =>
        {
            Assert.IsType<FakeSocialPublisher>(catalog.Find(platform));
            Assert.Equal(platform, catalog.Find(platform)!.Platform);
        });
    }

    [Fact]
    public void ARealPublisher_IsUsedForItsPlatform_TheFakeFillsTheRest_AndTheModeIsLive()
    {
        var instagram = Real("instagram");

        var catalog = new PublisherCatalog([instagram], Options(fake: true));

        Assert.Equal("live", catalog.Mode);
        Assert.Same(instagram, catalog.Find("instagram"));
        Assert.IsType<FakeSocialPublisher>(catalog.Find("facebook"));
    }

    [Fact]
    public void RealPublishersWithoutTheFake_CoverOnlyTheirOwnPlatforms()
    {
        var catalog = new PublisherCatalog([Real("facebook"), Real("youtube")], Options());

        Assert.Equal("live", catalog.Mode);
        Assert.NotNull(catalog.Find("facebook"));
        Assert.Null(catalog.Find("instagram"));
    }

    [Fact]
    public void TwoPublishersForOnePlatform_TheFirstWins()
    {
        var first = Real("x");
        var second = Real("x");

        Assert.Same(first, new PublisherCatalog([first, second], Options()).Find("x"));
    }

    // --- Fake publisher ---------------------------------------------------------------------------------

    private static SocialPublishRequest Request(string text) => new() { PublicationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), AgencyId = TestData.AgencyId, Text = text };

    [Fact]
    public async Task TheFake_SucceedsWithAStableIdAndAWebAddress_AndPostsNothing()
    {
        var result = await new FakeSocialPublisher("x").PublishAsync(Request("Hello"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("fake-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.ExternalPostId);
        Assert.Equal("https://example.com/fake/x/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.ExternalPostUrl);
    }

    [Fact]
    public async Task TheFake_FailsWhenAskedTo_ForGoodOrToBeTriedAgain()
    {
        var publisher = new FakeSocialPublisher("x");

        var forGood = await publisher.PublishAsync(Request("this should [FAIL] now"), CancellationToken.None);
        var again = await publisher.PublishAsync(Request("try [retry] later"), CancellationToken.None);

        Assert.False(forGood.Succeeded);
        Assert.False(forGood.Retryable);
        Assert.False(again.Succeeded);
        Assert.True(again.Retryable);
    }

    // --- Options ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheDefaults_AreValid_AndTheFakeIsOffByDefault()
    {
        var options = new ContentOptions();

        Assert.True(new ContentOptionsValidator().Validate(null, options).Succeeded);
        Assert.False(options.Publishing.UseFakePublisher);
        Assert.True(options.Publishing.WorkerEnabled);
    }

    [Theory]
    [InlineData(nameof(PublishingOptions.PollSeconds), 0)]
    [InlineData(nameof(PublishingOptions.PollSeconds), 3601)]
    [InlineData(nameof(PublishingOptions.BatchSize), 0)]
    [InlineData(nameof(PublishingOptions.BatchSize), 101)]
    [InlineData(nameof(PublishingOptions.MaxAttempts), 0)]
    [InlineData(nameof(PublishingOptions.MaxAttempts), 11)]
    [InlineData(nameof(PublishingOptions.StaleMinutes), 0)]
    [InlineData(nameof(PublishingOptions.StaleMinutes), 1441)]
    [InlineData(nameof(PublishingOptions.StartupDelaySeconds), -1)]
    [InlineData(nameof(PublishingOptions.StartupDelaySeconds), 601)]
    public void ASettingOutsideItsRange_IsRefusedByName(string property, int value)
    {
        var options = new ContentOptions();
        typeof(PublishingOptions).GetProperty(property)!.SetValue(options.Publishing, value);

        var result = new ContentOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("Content:Publishing:" + property));
    }

    [Fact]
    public void TheSettings_AreReadFromTheContentSectionOfTheConfiguration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Content:Publishing:UseFakePublisher"] = "true",
            ["Content:Publishing:PollSeconds"] = "5",
            ["Content:Publishing:WorkerEnabled"] = "false"
        }).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration);
        services.AddContentModule();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ContentOptions>>().Value;

        Assert.Equal((true, 5, false), (options.Publishing.UseFakePublisher, options.Publishing.PollSeconds, options.Publishing.WorkerEnabled));
    }

    [Fact]
    public void ABadSetting_StopsTheHostFromStarting()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Content:Publishing:PollSeconds"] = "0" }).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration);
        services.AddContentModule();

        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ContentOptions>>().Value);
    }

    // --- The things that do not exist yet ---------------------------------------------------------------

    [Fact]
    public async Task TheSeams_SayTheyAreNotThere()
    {
        Assert.False(new NoMediaLibrary().IsAvailable);
        Assert.False(new NotConfiguredAiAssistant().IsAvailable);
        var campaigns = new NoCampaigns();
        Assert.False(campaigns.IsAvailable);
        Assert.False(await campaigns.ExistsAsync(TestData.AgencyId, TestData.UserId, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public void Capabilities_TellThePageWhatIsReal()
    {
        var tenant = TestData.Tenant(PermissionKeys.ContentStudioView);
        var catalog = new PublisherCatalog([], Options(fake: true));
        var service = new CapabilitiesService(new NoMediaLibrary(), new NotConfiguredAiAssistant(), new NoCampaigns(), catalog, Substitute.For<IContentLookupRepository>(), tenant);

        Assert.Equal(new CapabilitiesResponse(false, false, false, "fake"), service.GetCapabilities());
    }

    [Fact]
    public async Task Capabilities_AndPlatformRules_NeedTheViewPermission()
    {
        var service = new CapabilitiesService(new NoMediaLibrary(), new NotConfiguredAiAssistant(), new NoCampaigns(), new PublisherCatalog([], Options()), Substitute.For<IContentLookupRepository>(), TestData.Tenant());

        Assert.Throws<ForbiddenException>(service.GetCapabilities);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetPlatformRulesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PlatformRules_ListEightPlatformsWithTheirLimits_AndWhichAreEnabled()
    {
        var lookups = Substitute.For<IContentLookupRepository>();
        lookups.EnabledPlatformsAsync(default, default, default).ReturnsForAnyArgs(Task.FromResult<IReadOnlySet<string>>(new HashSet<string> { "instagram", "youtube" }));
        var service = new CapabilitiesService(new NoMediaLibrary(), new NotConfiguredAiAssistant(), new NoCampaigns(), new PublisherCatalog([], Options()), lookups, TestData.Tenant(PermissionKeys.ContentStudioView));

        var rules = await service.GetPlatformRulesAsync(CancellationToken.None);

        Assert.Equal(DbEnum<SocialPlatform>.All, rules.Select(r => r.Platform));
        Assert.Equal(["instagram", "youtube"], rules.Where(r => r.Enabled).Select(r => r.Platform));
        var x = rules.Single(r => r.Platform == "x");
        Assert.Equal(("X", 280, (int?)null, (int?)null), (x.Name, x.CaptionLimit, x.TitleLimit, x.MaxHashtags));
        Assert.Equal(100, rules.Single(r => r.Platform == "youtube").TitleLimit);
        Assert.Equal(30, rules.Single(r => r.Platform == "instagram").MaxHashtags);
    }

    // --- The dependency graph ----------------------------------------------------------------------------

    [Fact]
    public void EveryServiceTheModuleRegisters_CanBeBuilt_WithItsDependenciesAndLifetimesInOrder()
    {
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddLogging()
            .AddScoped(_ => Substitute.For<IDbConnectionFactory>())
            .AddScoped<ITenantContext>(_ => new TenantContext());
        services.AddContentModule();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<IPostService>());
        Assert.NotNull(sp.GetRequiredService<ISchedulingService>());
        Assert.NotNull(sp.GetRequiredService<IPublishingService>());
        Assert.NotNull(sp.GetRequiredService<IHashtagService>());
        Assert.NotNull(sp.GetRequiredService<ITemplateService>());
        Assert.NotNull(sp.GetRequiredService<ICapabilitiesService>());
        Assert.NotNull(provider.GetRequiredService<IPublisherCatalog>());
        Assert.Contains(provider.GetServices<IHostedService>(), service => service is ContentPublishingWorker);
    }

    [Fact]
    public void ARealModuleThatRegistersFirst_ReplacesTheSeams()
    {
        var campaigns = Substitute.For<ICampaignLookup>();
        var services = new ServiceCollection().AddSingleton(campaigns);
        services.AddSingleton<ICampaignLookup>(campaigns);

        services.AddContentModule();

        using var provider = services.BuildServiceProvider();
        Assert.Same(campaigns, provider.GetRequiredService<ICampaignLookup>());
        Assert.IsType<NoMediaLibrary>(provider.GetRequiredService<IContentMediaLibrary>());
    }

    // --- Every endpoint names a permission ----------------------------------------------------------------

    private static readonly Type[] Controllers =
        [.. typeof(ContentModule).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(ControllerBase)) && !t.IsAbstract)];

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes().OfType<HttpMethodAttribute>().Any());

    [Fact]
    public void ThereAreFiveControllers_SoANewOneIsNoticedHere() => Assert.Equal(5, Controllers.Length);

    [Fact]
    public void EveryEndpoint_NamesAPermissionThatExists_SoNothingIsLeftToTheFallbackPolicyByAccident()
    {
        var prefix = PolicyNames.PermissionPrefix;
        var unprotected = new List<string>();
        foreach (var controller in Controllers)
        {
            foreach (var action in Actions(controller))
            {
                var policies = action.GetCustomAttributes<AuthorizeAttribute>().Concat(controller.GetCustomAttributes<AuthorizeAttribute>())
                    .Select(a => a.Policy)
                    .ToList();
                var named = policies.Where(policy => policy is not null && policy.StartsWith(prefix, StringComparison.Ordinal)).ToList();
                if (named.Count == 0 || named.Any(policy => !PermissionKeys.All.Contains(policy![prefix.Length..])))
                {
                    unprotected.Add($"{controller.Name}.{action.Name}");
                }

                Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>());
            }
        }

        Assert.Empty(unprotected);
    }

    [Fact]
    public void EveryControllerIsUnderV1Content_AndNothingIsCached()
    {
        foreach (var controller in Controllers)
        {
            var route = controller.GetCustomAttribute<RouteAttribute>();
            Assert.StartsWith("v1/content", route!.Template);
            var cache = controller.GetCustomAttribute<ResponseCacheAttribute>();
            Assert.True(cache is { NoStore: true }, controller.Name);
        }
    }

    [Fact]
    public void TheRoutesOfThePlan_AreAllThere()
    {
        var routes = Controllers
            .SelectMany(controller => Actions(controller).Select(action =>
            {
                var http = action.GetCustomAttributes().OfType<HttpMethodAttribute>().Single();
                var prefix = controller.GetCustomAttribute<RouteAttribute>()!.Template;
                return $"{http.HttpMethods.Single()} {prefix}/{http.Template}".TrimEnd('/');
            }))
            .Order()
            .ToArray();

        Assert.Equal(
            new[]
            {
                "DELETE v1/content/posts/{id:guid}",
                "DELETE v1/content/templates/{id:guid}",
                "GET v1/content/capabilities",
                "GET v1/content/hashtags/suggestions",
                "GET v1/content/platform-rules",
                "GET v1/content/posts",
                "GET v1/content/posts/{id:guid}",
                "GET v1/content/posts/{id:guid}/revisions",
                "GET v1/content/tags",
                "GET v1/content/templates",
                "POST v1/content/posts",
                "POST v1/content/posts/{id:guid}/archive",
                "POST v1/content/posts/{id:guid}/duplicate",
                "POST v1/content/posts/{id:guid}/publish-now",
                "POST v1/content/posts/{id:guid}/restore",
                "POST v1/content/posts/{id:guid}/schedule",
                "POST v1/content/posts/{id:guid}/unschedule",
                "POST v1/content/tags",
                "POST v1/content/templates",
                "PUT v1/content/posts/{id:guid}"
            }.Order(),
            routes);
    }
}
