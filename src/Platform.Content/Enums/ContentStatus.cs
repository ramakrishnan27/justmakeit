namespace Platform.Content.Enums;

/// <summary>Where a post is in its life. The transitions between them are in <c>ContentStatusMachine</c>.</summary>
public enum ContentStatus
{
    [DbValue("draft")] Draft,
    [DbValue("scheduled")] Scheduled,
    [DbValue("publishing")] Publishing,
    [DbValue("published")] Published,
    [DbValue("failed")] Failed,
    [DbValue("archived")] Archived
}
