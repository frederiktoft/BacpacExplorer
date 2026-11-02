using System.Globalization;
using BacpacExplorer.Core.Localization;
using BacpacExplorer.Core.Querying;
using ClosedXML.Excel;

namespace BacpacExplorer.Core.Exporting;

public sealed class ExcelQueryResultExporter : IQueryResultExporter
{
    public string FileExtension => ".xlsx";

    public void Write(QueryResult result, Stream destination)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Results");
        for (var column = 0; column < result.Columns.Count; column++)
            SetText(sheet.Cell(1, column + 1), result.Columns[column]);

        for (var row = 0; row < result.Rows.Count; row++)
            for (var column = 0; column < result.Columns.Count; column++)
                SetValue(sheet.Cell(row + 2, column + 1), result.Rows[row][column]);

        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        if (result.Columns.Count > 0)
        {
            sheet.Range(1, 1, result.Rows.Count + 1, result.Columns.Count).SetAutoFilter();
            foreach (var column in sheet.Columns(1, result.Columns.Count))
                column.AdjustToContents(1, result.Rows.Count + 1, 10, 60);
        }
        workbook.SaveAs(destination);
    }

    private static void SetValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null or DBNull:
                return;
            case string text:
                SetText(cell, text);
                return;
            case DateTime date when date >= new DateTime(1900, 1, 1):
                cell.Value = date;
                cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss.000";
                return;
            case DateTime date:
                SetText(cell, date.ToString("O", CultureInfo.InvariantCulture));
                return;
            case DateTimeOffset date:
                SetText(cell, date.ToString("O", CultureInfo.InvariantCulture));
                return;
            case TimeSpan time:
                SetText(cell, time.ToString("c", CultureInfo.InvariantCulture));
                return;
            case long or ulong or decimal:
                var number = ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
                // Excel numbers have 15 significant digits. Keep more precise values as text.
                if (number.TrimStart('-').Replace(".", string.Empty, StringComparison.Ordinal)
                    .TrimStart('0').TrimEnd('0').Length > 15)
                {
                    SetText(cell, number);
                    return;
                }
                break;
        }

        cell.Value = XLCellValue.FromObject(value, CultureInfo.InvariantCulture);
    }

    private static void SetText(IXLCell cell, string text)
    {
        if (text.Length > 32_767)
            throw new InvalidOperationException(CoreText.Get("ExcelCellTextTooLong"));

        // ClosedXML consumes one leading apostrophe as Excel's quote prefix.
        cell.Value = text.StartsWith('\'') ? "'" + text : text;
    }
}
