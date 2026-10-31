using System.Data;
using System.Text.Json;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using Platform.Infrastructure.Data;

namespace Platform.IntegrationTests.Infrastructure;

/// <summary>Checks that need no database: the handler and parameter classes in isolation.</summary>
public sealed class DapperTypeHandlerTests
{
    private static readonly DateTimeOffsetTypeHandler Handler = new();

    [Fact]
    public void DateTimeOffset_IsWrittenAsUtcTimestampTz_WhateverTheOffset()
    {
        var local = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.FromHours(5.5));
        var parameter = new NpgsqlParameter();

        Handler.SetValue(parameter, local);

        Assert.Equal(NpgsqlDbType.TimestampTz, parameter.NpgsqlDbType);
        var written = Assert.IsType<DateTime>(parameter.Value);
        Assert.Equal(DateTimeKind.Utc, written.Kind);
        Assert.Equal(new DateTime(2026, 10, 31, 6, 30, 0, DateTimeKind.Utc), written);
    }

    [Fact]
    public void DateTimeOffset_ReadFromUtcDateTime_HasAZeroOffset()
    {
        var parsed = Handler.Parse(new DateTime(2026, 10, 31, 6, 30, 0, DateTimeKind.Utc));

        Assert.Equal(TimeSpan.Zero, parsed.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 6, 30, 0, TimeSpan.Zero), parsed);
    }

    [Fact]
    public void DateTimeOffset_ReadFromUnspecifiedDateTime_IsTakenAsUtc()
    {
        var parsed = Handler.Parse(new DateTime(2026, 10, 31, 6, 30, 0, DateTimeKind.Unspecified));

        Assert.Equal(new DateTimeOffset(2026, 10, 31, 6, 30, 0, TimeSpan.Zero), parsed);
    }

    [Fact]
    public void DateTimeOffset_ReadFromAnotherOffset_IsNormalizedToUtc()
    {
        var parsed = Handler.Parse(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.FromHours(2)));

        Assert.Equal(TimeSpan.Zero, parsed.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero), parsed);
    }

    [Fact]
    public void DateTimeOffset_ReadFromAString_IsRejected()
    {
        Assert.Throws<InvalidCastException>(() => Handler.Parse("2026-10-31"));
    }

    [Fact]
    public void Configure_CanRunRepeatedly()
    {
        DapperConfiguration.Configure();
        DapperConfiguration.Configure();

        Assert.True(DefaultTypeMap.MatchNamesWithUnderscores);
    }

    [Theory]
    [InlineData(ProbeStatus.Draft, "draft")]
    [InlineData(ProbeStatus.Scheduled, "scheduled")]
    [InlineData(ProbeStatus.WhatsApp, "whatsapp")]
    public void ToDb_LowerCasesTheEnumName(ProbeStatus status, string expected)
    {
        Assert.Equal(expected, status.ToDb());
    }

    [Fact]
    public void JsonbParameter_AddsAJsonbTypedParameterWithTheGivenName()
    {
        using var command = new NpgsqlCommand();

        new JsonbParameter("{\"a\":1}").AddParameter(command, "attributes");

        var parameter = Assert.IsType<NpgsqlParameter>(Assert.Single(command.Parameters));
        Assert.Equal("attributes", parameter.ParameterName);
        Assert.Equal(NpgsqlDbType.Jsonb, parameter.NpgsqlDbType);
        Assert.Equal("{\"a\":1}", parameter.Value);
    }

    [Fact]
    public void JsonbParameter_FromAnObject_SerializesWithCamelCaseNames()
    {
        using var command = new NpgsqlCommand();

        JsonbParameter.From(new { FirstName = "Ann", Tags = new[] { "vip" } }).AddParameter(command, "attributes");

        var json = (string)command.Parameters[0].Value!;
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Ann", document.RootElement.GetProperty("firstName").GetString());
        Assert.Equal("vip", document.RootElement.GetProperty("tags")[0].GetString());
    }

    [Fact]
    public void JsonbParameter_WithNull_WritesDbNull()
    {
        using var command = new NpgsqlCommand();

        new JsonbParameter(null).AddParameter(command, "attributes");

        Assert.Equal(DBNull.Value, command.Parameters[0].Value);
    }
}

public enum ProbeStatus
{
    Draft,
    Scheduled,
    WhatsApp
}

public sealed class ProbeRow
{
    public Guid Id { get; set; }
    public ProbeStatus Status { get; set; }
    public ProbeStatus? OptionalStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public string Attributes { get; set; } = string.Empty;
    public string? Extra { get; set; }
}

