using Platform.Content.Interfaces;
using Platform.Helpers.Media;

namespace Platform.Content.Features.Media;

/// <summary>
/// Tells the Media Library where a post uses a file, through the contract in Core (this project may not reference Platform.Media or
/// touch its tables). Registered by <see cref="ContentModule"/> when a module has registered <see cref="IMediaAssetUsageRegistry"/>.
/// </summary>
public sealed class CatalogMediaUsage(IMediaAssetUsageRegistry registry) : IContentMediaUsage
{
    public Task ReplaceAsync(Guid agencyId, Guid userId, Guid postId, IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken) =>
        registry.ReplaceAsync(agencyId, userId, MediaPolicy.UsageModule, MediaPolicy.UsageEntityType, postId, assetIds, cancellationToken);

    public Task RemoveAllAsync(Guid agencyId, Guid userId, Guid postId, CancellationToken cancellationToken) =>
        registry.RemoveAllAsync(agencyId, userId, MediaPolicy.UsageModule, MediaPolicy.UsageEntityType, postId, cancellationToken);
}
