using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Platform.Helpers.Media;
using Platform.Media.Interfaces;
using Platform.Media.Providers;
using Platform.Media.Repositories;
using Platform.Media.Services;
using Platform.Media.Settings;

namespace Platform.Media;

/// <summary>
/// The Media Library: files, folders and tags per workspace. Depends on Platform.Core only; it never references Platform.Content, Platform.Email or
/// a channel project, and it never uses the Auth folder's namespace. Content Studio and other features reach a file through the
/// <c>IMediaAssetCatalog</c> contract in Core, which this module implements and Platform.Api registers.
/// </summary>
public static class MediaModule
{
    public static IServiceCollection AddMediaModule(this IServiceCollection services)
    {
        // Read lazily (options pattern), so a test or a deployment can override it, and checked when the host starts.
        services.AddOptions<MediaOptions>().BindConfiguration(MediaOptions.SectionName).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MediaOptions>, MediaOptionsValidator>());

        // Where the bytes are kept. TryAdd: a deployment that keeps them elsewhere registers its own IMediaStorage first.
        services.TryAddSingleton<IMediaStorage, LocalDiskMediaStorage>();

        services.AddScoped<IMediaAssetRepository, MediaAssetRepository>();
        services.AddScoped<IMediaFolderRepository, MediaFolderRepository>();
        services.AddScoped<IMediaLookupRepository, MediaLookupRepository>();
        services.AddScoped<IMediaUsageRepository, MediaUsageRepository>();

        services.AddScoped<IMediaAssetService, MediaAssetService>();
        services.AddScoped<IMediaFolderService, MediaFolderService>();

        // The contracts other features use: to find out about files (Platform.Content's CatalogMediaLibrary) and to say where they use them (CatalogMediaUsage).
        services.AddScoped<IMediaAssetCatalog, MediaAssetCatalog>();
        services.AddScoped<IMediaAssetUsageRegistry, MediaAssetUsageRegistry>();

        // Makes this assembly's controllers part of the app without relying on assembly scanning, as the other modules do.
        services.AddControllers().AddApplicationPart(typeof(MediaModule).Assembly);

        return services;
    }
}
