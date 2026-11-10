namespace Platform.Content.Features.Hashtags;

/// <summary>The colour a tag gets when it is created by name: one of a few that read well on the page, picked from the name so the same name always looks the same.</summary>
public static class TagColors
{
    public static readonly IReadOnlyList<string> Palette = ["#1F7A55", "#7C3AED", "#D97706", "#2563EB", "#DB2777", "#0F766E"];

    public static string For(string name)
    {
        var sum = 0;
        foreach (var c in name.ToLowerInvariant())
        {
            sum = unchecked((sum * 31) + c);
        }

        return Palette[(int)((uint)sum % (uint)Palette.Count)];
    }

    /// <summary>The shape the database accepts: # and six hex digits.</summary>
    public static bool IsValid(string? color) =>
        color is { Length: 7 } && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit);
}
