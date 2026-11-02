using System.Globalization;
using System.Text;
using BacpacExplorer.Core.Exporting;
using BacpacExplorer.Core.Querying;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Microsoft.VisualBasic.FileIO;

namespace BacpacExplorer.Core.Tests;

public sealed class QueryResultExportTests
{
    [Fact]
    public void Csv_RoundTripsHeadersUnicodeQuotesAndMultilineValues()
    {
        var result = Result(["Name, alias", "Text", "Null", "Empty", "Number", "Date"],
            ["æøå 日本語", "A, \"quote\"\r\nand another line", null, "", 1234.56m, new DateTime(2026, 11, 2, 12, 34, 56)]);
        using var stream = new MemoryStream();
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("da-DK");
            new CsvQueryResultExporter().Write(result, stream);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }

        Assert.True(stream.CanWrite);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, stream.ToArray()[..3]);
        stream.Position = 0;
        using var parser = new TextFieldParser(stream, Encoding.UTF8);
        parser.SetDelimiters(",");
        parser.TrimWhiteSpace = false;
        Assert.Equal(result.Columns, parser.ReadFields());
        Assert.Equal(new[] { "æøå 日本語", "A, \"quote\"\r\nand another line", "", "", "1234.56", "2026-11-02T12:34:56.0000000" },
            parser.ReadFields());
        Assert.True(parser.EndOfData);
    }

    [Fact]
    public void Excel_PreservesTypesLiteralTextAndPrecision()
    {
        var date = new DateTime(2026, 11, 2, 12, 34, 56);
        var offsetDate = new DateTimeOffset(date, TimeSpan.FromHours(1));
        var result = Result(["Int", "Decimal", "Bool", "Date", "Null", "Formula text", "Quoted text", "Big integer", "Precise decimal", "Offset date", "Unicode"],
            [42, 1234.56m, true, date, null, "=1+1", "'literal", long.MaxValue, 1.234567890123456789m, offsetDate, "æøå 日本語"]);
        using var stream = new MemoryStream();
        new ExcelQueryResultExporter().Write(result, stream);

        Assert.True(stream.CanWrite);
        stream.Position = 0;
        using (var document = SpreadsheetDocument.Open(stream, false))
            Assert.Empty(new OpenXmlValidator().Validate(document));

        stream.Position = 0;
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);
        Assert.Equal(result.Columns, Enumerable.Range(1, result.Columns.Count).Select(column => sheet.Cell(1, column).GetString()));
        Assert.Equal(XLDataType.Number, sheet.Cell(2, 1).DataType);
        Assert.Equal(42, sheet.Cell(2, 1).GetValue<int>());
        Assert.Equal(1234.56m, sheet.Cell(2, 2).GetValue<decimal>());
        Assert.True(sheet.Cell(2, 3).GetBoolean());
        Assert.Equal(date, sheet.Cell(2, 4).GetDateTime());
        Assert.True(sheet.Cell(2, 5).IsEmpty());
        Assert.False(sheet.Cell(2, 6).HasFormula);
        Assert.Equal("=1+1", sheet.Cell(2, 6).GetString());
        Assert.Equal("'literal", sheet.Cell(2, 7).GetString());
        Assert.Equal("9223372036854775807", sheet.Cell(2, 8).GetString());
        Assert.Equal("1.234567890123456789", sheet.Cell(2, 9).GetString());
        Assert.Equal(offsetDate.ToString("O", CultureInfo.InvariantCulture), sheet.Cell(2, 10).GetString());
        Assert.Equal("æøå 日本語", sheet.Cell(2, 11).GetString());
        Assert.True(sheet.Cell(1, 1).Style.Font.Bold);
        Assert.Equal(1, sheet.SheetView.SplitRow);
        Assert.True(sheet.AutoFilter.IsEnabled);
        Assert.InRange(sheet.Column(1).Width, 10, 60);
    }

    [Fact]
    public void EmptyResults_ExportHeadersInBothFormats()
    {
        var result = Result(["Id", "Name"]);
        using var csv = new MemoryStream();
        new CsvQueryResultExporter().Write(result, csv);
        Assert.Equal("Id,Name\r\n", Encoding.UTF8.GetString(csv.ToArray()[3..]));

        using var excel = new MemoryStream();
        new ExcelQueryResultExporter().Write(result, excel);
        excel.Position = 0;
        using var workbook = new XLWorkbook(excel);
        Assert.Equal("Id", workbook.Worksheet(1).Cell(1, 1).GetString());
        Assert.Equal("Name", workbook.Worksheet(1).Cell(1, 2).GetString());
        Assert.Equal(1, workbook.Worksheet(1).LastRowUsed()!.RowNumber());
    }

    [Fact]
    public void LongText_ExcelFailsWithoutWritingAndCsvPreservesCompleteValue()
    {
        var text = new string('x', 32_768);
        var result = Result(["Text"], [text]);
        using var excel = new MemoryStream();
        var exception = Assert.Throws<InvalidOperationException>(() => new ExcelQueryResultExporter().Write(result, excel));
        Assert.Contains("CSV", exception.Message);
        Assert.Equal(0, excel.Length);

        using var csv = new MemoryStream();
        new CsvQueryResultExporter().Write(result, csv);
        Assert.Equal($"Text\r\n{text}\r\n", Encoding.UTF8.GetString(csv.ToArray()[3..]));
    }

    private static QueryResult Result(string[] columns, params object?[][] rows) =>
        new(columns, rows, false, TimeSpan.Zero, []);
}
