using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Platform.Content.Features.AI;
using Platform.Content.Features.Campaigns;
using Platform.Content.Features.Hashtags;
using Platform.Content.Features.Media;
using Platform.Content.Features.Posts;
using Platform.Content.Features.Publishing;
using Platform.Content.Features.Scheduling;
using Platform.Content.Features.Templates;
using Platform.Content.Interfaces;
using Platform.Content.Repositories;
using Platform.Content.Settings;
using Platform.Helpers.Media;

namespace Platform.Content;

/// <summary>
/// Content Studio: posts, hashtags and tags, templates, scheduling and publishing. Depends on Platform.Core only; it never
/// references Platform.Email or a channel project, and it never uses the Auth folder's namespace. Publishing reaches a channel
/// through the <c>ISocialPublisher</c> contract in Core, which each channel project implements and Platform.Api registers.
/// </summary>
public static class ContentModule
{
    public static IServiceCollection AddContentModule(this IServiceCollection services)
    {
        // Read lazily (options pattern), so a test or a deployment can override it, and checked when the host starts.
        services.AddOptions<ContentOptions>().BindConfiguration(ContentOptions.SectionName).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ContentOptions>, ContentOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IContentPostRepository, PostRepository>();
        services.AddScoped<ISchedulingRepository, SchedulingRepository>();
        services.AddScoped<IPublicationRepository, PublicationRepository>();
        services.AddScoped<IContentLookupRepository, ContentLookupRepository>();
        services.AddScoped<IHashtagRepository, HashtagRepository>();
        services.AddScoped<ITemplateRepository, TemplateRepository>();

        services.AddScoped<IPostService, PostService>();
        services.AddScoped<ISchedulingService, SchedulingService>();
        services.AddScoped<IPublishingService, PublishingService>();
        services.AddScoped<IHashtagService, HashtagService>();
        services.AddScoped<ITemplateService, TemplateService>();
        services.AddScoped<ICapabilitiesService, CapabilitiesService>();
        services.AddSingleton<IPublisherCatalog, PublisherCatalog>();

        // Three things that may not exist. TryAdd: the module that builds one registers its own first, or replaces these.
        // The Media Library is reached through the contract in Core: when Platform.Media registered IMediaAssetCatalog (in either
        // order, this is decided when it is first asked for) Content uses it, and otherwise there is no library.
        services.TryAddScoped<IContentMediaLibrary>(provider =>
            provider.GetService<IMediaAssetCatalog>() is { } catalog ? new CatalogMediaLibrary(catalog) : new NoMediaLibrary());
        // The same for telling the library where a post uses a file (IMediaAssetUsageRegistry, also registered by Platform.Media).
        services.TryAddScoped<IContentMediaUsage>(provider =>
            provider.GetService<IMediaAssetUsageRegistry>() is { } registry ? new CatalogMediaUsage(registry) : new NoMediaUsage());
        services.TryAddSingleton<IContentAiAssistant, NotConfiguredAiAssistant>();
        services.TryAddSingleton<ICampaignLookup, NoCampaigns>();

        services.AddHostedService<ContentPublishingWorker>();

        // Makes this assembly's controllers part of the app without relying on assembly scanning, as the Auth module does.
        services.AddControllers().AddApplicationPart(typeof(ContentModule).Assembly);

        return services;
    }
}
