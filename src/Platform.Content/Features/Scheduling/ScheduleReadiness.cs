using Platform.Content.Data;
using Platform.Content.Entities;
using Platform.Content.Enums;
using Platform.Content.Features.Publishing;
using Platform.Helpers.Validation;

namespace Platform.Content.Features.Scheduling;

/// <summary>
/// Whether a post is complete enough to go out: it has something to say, it has somewhere to go, and what it says fits each
/// platform. Saving a draft asks for none of this; scheduling, publishing now and editing a scheduled post do.
/// </summary>
public static class ScheduleReadiness
{
    public static ValidationErrors Check(ContentPostAggregate post)
    {
        var errors = new ValidationErrors();

        if (post.Post.Body.Length == 0)
        {
            errors.Add("bodyDoc", "Write some text before scheduling.");
        }

        if (DbEnum<ContentType>.TryParse(post.Post.ContentType, out var type)
            && type is ContentType.BlogPost or ContentType.VideoScript
            && post.Post.Title.Length == 0)
        {
            errors.Add("title", "Give it a title before scheduling.");
        }

        if (post.Platforms.Count == 0)
        {
            errors.Add("platforms", "Choose at least one platform.");
        }

        var hashtags = post.Hashtags.Select(h => h.Hashtag).ToList();
        foreach (var target in post.Platforms)
        {
            if (!DbEnum<SocialPlatform>.TryParse(target.Platform, out var platform))
            {
                continue;
            }

            var rule = PlatformRules.For(platform);
            var text = target.PlatformCaption ?? post.Post.Body;
            var length = PlatformRules.CountedLength(text, hashtags);
            if (length > rule.CaptionLimit)
            {
                errors.Add("platforms", $"{rule.Name}: the text is {length} characters; the limit is {rule.CaptionLimit}.");
            }

            var title = target.PlatformTitle ?? (post.Post.Title.Length > 0 ? post.Post.Title : null);
            if (rule.TitleLimit is { } titleLimit && title is not null && title.Length > titleLimit)
            {
                errors.Add("platforms", $"{rule.Name}: the title is {title.Length} characters; the limit is {titleLimit}.");
            }

            if (rule.MaxHashtags is { } maxHashtags && hashtags.Count > maxHashtags)
            {
                errors.Add("hashtags", $"{rule.Name}: at most {maxHashtags} hashtags.");
            }
        }

        return errors;
    }
}
