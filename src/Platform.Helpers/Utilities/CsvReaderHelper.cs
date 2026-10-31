using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Platform.Helpers.Exceptions;

namespace Platform.Helpers.Utilities;

/// <summary>One data row of a CSV file, keyed by lower-cased header name.</summary>
/// <param name="RowNumber">1-based record number in the file; the header is row 1, so the first data row is 2.</param>
public sealed record CsvRow(int RowNumber, IReadOnlyDictionary<string, string> Values)
{
    public string Get(string column) => Values.TryGetValue(column, out var value) ? value : string.Empty;
}

public static class CsvReaderHelper
{
    private static readonly string[] SupportedDelimiters = [",", ";", "\t", "|"];

    /// <summary>
    /// Streams the data rows of a CSV file one at a time, so large files are never held in memory.
    /// Header names are trimmed and lower-cased, values are trimmed, blank lines are skipped, and the
    /// delimiter (comma, semicolon, tab or pipe) is detected from the header line. Malformed input and
    /// files with more than <paramref name="maxRows"/> data rows raise <see cref="BadRequestException"/>.
    /// </summary>
    public static async IAsyncEnumerable<CsvRow> ReadRowsAsync(
        Stream stream,
        int maxRows = int.MaxValue,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            DetectDelimiter = true,
            DetectDelimiterValues = SupportedDelimiters,
            MissingFieldFound = null,
            HeaderValidated = null
        };
        using var csv = new CsvReader(reader, configuration);

        string[] headers;
        try
        {
            if (!await csv.ReadAsync() || !csv.ReadHeader() || csv.HeaderRecord is null)
            {
                yield break;
            }

            headers = csv.HeaderRecord.Select(h => h.Trim().ToLowerInvariant()).ToArray();
        }
        catch (CsvHelperException)
        {
            throw new BadRequestException("The CSV file is malformed.", "csv_malformed");
        }

        var dataRows = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Dictionary<string, string>? values = null;
            try
            {
                // Bad quoting can surface while a field is read, not only while the row is parsed,
                // so both steps sit inside the same guard.
                if (await csv.ReadAsync())
                {
                    values = ReadFields(csv, headers);
                }
            }
            catch (CsvHelperException)
            {
                throw new BadRequestException($"The CSV file is malformed near row {csv.Parser.Row}.", "csv_malformed");
            }

            if (values is null)
            {
                yield break;
            }

            if (++dataRows > maxRows)
            {
                throw new BadRequestException($"The CSV file has more than {maxRows} rows.", "csv_too_many_rows");
            }

            yield return new CsvRow(csv.Parser.Row, values);
        }
    }

    private static Dictionary<string, string> ReadFields(CsvReader csv, string[] headers)
    {
        var values = new Dictionary<string, string>(headers.Length, StringComparer.Ordinal);
        for (var i = 0; i < headers.Length; i++)
        {
            if (headers[i].Length > 0)
            {
                values[headers[i]] = i < csv.Parser.Count ? csv.GetField(i) ?? string.Empty : string.Empty;
            }
        }

        return values;
    }
}
