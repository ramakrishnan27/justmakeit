using Platform.Infrastructure.Data;

namespace Platform.Media.Models;

/// <summary>The kind of file, as the tabs of the page group them. Stored lower-case (<c>image</c>, <c>video</c>, <c>document</c>, <c>other</c>).</summary>
public enum MediaType
{
    Image,
    Video,
    Document,

    /// <summary>Everything else that is allowed: audio files today.</summary>
    Other
}

public static class MediaTypes
{
    /// <summary>Strict: the exact stored text, never a number and never another letter case.</summary>
    public static bool TryParse(string? text, out MediaType type)
    {
        foreach (var candidate in Enum.GetValues<MediaType>())
        {
            if (text == candidate.ToDb())
            {
                type = candidate;
                return true;
            }
        }

        type = default;
        return false;
    }
}
