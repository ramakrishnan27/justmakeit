using Platform.Content.Enums;

namespace Platform.Content.Features.Publishing;

/// <summary>What a platform allows in a post. Used to show a counter while writing and to refuse a post that would be rejected there.</summary>
/// <param name="CaptionLimit">The most characters of text (the hashtags count).</param>
/// <param name="TitleLimit">The most characters of a title, or null when the platform has no title.</param>
/// <param name="MaxHashtags">The most hashtags, or null when the platform sets none.</param>
public sealed record PlatformRule(SocialPlatform Platform, string Name, int CaptionLimit, int? TitleLimit, int? MaxHashtags);

/// <summary>
/// The limits of the eight platforms. They are the platforms' own published limits as of writing, kept here in one place: a
/// platform changing one is a change to this table, and a post that is over a limit is refused before scheduling instead of failing
/// at the platform later.
/// </summary>
public static class PlatformRules
{
    public static readonly IReadOnlyList<PlatformRule> All =
    [
        new(SocialPlatform.Facebook, "Facebook", 63206, null, null),
        new(SocialPlatform.Instagram, "Instagram", 2200, null, 30),
        new(SocialPlatform.X, "X", 280, null, null),
        new(SocialPlatform.YouTube, "YouTube", 5000, 100, null),
        new(SocialPlatform.LinkedIn, "LinkedIn", 3000, null, null),
        new(SocialPlatform.TikTok, "TikTok", 2200, null, null),
        new(SocialPlatform.Pinterest, "Pinterest", 500, 100, null),
        new(SocialPlatform.Threads, "Threads", 500, null, null)
    ];

    public static PlatformRule For(SocialPlatform platform) => All.Single(rule => rule.Platform == platform);

    /// <summary>
    /// The number of characters a platform will count for this text: the text, then the hashtags with their # one space apart,
    /// a blank line between the two.
    /// </summary>
    public static int CountedLength(string text, IReadOnlyCollection<string> hashtags)
    {
        if (hashtags.Count == 0)
        {
            return text.Length;
        }

        var tags = hashtags.Sum(tag => tag.Length + 1) + (hashtags.Count - 1);
        return text.Length + 2 + tags;
    }
}
