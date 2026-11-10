using Platform.Content.Enums;

namespace Platform.Content.Features.Media;

/// <summary>
/// Works out what each file of a post is for, from the kind of content and the order the person put them in. Nobody chooses a role in
/// the editor: a carousel's files are its slides, a blog post's first picture is its cover, a video script's or a reel's video is its
/// video and its first picture the thumbnail, and everything else is an attachment. The rule always gives the table's own limit
/// (at most one cover and one thumbnail a post) and never the same file twice in one role.
/// </summary>
public static class MediaRoles
{
    /// <summary>The role of each file, in the order given. <paramref name="mediaTypes"/> are the library's kinds: image or video.</summary>
    public static MediaRole[] Assign(ContentType contentType, IReadOnlyList<string> mediaTypes)
    {
        var roles = new MediaRole[mediaTypes.Count];
        var coverTaken = false;
        var thumbnailTaken = false;

        for (var i = 0; i < mediaTypes.Count; i++)
        {
            var isImage = mediaTypes[i] == MediaPolicy.Image;
            var isVideo = mediaTypes[i] == MediaPolicy.Video;

            if (contentType == ContentType.Carousel)
            {
                roles[i] = MediaRole.Slide;
            }
            else if (contentType == ContentType.BlogPost && isImage && !coverTaken)
            {
                roles[i] = MediaRole.Cover;
                coverTaken = true;
            }
            else if (contentType is ContentType.VideoScript or ContentType.ShortReel && isVideo)
            {
                roles[i] = MediaRole.Video;
            }
            else if (contentType is ContentType.VideoScript or ContentType.ShortReel && isImage && !thumbnailTaken)
            {
                roles[i] = MediaRole.Thumbnail;
                thumbnailTaken = true;
            }
            else
            {
                roles[i] = MediaRole.Attachment;
            }
        }

        return roles;
    }
}
