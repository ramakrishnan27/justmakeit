using Platform.Content.Interfaces;
using Platform.Helpers.Media;

namespace Platform.Content.Features.Media;

/// <summary>There is no Media Library in this installation (no module registered <see cref="IMediaAssetCatalog"/>), so nothing can be attached to a post.</summary>
public sealed class NoMediaLibrary : IContentMediaLibrary
{
    public bool IsAvailable => false;

    public Task<IReadOnlyList<MediaAssetSummary>> FindAsync(
        Guid agencyId, Guid userId, IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MediaAssetSummary>>([]);
}
