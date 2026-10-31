using Dapper;

namespace Platform.Infrastructure.Data;

public static class DapperConfiguration
{
    /// <summary>Sets Dapper's process-wide mapping rules. Safe to call more than once.</summary>
    public static void Configure()
    {
        // snake_case columns map to PascalCase properties. This applies to settable properties only;
        // Dapper does not apply it to constructor parameters, so row models are classes, not positional records.
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        // DateTimeOffset is in Dapper's built-in type map, which is consulted before custom handlers.
        // Remove it so the handler is used when writing as well as reading.
        SqlMapper.RemoveTypeMap(typeof(DateTimeOffset));
        SqlMapper.RemoveTypeMap(typeof(DateTimeOffset?));
        SqlMapper.AddTypeHandler(new DateTimeOffsetTypeHandler());
    }
}