/// <summary>The same mappings against a real PostgreSQL, through Npgsql.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class DapperRoundTripTests(PostgresFixture postgres) : IAsyncLifetime
{
    private TestDatabase _db = null!;
    private NpgsqlConnection _connection = null!;

    public async Task InitializeAsync()
    {
        DapperConfiguration.Configure();
        _db = await postgres.CreateDatabaseAsync();
        _connection = await _db.OpenOwnerAsync();
        await _connection.ExecuteAsync(
            """
            CREATE TABLE handler_probe (
                id uuid PRIMARY KEY,
                status text NOT NULL,
                optional_status text NULL,
                created_at timestamptz NOT NULL,
                done_at timestamptz NULL,
                attributes jsonb NOT NULL,
                extra jsonb NULL)
            """);
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await _db.DisposeAsync();
    }

    private Task InsertAsync(Guid id, ProbeStatus status, ProbeStatus? optional, DateTimeOffset createdAt, DateTimeOffset? doneAt, JsonbParameter attributes, JsonbParameter extra) =>
        _connection.ExecuteAsync(
            "INSERT INTO handler_probe (id, status, optional_status, created_at, done_at, attributes, extra) " +
            "VALUES (@id, @status, @optionalStatus, @createdAt, @doneAt, @attributes, @extra)",
            new
            {
                id,
                status = status.ToDb(),
                optionalStatus = optional?.ToDb(),
                createdAt,
                doneAt,
                attributes,
                extra
            });

    private Task<ProbeRow> ReadAsync(Guid id) =>
        _connection.QuerySingleAsync<ProbeRow>(
            "SELECT id, status, optional_status, created_at, done_at, attributes, extra FROM handler_probe WHERE id = @id",
            new { id });

    [Fact]
    public async Task DateTimeOffset_WithANonUtcOffset_RoundTripsToTheSameInstant()
    {
        var id = Guid.NewGuid();
        var written = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.FromHours(5.5));

        await InsertAsync(id, ProbeStatus.Draft, null, written, null, JsonbParameter.From(new { }), new JsonbParameter(null));
        var row = await ReadAsync(id);

        Assert.Equal(written, row.CreatedAt);
        Assert.Equal(TimeSpan.Zero, row.CreatedAt.Offset);
    }

    [Fact]
    public async Task NullableDateTimeOffset_RoundTripsBothNullAndAValue()
    {
        var withNull = Guid.NewGuid();
        var withValue = Guid.NewGuid();
        var moment = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        await InsertAsync(withNull, ProbeStatus.Draft, null, moment, null, JsonbParameter.From(new { }), new JsonbParameter(null));
        await InsertAsync(withValue, ProbeStatus.Draft, null, moment, moment, JsonbParameter.From(new { }), new JsonbParameter(null));

        Assert.Null((await ReadAsync(withNull)).DoneAt);
        Assert.Equal(moment, (await ReadAsync(withValue)).DoneAt);
    }

    [Fact]
    public async Task Enums_AreStoredAsLowerCaseTextAndReadBackCaseInsensitively()
    {
        var id = Guid.NewGuid();

        await InsertAsync(id, ProbeStatus.WhatsApp, ProbeStatus.Scheduled, DateTimeOffset.UtcNow, null, JsonbParameter.From(new { }), new JsonbParameter(null));

        Assert.Equal("whatsapp", await _connection.ExecuteScalarAsync<string>("SELECT status FROM handler_probe WHERE id = @id", new { id }));
        var row = await ReadAsync(id);
        Assert.Equal(ProbeStatus.WhatsApp, row.Status);
        Assert.Equal(ProbeStatus.Scheduled, row.OptionalStatus);
    }

    [Fact]
    public async Task NullableEnum_RoundTripsNull()
    {
        var id = Guid.NewGuid();

        await InsertAsync(id, ProbeStatus.Draft, null, DateTimeOffset.UtcNow, null, JsonbParameter.From(new { }), new JsonbParameter(null));

        Assert.Null((await ReadAsync(id)).OptionalStatus);
    }

    [Fact]
    public async Task Jsonb_IsWrittenWithoutACastAndReadBackAsAJsonString()
    {
        var id = Guid.NewGuid();

        await InsertAsync(
            id, ProbeStatus.Draft, null, DateTimeOffset.UtcNow, null,
            JsonbParameter.From(new { plan = "pro", seats = 3 }), JsonbParameter.From(new[] { "a", "b" }));
        var row = await ReadAsync(id);

        using var attributes = JsonDocument.Parse(row.Attributes);
        Assert.Equal("pro", attributes.RootElement.GetProperty("plan").GetString());
        Assert.Equal(3, attributes.RootElement.GetProperty("seats").GetInt32());
        using var extra = JsonDocument.Parse(row.Extra!);
        Assert.Equal(2, extra.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Jsonb_NullGoesToANullableColumnAsSqlNull()
    {
        var id = Guid.NewGuid();

        await InsertAsync(id, ProbeStatus.Draft, null, DateTimeOffset.UtcNow, null, JsonbParameter.From(new { }), new JsonbParameter(null));

        Assert.Null((await ReadAsync(id)).Extra);
    }

    [Fact]
    public async Task SnakeCaseColumns_MapToPascalCaseProperties()
    {
        var id = Guid.NewGuid();
        var moment = new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);

        await InsertAsync(id, ProbeStatus.Draft, ProbeStatus.Draft, moment, moment, JsonbParameter.From(new { }), new JsonbParameter(null));
        var row = await ReadAsync(id);

        Assert.Equal(moment, row.CreatedAt);
        Assert.Equal(ProbeStatus.Draft, row.OptionalStatus);
    }
}
