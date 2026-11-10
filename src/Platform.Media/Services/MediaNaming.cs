using System.Text.RegularExpressions;
using Platform.Helpers.Validation;

namespace Platform.Media.Services;

/// <summary>Where a file's bytes are kept: the key is made here, from ids and a fixed extension, and never from anything a person typed.</summary>
public static partial class StorageKeys
{
    // <organization id>/<file id>.<extension>, ids as 32 hex digits. The one shape the storage accepts, so a key can never climb out of its folder.
    // \z, not $: in .NET a $ also matches just before a final newline, which would let "….png\n" through.
    [GeneratedRegex(@"^[0-9a-f]{32}/[0-9a-f]{32}\.[a-z0-9]{2,5}\z")]
    private static partial Regex Shape();

    public static string For(Guid agencyId, Guid assetId, string extension) => $"{agencyId:N}/{assetId:N}.{extension}";

    public static bool IsValid(string? key) => key is not null && Shape().IsMatch(key);
}

/// <summary>The rules for the names of tags, folders and the text in the search box.</summary>
public static class MediaNaming
{
    public const int MaxTagsPerUpload = 10;
    public const int MaxTagLength = 50;
    public const int MaxFolderNameLength = 100;
    public const int MaxSearchLength = 100;

    /// <summary>
    /// Trims, drops empty ones and repeats (in any letter case), and reports a name that is too long or holds a control character.
    /// Returns the names to use, in the order given. The first spelling of a repeated name wins.
    /// </summary>
    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string>? raw, ValidationErrors errors)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in raw ?? [])
        {
            // A tag may be typed with its # (#summer): the mark is not part of the name.
            var name = (item ?? string.Empty).Trim().TrimStart('#').Trim();
            if (name.Length == 0)
            {
                continue;
            }

            if (name.Length > MaxTagLength || name.Any(char.IsControl))
            {
                errors.Add("tags", $"A tag is at most {MaxTagLength} characters on one line.");
                continue;
            }

            if (seen.Add(name))
            {
                names.Add(name);
            }
        }

        if (names.Count > MaxTagsPerUpload)
        {
            errors.Add("tags", $"Use at most {MaxTagsPerUpload} tags.");
        }

        return names;
    }

    /// <summary>The folder name, trimmed, or null (and an error) when it is empty, too long or has a control character.</summary>
    public static string? NormalizeFolderName(string? raw, ValidationErrors errors)
    {
        var name = (raw ?? string.Empty).Trim();
        if (name.Length is < 1 or > MaxFolderNameLength || name.Any(char.IsControl))
        {
            errors.Add("name", $"A folder name is 1 to {MaxFolderNameLength} characters on one line.");
            return null;
        }

        return name;
    }
}
