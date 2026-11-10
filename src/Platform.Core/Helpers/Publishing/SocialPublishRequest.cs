namespace Platform.Helpers.Publishing;

/// <summary>One post going to one platform. Plain text only: what the platform will show, already per-platform where the person customized it.</summary>
public sealed class SocialPublishRequest
{
    /// <summary>The target being published (<c>content_post_platforms.id</c>). The same value on every retry, so a publisher can avoid posting twice.</summary>
    public required Guid PublicationId { get; init; }

    /// <summary>The organization the post belongs to.</summary>
    public required Guid AgencyId { get; init; }

    /// <summary>The connected account to post as. Null until social accounts can be connected.</summary>
    public Guid? SocialAccountId { get; init; }

    /// <summary>The title for platforms that have one (a YouTube video, a LinkedIn article). Null when the platform has none.</summary>
    public string? Title { get; init; }

    /// <summary>The text to post: the platform's own caption when there is one, otherwise the post's text.</summary>
    public required string Text { get; init; }

    /// <summary>Hashtags without the leading #, in the order the person chose.</summary>
    public IReadOnlyList<string> Hashtags { get; init; } = [];

    /// <summary>The platform-specific settings as a JSON object (<c>content_post_platforms.platform_settings</c>).</summary>
    public string SettingsJson { get; init; } = "{}";
}
