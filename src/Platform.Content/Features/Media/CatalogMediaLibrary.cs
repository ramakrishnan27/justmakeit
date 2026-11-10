using Platform.Content.Interfaces;
using Platform.Helpers.Media;

namespace Platform.Content.Features.Media;

/// <summary>
/// Content Studio's view of the Media Library: the Media project's catalog, reached through the contract in Core, because this
/// project may not reference Platform.Media. Registered by <see cref="ContentModule"/> when a module has registered
/// <see cref="IMediaAssetCatalog"/>.
/// </summary>
public sealed class CatalogMediaLibrary(IMediaAssetCatalog catalog) : IContentMediaLibrary
{
    public bool IsAvailable => true;

    public Task<IReadOnlyList<MediaAssetSummary>> FindAsync(
        Guid agencyId, Guid userId, IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken) =>
        assetIds.Count == 0
            ? Task.FromResult<IReadOnlyList<MediaAssetSummary>>([])
            : catalog.FindAsync(agencyId, userId, assetIds, cancellationToken);
}
