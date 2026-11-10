using Platform.Helpers.Media;
using Platform.Media.Interfaces;

namespace Platform.Media.Services;

/// <summary>
/// The Media Library's side of the contract in Core: how Content Studio, and any later feature, finds out about files without
/// referencing this project. It runs for the organization and user it is given with row-level security on, so a file of another
/// organization is not found. The caller has already checked the person's permission, as the repositories of every feature rely on.
/// </summary>
public sealed class MediaAssetCatalog(IMediaAssetRepository assets) : IMediaAssetCatalog
{
    public Task<IReadOnlyList<MediaAssetSummary>> FindAsync(
        Guid agencyId, Guid userId, IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken) =>
        assets.FindAsync(agencyId, userId, assetIds, cancellationToken);
}
