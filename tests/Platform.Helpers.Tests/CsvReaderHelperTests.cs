using System.Text;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Utilities;

namespace Platform.Helpers.Tests;

public sealed class CsvReaderHelperTests
{
    private static MemoryStream Csv(string content) => new(Encoding.UTF8.GetBytes(content));

    private static async Task<List<CsvRow>> ReadAllAsync(string content, int maxRows = int.MaxValue)
    {
        var rows = new List<CsvRow>();
        await foreach (var row in CsvReaderHelper.ReadRowsAsync(Csv(content), maxRows))
        {
            rows.Add(row);
        }

        return rows;
    }

    [Fact]
    public async Task ReadRowsAsync_ReadsRowsKeyedByLowerCasedTrimmedHeaders()
    {
        var rows = await ReadAllAsync("Email, First_Name \na@example.com,Ann\nb@example.com,Bob\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal("a@example.com", rows[0].Get("email"));
        Assert.Equal("Ann", rows[0].Get("first_name"));
        Assert.Equal("Bob", rows[1].Get("first_name"));
    }

    [Fact]
    public async Task ReadRowsAsync_RowNumbersCountTheHeaderAsRowOne()
    {
        var rows = await ReadAllAsync("email\na@example.com\nb@example.com\n");

        Assert.Equal([2, 3], rows.Select(r => r.RowNumber).ToArray());
    }

    [Fact]
    public async Task ReadRowsAsync_TrimsValues()
    {
        var rows = await ReadAllAsync("email,name\n  a@example.com  ,  Ann  \n");

        Assert.Equal("a@example.com", rows[0].Get("email"));
        Assert.Equal("Ann", rows[0].Get("name"));
    }

    [Fact]
    public async Task ReadRowsAsync_HandlesQuotedFieldsWithCommasQuotesAndNewlines()
    {
        var rows = await ReadAllAsync("email,note\na@example.com,\"Smith, \"\"Al\"\"\nline two\"\n");

        Assert.Single(rows);
        Assert.Equal("Smith, \"Al\"\nline two", rows[0].Get("note"));
    }

    [Fact]
    public async Task ReadRowsAsync_SkipsBlankLines()
    {
        var rows = await ReadAllAsync("email\na@example.com\n\n\nb@example.com\n");

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task ReadRowsAsync_ShortRowsGetEmptyStringsForMissingColumns()
    {
        var rows = await ReadAllAsync("email,first_name,last_name\na@example.com,Ann\n");

        Assert.Equal("Ann", rows[0].Get("first_name"));
        Assert.Equal(string.Empty, rows[0].Get("last_name"));
    }

    [Fact]
    public async Task ReadRowsAsync_UnknownColumnReturnsEmptyString()
    {
        var rows = await ReadAllAsync("email\na@example.com\n");

        Assert.Equal(string.Empty, rows[0].Get("phone"));
    }

    [Theory]
    [InlineData("email;first_name\na@example.com;Ann\n")]
    [InlineData("email\tfirst_name\na@example.com\tAnn\n")]
    [InlineData("email|first_name\na@example.com|Ann\n")]
    public async Task ReadRowsAsync_DetectsTheDelimiter(string content)
    {
        var rows = await ReadAllAsync(content);

        Assert.Equal("a@example.com", rows[0].Get("email"));
        Assert.Equal("Ann", rows[0].Get("first_name"));
    }

    [Fact]
    public async Task ReadRowsAsync_AcceptsAUtf8ByteOrderMark()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("email\na@example.com\n")).ToArray();
        var rows = new List<CsvRow>();

        await foreach (var row in CsvReaderHelper.ReadRowsAsync(new MemoryStream(bytes)))
        {
            rows.Add(row);
        }

        Assert.Equal("a@example.com", rows[0].Get("email"));
    }

    [Fact]
    public async Task ReadRowsAsync_EmptyFile_YieldsNothing()
    {
        Assert.Empty(await ReadAllAsync(string.Empty));
    }

    [Fact]
    public async Task ReadRowsAsync_HeaderOnly_YieldsNothing()
    {
        Assert.Empty(await ReadAllAsync("email,first_name\n"));
    }

    [Fact]
    public async Task ReadRowsAsync_AtTheRowLimit_Succeeds()
    {
        var rows = await ReadAllAsync("email\na@example.com\nb@example.com\n", maxRows: 2);

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task ReadRowsAsync_OverTheRowLimit_ThrowsBadRequest()
    {
        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => ReadAllAsync("email\na@example.com\nb@example.com\nc@example.com\n", maxRows: 2));

        Assert.Equal("csv_too_many_rows", exception.Code);
    }

    [Fact]
    public async Task ReadRowsAsync_UnterminatedQuote_ThrowsBadRequest()
    {
        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => ReadAllAsync("email,note\na@example.com,\"never closed\nb@example.com,x\n"));

        Assert.Equal("csv_malformed", exception.Code);
    }

    [Fact]
    public async Task ReadRowsAsync_LeavesTheStreamOpen()
    {
        using var stream = Csv("email\na@example.com\n");

        await foreach (var _ in CsvReaderHelper.ReadRowsAsync(stream))
        {
        }

        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task ReadRowsAsync_StopsWhenCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in CsvReaderHelper.ReadRowsAsync(Csv("email\na@example.com\n"), cancellationToken: cancellation.Token))
            {
            }
        });
    }
}
