using Platform.Helpers.Validation;

namespace Platform.Content.Features.Hashtags;

/// <summary>
/// What a hashtag is: text with no spaces, stored without the leading # and shown with one. The editor and the suggestions
/// may send it either way; it is stored once, in the spelling it was first written, and never twice that differ only in letter case.
/// </summary>
public static class HashtagNormalizer
{
    public const int MaxLength = 100;
    public const int MaxPerPost = 30;

    /// <summary>Strips the leading #s and the spaces around it. False when what is left is not a hashtag.</summary>
    public static bool TryNormalize(string? raw, out string hashtag)
    {
        hashtag = (raw ?? string.Empty).Trim().TrimStart('#').Trim();
        return hashtag.Length is >= 1 and <= MaxLength && hashtag.All(c => !char.IsWhiteSpace(c) && !char.IsControl(c) && c != '#');
    }

    /// <summary>
    /// The normalized hashtags in the order given, without repeats (letter case ignored, first spelling kept). Every one that is
    /// not a hashtag, and a list over <see cref="MaxPerPost"/>, becomes an error under <paramref name="field"/>.
    /// </summary>
    public static List<string> NormalizeAll(IEnumerable<string>? raw, ValidationErrors errors, string field)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in raw ?? [])
        {
            if (!TryNormalize(item, out var hashtag))
            {
                errors.Add(field, "A hashtag has no spaces and at most 100 characters.");
                continue;
            }

            if (seen.Add(hashtag))
            {
                result.Add(hashtag);
            }
        }

        if (result.Count > MaxPerPost)
        {
            errors.Add(field, $"A post can have at most {MaxPerPost} hashtags.");
        }

        return result;
    }
}
