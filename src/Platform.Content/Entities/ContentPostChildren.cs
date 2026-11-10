namespace Platform.Content.Entities;

// The tables that hang off a post (014_content_studio.sql). One class per table, settable properties for Dapper.

/// <summary>A row of <c>content_post_platforms</c>: one post going to one platform.</summary>
public sealed class ContentPostPlatform
{
    public Guid Id { get; set; }
    public Guid AgencyId { get; set; }
    public Guid PostId { get; set; }

    /// <summary>Null until a social account can be connected (015_content_studio_posts.sql).</summary>
    public Guid? SocialAccountId { get; set; }

    public string Platform { get; set; } = string.Empty;

    /// <summary>Null means "publish the post's own text"; a value overrides it for this platform only.</summary>
    public string? PlatformCaption { get; set; }

    public string? PlatformTitle { get; set; }
    public string PlatformSettings { get; set; } = "{}";
    public string PublishStatus { get; set; } = "pending";
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? ExternalPostId { get; set; }
    public string? ExternalPostUrl { get; set; }
    public string? LastError { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A row of <c>content_hashtags</c>. Stored without the leading #.</summary>
public sealed class ContentHashtag
{
    public Guid Id { get; set; }
    public Guid AgencyId { get; set; }
    public Guid PostId { get; set; }
    public string Hashtag { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

public sealed class ContentPoll
{
    public Guid Id { get; set; }
    public Guid AgencyId { get; set; }
    public Guid PostId { get; set; }
    public string Question { get; set; } = string.Empty;
    public DateTimeOffset? EndsAt { get; set; }
}

public sealed class ContentPollOption
{
    public Guid Id { get; set; }
    public Guid AgencyId { get; set; }
    public Guid PollId { get; set; }
    public string OptionText { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

/// <summary>A file of the Media Library on a post, in the order the person put it, with what it is for there.</summary>
public sealed class ContentPostMedia
{
    public Guid Id { get; set; }
    public Guid AgencyId { get; set; }
    public Guid PostId { get; set; }
    public Guid MediaAssetId { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>The stored text of a <see cref="Enums.MediaRole"/>; worked out by <c>MediaRoles.Assign</c>, never sent by a client.</summary>
    public string MediaRole { get; set; } = "attachment";

    public string Caption { get; set; } = string.Empty;
}

/// <summary>A call-to-action button.</summary>
public sealed class ContentCta
{
    public Guid Id { get; set; }
    public Guid AgencyId { get; set; }
    public Guid PostId { get; set; }
    public string ButtonText { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

public sealed class ContentMention
{
    public Guid Id { get; set; }
    public Guid AgencyId { get; set; }
    public Guid PostId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string MentionedName { get; set; } = string.Empty;
    public string? PlatformUserId { get; set; }
}

public sealed class ContentLocation
{
    public Guid Id { get; set; }
    public Guid AgencyId { get; set; }
    public Guid PostId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? ExternalPlaceId { get; set; }
}
