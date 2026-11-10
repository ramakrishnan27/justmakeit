namespace Platform.Content.Entities;

/// <summary>
/// A row of <c>content_posts</c>. Dapper fills settable properties (underscore names match by name), so this is a class, not a
/// positional record. The status, type and visibility are the stored text (<c>social_post</c>, <c>draft</c>, ...): convert with
/// <c>DbEnum</c> where the code needs the enum. <c>Settings</c> and <c>BodyDoc</c> are JSON text (jsonb).
/// </summary>
public sealed class ContentPost
{
    public Guid Id { get; set; }
    public Guid AgencyId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? BrandId { get; set; }
    public Guid? CampaignId { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;

    /// <summary>The plain-text projection of <see cref="BodyDoc"/>, written by the service and never taken from a client.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>The editor's document as JSON text, or null for a post that has no text yet.</summary>
    public string? BodyDoc { get; set; }

    public string Excerpt { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Visibility { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "en";
    public string Timezone { get; set; } = "UTC";
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
    public string? SeoSlug { get; set; }
    public string[] SeoKeywords { get; set; } = [];
    public string Settings { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>A copy to edit without touching the original. Shallow: the arrays and text are replaced, never changed in place.</summary>
    public ContentPost Clone() => (ContentPost)MemberwiseClone();
}
