namespace Platform.Content.Features.Media;

/// <summary>What Content Studio allows of the files on a post, and the names it uses when it tells the library where a file is used.</summary>
public static class MediaPolicy
{
    /// <summary>The most files one post can hold (a carousel's and Instagram's limit). The web client keeps the same number; a test compares them.</summary>
    public const int MaxMedia = 10;

    /// <summary>The longest caption of a file; the column's own limit.</summary>
    public const int MaxCaptionLength = 2200;

    /// <summary>The kinds of file a post can carry. A document or a sound is a file of the library but not something a post shows.</summary>
    public const string Image = "image";

    public const string Video = "video";

    public static bool IsAllowedType(string? mediaType) => mediaType is Image or Video;

    /// <summary>The module and kind of thing a post's usages are recorded under (<c>media_asset_usages</c>).</summary>
    public const string UsageModule = "content_studio";

    public const string UsageEntityType = "post";
}
