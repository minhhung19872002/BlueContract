using BlueContract.Application.Common;
using ClosedXML.Excel;

namespace BlueContract.Infrastructure.Reporting;

internal sealed class ClosedXmlExcelExporter : IExcelExporter
{
    private static readonly TimeZoneInfo VietnamZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    public byte[] Export<T>(string sheetTitle, IReadOnlyList<ExcelColumn<T>> columns, IEnumerable<T> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SafeSheetName(sheetTitle));

        for (var c = 0; c < columns.Count; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F2");
            cell.Style.Alignment.WrapText = true;
            sheet.Column(c + 1).Width = columns[c].Width;
        }

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                var cell = sheet.Cell(r, c + 1);
                var value = columns[c].Value(row);
                SetValue(cell, value, columns[c].Format);
            }

            r++;
        }

        var range = sheet.Range(1, 1, Math.Max(r - 1, 1), Math.Max(columns.Count, 1));
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        sheet.SheetView.FreezeRows(1);
        if (r > 2)
        {
            range.SetAutoFilter();
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void SetValue(IXLCell cell, object? value, ExcelColumnFormat format)
    {
        switch (value)
        {
            case null:
                return;
            case DateTimeOffset dto:
                cell.Value = TimeZoneInfo.ConvertTime(dto, VietnamZone).DateTime;
                cell.Style.DateFormat.Format = format == ExcelColumnFormat.DateTime ? "dd/MM/yyyy HH:mm" : "dd/MM/yyyy";
                return;
            case DateTime dt:
                cell.Value = dt;
                cell.Style.DateFormat.Format = format == ExcelColumnFormat.DateTime ? "dd/MM/yyyy HH:mm" : "dd/MM/yyyy";
                return;
            case DateOnly d:
                cell.Value = d.ToDateTime(TimeOnly.MinValue);
                cell.Style.DateFormat.Format = "dd/MM/yyyy";
                return;
            case decimal or double or int or long or float:
                cell.Value = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                cell.Style.NumberFormat.Format = format == ExcelColumnFormat.Money ? "#,##0" : "#,##0.###";
                return;
            case bool b:
                cell.Value = b ? "Có" : "Không";
                return;
            default:
                cell.Value = value.ToString();
                return;
        }
    }

    private static string SafeSheetName(string name)
    {
        var invalid = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var clean = new string([.. name.Where(ch => !invalid.Contains(ch))]);
        return clean.Length > 31 ? clean[..31] : clean.Length == 0 ? "Sheet1" : clean;
    }
}
