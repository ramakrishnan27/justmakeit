using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Platform.Helpers.Constants;
using Platform.Helpers.Media;
using Platform.Helpers.Tenancy;
using Platform.Infrastructure.Data;
using Platform.Media.Controllers;
using Platform.Media.Interfaces;
using Platform.Media.Providers;
using Platform.Media.Settings;

namespace Platform.Media.Tests;

public sealed class ModuleTests
{
    private static ServiceProvider Provider(Action<IServiceCollection>? more = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(Path.GetTempPath());
        services.AddSingleton(environment);
        services.AddSingleton(Substitute.For<IDbConnectionFactory>());
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddMediaModule();
        more?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
    }

    [Fact]
    public void TheModule_RegistersEveryServiceTheControllersNeed_AndTheContractOthersUse()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.NotNull(sp.GetRequiredService<IMediaAssetService>());
        Assert.NotNull(sp.GetRequiredService<IMediaFolderService>());
        Assert.NotNull(sp.GetRequiredService<IMediaAssetRepository>());
        Assert.NotNull(sp.GetRequiredService<IMediaFolderRepository>());
        Assert.NotNull(sp.GetRequiredService<IMediaLookupRepository>());
        Assert.NotNull(sp.GetRequiredService<IMediaAssetCatalog>());
        Assert.NotNull(sp.GetRequiredService<IMediaAssetUsageRegistry>());
        Assert.NotNull(sp.GetRequiredService<IMediaUsageRepository>());
        Assert.IsType<LocalDiskMediaStorage>(sp.GetRequiredService<IMediaStorage>());
    }

    [Fact]
    public void TheOptionsAreReadLazily_FromTheConfigurationOfTheHost()
    {
        using var provider = Provider(services =>
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Media:MaxFileMegabytes"] = "7",
                ["Media:Storage:RootPath"] = "D:\\media"
            }).Build()));

        var options = provider.GetRequiredService<IOptions<MediaOptions>>().Value;

        Assert.Equal(7, options.MaxFileMegabytes);
        Assert.Equal("D:\\media", options.Storage.RootPath);
    }

    [Fact]
    public void AnInvalidSetting_StopsTheHostWithAClearMessage()
    {
        using var provider = Provider(services =>
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Media:MaxFileMegabytes"] = "0"
            }).Build()));

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MediaOptions>>().Value);

        Assert.Contains("Media:MaxFileMegabytes", exception.Message);
    }

    [Fact]
    public void AStorageThatAnotherModuleRegisteredFirst_IsNotReplacedByTheDiskOne()
    {
        var other = Substitute.For<IMediaStorage>();
        var services = new ServiceCollection();
        services.AddSingleton(other);

        services.AddLogging().AddMediaModule();

        using var provider = services.BuildServiceProvider();
        Assert.Same(other, provider.GetRequiredService<IMediaStorage>());
    }

    [Fact]
    public void TheModule_AddsItsControllersToTheApp()
    {
        using var provider = new ServiceCollection().AddLogging().AddMediaModule().BuildServiceProvider();

        var manager = provider.GetRequiredService<Microsoft.AspNetCore.Mvc.ApplicationParts.ApplicationPartManager>();

        Assert.Contains(
            manager.ApplicationParts.OfType<Microsoft.AspNetCore.Mvc.ApplicationParts.AssemblyPart>(),
            part => part.Assembly == typeof(MediaModule).Assembly);
    }
}

