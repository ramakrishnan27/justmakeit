namespace Platform.Content.Entities;

/// <summary>A row of the list page: the post's own columns that the table shows, without the document or any child.</summary>
public sealed class PostSummary
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Visibility { get; set; } = string.Empty;
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A platform a listed post goes to, and where it is there.</summary>
public sealed class PostPlatformSummary
{
    public Guid PostId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string PublishStatus { get; set; } = string.Empty;
}

/// <summary>A tag on a listed post.</summary>
public sealed class PostTagSummary
{
    public Guid PostId { get; set; }
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}

/// <summary>One page of the list, with what the page needs around it: the platforms and tags of its posts, and the count per type.</summary>
public sealed class PostListResult
{
    public required IReadOnlyList<PostSummary> Items { get; init; }
    public required IReadOnlyList<PostPlatformSummary> Platforms { get; init; }
    public required IReadOnlyList<PostTagSummary> Tags { get; init; }

    /// <summary>Posts per content type (stored text), under every filter except the type.</summary>
    public required IReadOnlyDictionary<string, long> TypeCounts { get; init; }
}
