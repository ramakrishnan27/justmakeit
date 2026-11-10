namespace Platform.Content.Enums;

/// <summary>What a file is for on a post: the CHECK constraint <c>ck_content_post_media_role</c> of <c>014_content_studio.sql</c>.</summary>
public enum MediaRole
{
    [DbValue("attachment")] Attachment,
    [DbValue("cover")] Cover,
    [DbValue("thumbnail")] Thumbnail,
    [DbValue("slide")] Slide,
    [DbValue("video")] Video
}