public sealed class ControllerTests
{
    private static readonly Type[] Controllers =
        [.. typeof(MediaModule).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(ControllerBase)) && !t.IsAbstract)];

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes().OfType<HttpMethodAttribute>().Any());

    [Fact]
    public void ThereAreThreeControllers_SoANewOneIsNoticedHere() => Assert.Equal(3, Controllers.Length);

    [Fact]
    public void EveryEndpoint_NamesAPermissionThatExists_AndNoneIsAnonymous()
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

    private static string? PermissionOf(Type controller, string action)
    {
        // DeclaredOnly: ControllerBase has its own File(...) overloads, which would make the name ambiguous.
        var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
        return method.GetCustomAttributes<AuthorizeAttribute>().Single().Policy![PolicyNames.PermissionPrefix.Length..];
    }

    [Theory]
    [InlineData(typeof(MediaAssetsController), nameof(MediaAssetsController.List), PermissionKeys.MediaLibraryView)]
    [InlineData(typeof(MediaAssetsController), nameof(MediaAssetsController.Get), PermissionKeys.MediaLibraryView)]
    [InlineData(typeof(MediaAssetsController), nameof(MediaAssetsController.File), PermissionKeys.MediaLibraryView)]
    [InlineData(typeof(MediaAssetsController), nameof(MediaAssetsController.Upload), PermissionKeys.MediaLibraryCreate)]
    [InlineData(typeof(MediaAssetsController), nameof(MediaAssetsController.Organize), PermissionKeys.MediaLibraryCreate)]
    [InlineData(typeof(MediaAssetsController), nameof(MediaAssetsController.Delete), PermissionKeys.MediaLibraryDelete)]
    [InlineData(typeof(MediaLibraryController), nameof(MediaLibraryController.Config), PermissionKeys.MediaLibraryView)]
    [InlineData(typeof(MediaLibraryController), nameof(MediaLibraryController.Overview), PermissionKeys.MediaLibraryView)]
    [InlineData(typeof(MediaFoldersController), nameof(MediaFoldersController.Create), PermissionKeys.MediaLibraryCreate)]
    [InlineData(typeof(MediaFoldersController), nameof(MediaFoldersController.Delete), PermissionKeys.MediaLibraryDelete)]
    public void EachEndpoint_NeedsTheRightPermission(Type controller, string action, string permission) =>
        Assert.Equal(permission, PermissionOf(controller, action));

    [Fact]
    public void EveryRoute_IsUnderV1Media()
    {
        foreach (var controller in Controllers)
        {
            Assert.StartsWith("v1/media", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        }
    }

    [Fact]
    public void EveryAnswerButTheFileItself_IsMarkedNoStore()
    {
        foreach (var controller in Controllers)
        {
            foreach (var action in Actions(controller))
            {
                var cache = action.GetCustomAttribute<ResponseCacheAttribute>();
                Assert.NotNull(cache);
                if (action.Name == nameof(MediaAssetsController.File))
                {
                    // The bytes of a file never change once it exists, and they are private to the browser that asked.
                    Assert.False(cache.NoStore);
                    Assert.Equal(ResponseCacheLocation.Client, cache.Location);
                    Assert.Equal(3600, cache.Duration);
                }
                else
                {
                    Assert.True(cache.NoStore, $"{controller.Name}.{action.Name}");
                }
            }
        }
    }

    [Fact]
    public void OnlyTheUpload_TakesAFormAndRaisesTheSizeLimit()
    {
        var upload = typeof(MediaAssetsController).GetMethod(nameof(MediaAssetsController.Upload))!;

        Assert.Contains("multipart/form-data", upload.GetCustomAttribute<ConsumesAttribute>()!.ContentTypes);
        Assert.Equal(typeof(MediaUploadLimitFilter), upload.GetCustomAttribute<TypeFilterAttribute>()!.ImplementationType);

        foreach (var controller in Controllers)
        {
            foreach (var action in Actions(controller).Where(a => a != upload))
            {
                Assert.Null(action.GetCustomAttribute<TypeFilterAttribute>());
            }
        }
    }

    [Fact]
    public void TheRoutesOfThePlan_AreAllThere()
    {
        var routes = Controllers
            .SelectMany(controller => Actions(controller).Select(action =>
            {
                var http = action.GetCustomAttributes().OfType<HttpMethodAttribute>().Single();
                var template = controller.GetCustomAttribute<RouteAttribute>()!.Template;
                return $"{http.HttpMethods.Single()} {template}{(string.IsNullOrEmpty(http.Template) ? string.Empty : "/" + http.Template)}";
            }))
            .Order()
            .ToList();

        Assert.Equal(
            new[]
            {
                "DELETE v1/media/assets/{id:guid}",
                "DELETE v1/media/folders/{id:guid}",
                "GET v1/media/assets",
                "GET v1/media/assets/{id:guid}",
                "GET v1/media/assets/{id:guid}/file",
                "GET v1/media/config",
                "GET v1/media/overview",
                "POST v1/media/assets",
                "POST v1/media/assets/organize",
                "POST v1/media/folders"
            }.Order(),
            routes);
    }
}

public sealed class UploadLimitFilterTests
{
    private sealed class Body : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly { get; set; }
        public long? MaxRequestBodySize { get; set; } = 30_000_000;
    }

    private static ResourceExecutingContext ContextWith(DefaultHttpContext http) =>
        new(new ActionContext(http, new RouteData(), new ActionDescriptor()), [], []);

    private static MediaUploadLimitFilter Filter(int megabytes) =>
        new(Options.Create(new MediaOptions { MaxFileMegabytes = megabytes }), Options.Create(new FormOptions()));

    [Fact]
    public void TheBodyLimit_IsTheLargestFilePlusRoomForTheForm()
    {
        var http = new DefaultHttpContext();
        var body = new Body();
        http.Features.Set<IHttpMaxRequestBodySizeFeature>(body);

        Filter(250).OnResourceExecuting(ContextWith(http));

        Assert.Equal(250L * 1024 * 1024 + MediaUploadLimitFilter.FormOverheadBytes, body.MaxRequestBodySize);
    }

    [Fact]
    public void TheLimitOfOnePartOfTheForm_IsRaisedToo_FromTheHostsOwnDefaults()
    {
        var http = new DefaultHttpContext();
        http.Features.Set<IHttpMaxRequestBodySizeFeature>(new Body());

        Filter(250).OnResourceExecuting(ContextWith(http));

        var form = http.Features.Get<IFormFeature>();
        Assert.NotNull(form);
        Assert.Null(form.Form);   // nothing has been read yet
    }

    [Fact]
    public void ABodyThatIsAlreadyBeingRead_IsLeftAlone()
    {
        var http = new DefaultHttpContext();
        var body = new Body { IsReadOnly = true };
        http.Features.Set<IHttpMaxRequestBodySizeFeature>(body);

        Filter(250).OnResourceExecuting(ContextWith(http));

        Assert.Equal(30_000_000, body.MaxRequestBodySize);
    }

    [Fact]
    public void ARequestWithoutTheBodyFeature_DoesNotFail()
    {
        Filter(10).OnResourceExecuting(ContextWith(new DefaultHttpContext()));
    }
}
