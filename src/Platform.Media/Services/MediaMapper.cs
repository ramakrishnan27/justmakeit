using Platform.Infrastructure.Data;
using Platform.Media.DTOs;
using Platform.Media.Models;

namespace Platform.Media.Services;

/// <summary>Rows into what the API sends.</summary>
public static class MediaMapper
{
    public static MediaAssetResponse ToResponse(
        MediaAssetRow row, IReadOnlyList<MediaAssetTagRow>? tags, IReadOnlyList<Guid>? folderIds) =>
        new(
            row.Id,
            row.WorkspaceId,
            row.FileName,
            row.MimeType,
            row.MediaType,
            row.FileSizeBytes,
            row.Width,
            row.Height,
            row.UsageCount,
            row.CreatedAt,
            (tags ?? []).Select(tag => new MediaTagRef(tag.TagId, tag.Name)).ToList(),
            folderIds ?? []);

    public static MediaTypeCounts ToCounts(IReadOnlyDictionary<string, int> byType)
    {
        int Of(MediaType type) => byType.GetValueOrDefault(type.ToDb());
        return new MediaTypeCounts(
            byType.Values.Sum(), Of(MediaType.Image), Of(MediaType.Video), Of(MediaType.Document), Of(MediaType.Other));
    }
}
