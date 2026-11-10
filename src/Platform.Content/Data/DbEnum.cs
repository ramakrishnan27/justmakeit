using System.Reflection;
using Platform.Content.Enums;

namespace Platform.Content.Data;

/// <summary>
/// Converts between the content enums and the snake_case text of the database and the API, both ways, from the
/// <see cref="DbValueAttribute"/> on each member. A member without one is a programming error and fails at first use.
/// </summary>
public static class DbEnum<TEnum> where TEnum : struct, Enum
{
    private static readonly Dictionary<TEnum, string> NameOf = [];
    private static readonly Dictionary<string, TEnum> ValueOf = new(StringComparer.Ordinal);

    static DbEnum()
    {
        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var text = field.GetCustomAttribute<DbValueAttribute>()?.Value
                       ?? throw new InvalidOperationException($"{typeof(TEnum).Name}.{field.Name} has no [DbValue].");
            var value = (TEnum)field.GetValue(null)!;
            NameOf[value] = text;
            ValueOf[text] = value;
        }

        // Here, not in an initializer: initializers run before this constructor body, when the dictionaries are still empty.
        All = [.. Enum.GetValues<TEnum>().Select(value => NameOf[value])];
    }

    /// <summary>Every stored value, in the order the enum declares them.</summary>
    public static IReadOnlyList<string> All { get; }

    public static string ToDb(TEnum value) => NameOf[value];

    /// <summary>Strict: the exact stored text, never a number, never another letter case.</summary>
    public static bool TryParse(string? text, out TEnum value)
    {
        if (text is not null && ValueOf.TryGetValue(text, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>The value for text that came from the database, where an unknown value is a bug rather than a bad request.</summary>
    public static TEnum Parse(string text) =>
        TryParse(text, out var value) ? value : throw new InvalidOperationException($"'{text}' is not a {typeof(TEnum).Name}.");
}

public static class DbEnumExtensions
{
    /// <summary>The stored and wire text of an enum member: <c>ContentType.SocialPost.ToWire()</c> is <c>social_post</c>.</summary>
    public static string ToWire<TEnum>(this TEnum value) where TEnum : struct, Enum => DbEnum<TEnum>.ToDb(value);
}
