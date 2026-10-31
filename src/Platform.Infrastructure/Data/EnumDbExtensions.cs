namespace Platform.Infrastructure.Data;

/// <summary>
/// Status and type columns are text holding the lower-cased C# enum name. Dapper reads such text into
/// enums by itself, ignoring case. For writing it never calls a custom type handler for enums, so
/// pass enum parameters through <see cref="ToDb{TEnum}"/>: <c>new { status = status.ToDb() }</c>.
/// </summary>
public static class EnumDbExtensions
{
    public static string ToDb<TEnum>(this TEnum value) where TEnum : struct, Enum =>
        value.ToString().ToLowerInvariant();
}
