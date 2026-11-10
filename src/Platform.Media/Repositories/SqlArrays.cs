using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;

namespace Platform.Media.Repositories;

// Dapper expands any list parameter into (@p1, @p2, ...) when the name appears in the SQL, which turns "= ANY(@ids)" into a row of
// separate values. This passes one whole PostgreSQL array instead. A class, not a struct: Dapper cannot emit parameter code for a
// struct that implements the interface.

/// <summary>A <c>uuid[]</c> parameter.</summary>
internal sealed class UuidArray(IEnumerable<Guid> values) : SqlMapper.ICustomQueryParameter
{
    private readonly Guid[] _values = [.. values];

    public void AddParameter(IDbCommand command, string name) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = _values });
}
