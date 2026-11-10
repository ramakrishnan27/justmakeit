using System.Text.Json;
using Platform.Content.Data;
using Platform.Content.DTOs;
using Platform.Content.Entities;
using Platform.Content.Enums;
using Platform.Content.Interfaces;
using Platform.Helpers.Media;

namespace Platform.Content.Features.Posts;

/// <summary>Entities to what the API sends. Nothing here reads a database.</summary>
public static class PostMapper
{
    /// <summary>JSON text from the database as a value that is written out as JSON, not as a string.</summary>
    public static JsonElement Json(string? text, string fallback = "{}")
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? fallback : text);
        return document.RootElement.Clone();
    }

    /// <param name="aggregate">The post with its children.</param>
    /// <param name="media">What the library says about the files on the post, by id. A file missing here is still listed, by its id.</param>
    public static PostResponse ToResponse(ContentPostAggregate aggregate, IReadOnlyDictionary<Guid, MediaAssetSummary>? media = null)
    {
        var post = aggregate.Post;
        return new PostResponse(
            post.Id,
            post.WorkspaceId,
            post.CampaignId,
            post.ContentType,
            post.Title,
            post.BodyDoc is null ? null : Json(post.BodyDoc),
            post.Body,
            post.Excerpt,
            post.Status,
            post.Visibility,
            post.LanguageCode,
            post.Timezone,
            post.ScheduledAt,
            post.PublishedAt,
            post.ArchivedAt,
            post.SeoTitle,
            post.SeoDescription,
            post.SeoSlug,
            post.SeoKeywords,
            aggregate.Hashtags.OrderBy(h => h.DisplayOrder).Select(h => h.Hashtag).ToList(),
            aggregate.Tags.Select(t => new TagResponse(t.Id, t.Name, t.Color)).ToList(),
            aggregate.Platforms.Select(ToResponse).ToList(),
            aggregate.Poll is null
                ? null
                : new PollResponse(aggregate.Poll.Question, aggregate.Poll.EndsAt, aggregate.PollOptions.OrderBy(o => o.DisplayOrder).Select(o => o.OptionText).ToList()),
            aggregate.Ctas.OrderBy(c => c.DisplayOrder).Select(c => new CtaResponse(c.ButtonText, c.TargetUrl)).ToList(),
            aggregate.Mentions.Select(m => new MentionResponse(m.Platform, m.MentionedName)).ToList(),
            aggregate.Location is null ? null : new LocationResponse(aggregate.Location.LocationName, aggregate.Location.Latitude, aggregate.Location.Longitude),
            aggregate.Media.OrderBy(m => m.DisplayOrder).Select(m => ToResponse(m, media)).ToList(),
            Json(post.Settings),
            post.CreatedBy,
            post.UpdatedBy,
            post.CreatedAt,
            post.UpdatedAt);
    }

    private static PostMediaResponse ToResponse(ContentPostMedia file, IReadOnlyDictionary<Guid, MediaAssetSummary>? media)
    {
        var summary = media is not null && media.TryGetValue(file.MediaAssetId, out var found) ? found : null;
        return new PostMediaResponse(
            file.MediaAssetId,
            file.MediaRole,
            file.DisplayOrder,
            file.Caption,
            summary?.FileName ?? string.Empty,
            summary?.MediaType ?? string.Empty,
            summary?.MimeType ?? string.Empty,
            summary?.SizeBytes ?? 0,
            summary?.Width,
            summary?.Height);
    }

    private static PostPlatformResponse ToResponse(ContentPostPlatform target) =>
        new(
            target.Platform,
            target.PlatformCaption,
            target.PlatformTitle,
            Json(target.PlatformSettings),
            target.PublishStatus,
            target.ScheduledAt,
            target.PublishedAt,
            target.ExternalPostUrl,
            target.LastError,
            target.RetryCount);

    public static PostPageResponse ToPage(PostListResult result, PostFilter filter)
    {
        var platforms = result.Platforms.ToLookup(p => p.PostId);
        var tags = result.Tags.ToLookup(t => t.PostId);

        var items = result.Items.Select(post => new PostListItemResponse(
            post.Id,
            post.WorkspaceId,
            post.ContentType,
            post.Title,
            post.Excerpt,
            post.Status,
            post.Visibility,
            platforms[post.Id].Select(p => new PostListPlatform(p.Platform, p.PublishStatus)).ToList(),
            tags[post.Id].Select(t => new TagResponse(t.Id, t.Name, t.Color)).ToList(),
            post.ScheduledAt,
            post.PublishedAt,
            post.CreatedAt,
            post.UpdatedAt)).ToList();

        // Every type has a count (0 when there is none) and "all" is their sum, so the tabs always show a number.
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var type in DbEnum<ContentType>.All)
        {
            counts[type] = result.TypeCounts.GetValueOrDefault(type);
        }

        counts["all"] = counts.Values.Sum();

        var total = filter.Type is null ? counts["all"] : counts.GetValueOrDefault(filter.Type);
        var pages = (int)((total + filter.PageSize - 1) / filter.PageSize);
        return new PostPageResponse(items, filter.Page, filter.PageSize, total, pages, counts);
    }
}
