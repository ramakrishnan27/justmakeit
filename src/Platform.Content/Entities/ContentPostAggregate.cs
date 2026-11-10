namespace Platform.Content.Entities;

/// <summary>
/// A post with everything that belongs to it, read and written as one unit (one transaction). The children of a post are
/// replaced as a whole when it is saved, which is why an edit is only allowed while no platform is publishing.
/// </summary>
public sealed class ContentPostAggregate
{
    public required ContentPost Post { get; init; }

    public List<ContentHashtag> Hashtags { get; init; } = [];

    /// <summary>The tags of the post. A tag that does not exist in the workspace yet has an empty id and is created on save.</summary>
    public List<ContentTag> Tags { get; init; } = [];

    public List<ContentPostPlatform> Platforms { get; init; } = [];
    public ContentPoll? Poll { get; set; }
    public List<ContentPollOption> PollOptions { get; init; } = [];
    public List<ContentCta> Ctas { get; init; } = [];
    public List<ContentMention> Mentions { get; init; } = [];
    public ContentLocation? Location { get; set; }

    /// <summary>The files of the Media Library on the post, in order. Replaced as a whole when the post is saved, like every child.</summary>
    public List<ContentPostMedia> Media { get; init; } = [];
}
