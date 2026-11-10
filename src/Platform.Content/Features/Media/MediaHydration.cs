using Platform.Content.DTOs;
using Platform.Content.Entities;
using Platform.Content.Features.Posts;
using Platform.Content.Interfaces;

namespace Platform.Content.Features.Media;

/// <summary>
/// A post as the API sends it, with what the library says about each of its files (the name, the kind, the size) so the page can show
/// them without a second call. Used by every service that answers with a whole post. A file the library does not return still
/// appears, by its id, so a post never loses a file from what the page sees.
/// </summary>
public static class MediaHydration
{
    public static async Task<PostResponse> ToResponseAsync(
        IContentMediaLibrary media, ContentPostAggregate aggregate, Guid agencyId, Guid userId, CancellationToken cancellationToken)
    {
        if (aggregate.Media.Count == 0 || !media.IsAvailable)
        {
            return PostMapper.ToResponse(aggregate);
        }

        var found = await media.FindAsync(agencyId, userId, [.. aggregate.Media.Select(m => m.MediaAssetId).Distinct()], cancellationToken);
        return PostMapper.ToResponse(aggregate, found.ToDictionary(summary => summary.Id));
    }
}
