using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;

namespace Platform.Infrastructure.Data;

/// <summary>
/// Npgsql only writes <see cref="DateTimeOffset"/> values with a zero offset, so values are sent as UTC
/// <c>timestamptz</c>. Values read back always have a zero offset.
/// </summary>
public sealed class DateTimeOffsetTypeHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
    {
        parameter.Value = value.UtcDateTime;
        if (parameter is NpgsqlParameter npgsqlParameter)
        {
            npgsqlParameter.NpgsqlDbType = NpgsqlDbType.TimestampTz;
        }
    }

    public override DateTimeOffset Parse(object value) => value switch
    {
        DateTimeOffset offset => offset.ToUniversalTime(),
        DateTime dateTime => new DateTimeOffset(dateTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
            : dateTime.ToUniversalTime()),
        _ => throw new InvalidCastException($"Cannot convert {value.GetType().Name} to DateTimeOffset.")
    };
}
