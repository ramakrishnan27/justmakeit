namespace Platform.Content.Enums;

/// <summary>Where one platform's copy of a post is. A post is published when its targets are done and at least one succeeded.</summary>
public enum PublishStatus
{
    [DbValue("pending")] Pending,
    [DbValue("scheduled")] Scheduled,
    [DbValue("publishing")] Publishing,
    [DbValue("published")] Published,
    [DbValue("failed")] Failed
}
