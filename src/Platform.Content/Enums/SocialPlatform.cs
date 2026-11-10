namespace Platform.Content.Enums;

/// <summary>
/// The platforms a post can go to: the values of <c>content_post_platforms.platform</c>, and the keys an
/// <c>ISocialPublisher</c> in Platform.Core announces itself with.
/// </summary>
public enum SocialPlatform
{
    [DbValue("facebook")] Facebook,
    [DbValue("instagram")] Instagram,
    [DbValue("x")] X,
    [DbValue("youtube")] YouTube,
    [DbValue("linkedin")] LinkedIn,
    [DbValue("tiktok")] TikTok,
    [DbValue("pinterest")] Pinterest,
    [DbValue("threads")] Threads
}
