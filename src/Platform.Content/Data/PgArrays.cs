using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;

namespace Platform.Content.Data;

// Dapper expands any list parameter into (@p1, @p2, ...) when the name appears in the SQL, which turns
// "= ANY(@ids)" and an array column in an INSERT into a row of separate values. These pass one whole PostgreSQL array instead.
// Classes, not structs: Dapper cannot emit parameter code for a struct that implements the interface.

/// <summary>A <c>text[]</c> parameter.</summary>
public sealed class PgTextArray(IEnumerable<string> values) : SqlMapper.ICustomQueryParameter
{
    private readonly string[] _values = [.. values];

    public void AddParameter(IDbCommand command, string name) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = _values });
}

/// <summary>A <c>uuid[]</c> parameter.</summary>
public sealed class PgUuidArray(IEnumerable<Guid> values) : SqlMapper.ICustomQueryParameter
{
    private readonly Guid[] _values = [.. values];

    public void AddParameter(IDbCommand command, string name) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = _values });
}
