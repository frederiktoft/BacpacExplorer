using System.Globalization;
using System.Text;
using BacpacExplorer.Core.Querying;

namespace BacpacExplorer.Core.Exporting;

public sealed class CsvQueryResultExporter : IQueryResultExporter
{
    public string FileExtension => ".csv";

    public void Write(QueryResult result, Stream destination)
    {
        // The BOM lets Excel recognize Unicode when opening the CSV directly.
        using var writer = new StreamWriter(destination, new UTF8Encoding(true), leaveOpen: true);
        writer.NewLine = "\r\n";
        WriteRow(writer, result.Columns);
        foreach (var row in result.Rows)
            WriteRow(writer, row.Select(FormatValue));
    }

    private static string FormatValue(object? value) => value switch
    {
        null or DBNull => string.Empty,
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    private static void WriteRow(TextWriter writer, IEnumerable<string> values)
    {
        writer.WriteLine(string.Join(",", values.Select(value =>
            value.IndexOfAny([',', '"', '\r', '\n']) >= 0
                ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
                : value)));
    }
}
