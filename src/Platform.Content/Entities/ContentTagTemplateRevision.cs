namespace Platform.Content.Entities;

/// <summary>A row of <c>content_tags</c>: a workspace's tag, shown as a coloured chip.</summary>
public sealed class ContentTag
{
    public Guid Id { get; set; }
    public Guid AgencyId { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#6B7280";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A row of <c>content_templates</c>. The plain text is <see cref="BodyTemplate"/>; the editor's document and the hashtags
/// are in <see cref="TemplateData"/> (JSON text: <c>{"bodyDoc": {...}, "hashtags": [...]}</c>).
/// </summary>
public sealed class ContentTemplate
{
    public Guid Id { get; set; }
    public Guid AgencyId { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string TitleTemplate { get; set; } = string.Empty;
    public string BodyTemplate { get; set; } = string.Empty;
    public string TemplateData { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A row of <c>content_revisions</c> without its snapshot: who saved which version when. Written by a trigger, only read here.</summary>
public sealed class ContentRevision
{
    public Guid Id { get; set; }
    public int RevisionNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public int BodyLength { get; set; }
    public Guid? ChangedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
