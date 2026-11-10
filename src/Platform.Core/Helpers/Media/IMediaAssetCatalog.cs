namespace Platform.Helpers.Media;

/// <summary>
/// How a feature finds out about files in the Media Library without referencing the Media project. It lives in Core for the
/// reason <c>ISocialPublisher</c> does: a feature project may reference only Core, so the contract both sides know has to be here.
/// Platform.Media implements it and Platform.Api registers it; Content Studio (and any later feature) asks it.
/// </summary>
public interface IMediaAssetCatalog
{
    /// <summary>
    /// The files among <paramref name="assetIds"/> that exist for the organization. A file of another organization, or one that is
    /// gone, is simply not in the answer, so a caller compares the counts to find out that an id was refused. Runs for the given
    /// organization and user with row-level security on, like every query of a request.
    /// </summary>
    Task<IReadOnlyList<MediaAssetSummary>> FindAsync(
        Guid agencyId, Guid userId, IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken);
}

/// <summary>What a feature needs to know about one file: enough to check it and to show it, never the bytes.</summary>
/// <param name="Id">The file's id.</param>
/// <param name="WorkspaceId">The workspace the file belongs to.</param>
/// <param name="FileName">The name the file was uploaded with, for showing.</param>
/// <param name="MediaType">image, video, document or other.</param>
/// <param name="MimeType">The type read from the file's own first bytes, for example image/png.</param>
/// <param name="SizeBytes">The size in bytes.</param>
/// <param name="Width">Pixels, for an image whose size could be read; otherwise null.</param>
/// <param name="Height">Pixels, for an image whose size could be read; otherwise null.</param>
public sealed record MediaAssetSummary(
    Guid Id,
    Guid WorkspaceId,
    string FileName,
    string MediaType,
    string MimeType,
    long SizeBytes,
    int? Width,
    int? Height);
