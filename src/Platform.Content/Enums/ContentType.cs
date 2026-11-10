namespace Platform.Content.Enums;

/// <summary>What a piece of content is. The tabs of the Content Studio list and the choice at the top of the Create page.</summary>
public enum ContentType
{
    [DbValue("social_post")] SocialPost,
    [DbValue("blog_post")] BlogPost,
    [DbValue("video_script")] VideoScript,
    [DbValue("carousel")] Carousel,
    [DbValue("short_reel")] ShortReel
}
