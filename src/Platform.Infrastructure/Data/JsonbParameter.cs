using System.Data;
using System.Text.Json;
using Dapper;
using Npgsql;
using NpgsqlTypes;

namespace Platform.Infrastructure.Data;

/// <summary>
/// Passes JSON text to a <c>jsonb</c> column without a <c>::jsonb</c> cast in the SQL:
/// <c>new { attributes = JsonbParameter.From(attributes) }</c>. Reading needs nothing special;
/// Npgsql returns <c>jsonb</c> as a JSON string.
/// </summary>
/// <remarks>A class, not a struct: Dapper cannot emit parameter code for a struct that implements the interface.</remarks>
public sealed class JsonbParameter(string? json) : SqlMapper.ICustomQueryParameter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static JsonbParameter From<T>(T value) => new(JsonSerializer.Serialize(value, SerializerOptions));

    public void AddParameter(IDbCommand command, string name)
    {
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = (object?)json ?? DBNull.Value
        });
    }
}
