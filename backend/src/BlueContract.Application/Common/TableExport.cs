using System.Globalization;
using System.Text.Json;

namespace BlueContract.Application.Common;

/// <summary>One column of an exported table: header, cell format (text|number|money|date|datetime) and width.</summary>
public sealed record TableExportColumn(string Header, string? Format, double? Width);

/// <summary>A table as the web app shows it: rows of cells (strings, numbers, null; dates as dd/MM/yyyy [HH:mm]).</summary>
public sealed record TableExportRequest(string Title, string? FileName, IReadOnlyList<TableExportColumn> Columns, IReadOnlyList<IReadOnlyList<JsonElement>> Rows);

/// <summary>
/// "Xuất Excel" for any table of the web app (spec §9.3): the page sends what it displays, this lays it out as a typed
/// workbook. No data is read here, so the export never shows more than the user already sees.
/// </summary>
public sealed class TableExportService(IExcelExporter excel)
{
    public const int MaxColumns = 60;
    private const int MaxCellLength = 4000;

    public Result<(byte[] Content, string FileName)> Export(TableExportRequest r)
    {
        if (r.Columns is not { Count: > 0 } || r.Columns.Count > MaxColumns)
        {
            return Error.Validation("EXPORT_COLUMNS_INVALID");
        }

        if (r.Rows is null || r.Rows.Count > PageQuery.MaxExportRows)
        {
            return Error.Validation("EXPORT_TOO_MANY_ROWS");
        }

        var columns = r.Columns.Select((c, i) =>
        {
            var format = Format(c.Format);
            return new ExcelColumn<IReadOnlyList<JsonElement>>(
                Trim(c.Header) ?? $"Cột {i + 1}",
                row => i < row.Count ? Value(row[i], format) : null,
                format,
                Math.Clamp(c.Width ?? DefaultWidth(format), 6, 80));
        }).ToList();
        var content = excel.Export(Trim(r.Title) ?? "Danh sách", columns, r.Rows);
        return (content, FileName(r.FileName ?? r.Title));
    }

    private static ExcelColumnFormat Format(string? format) => format?.ToLowerInvariant() switch
    {
        "number" => ExcelColumnFormat.Number,
        "money" => ExcelColumnFormat.Money,
        "date" => ExcelColumnFormat.Date,
        "datetime" => ExcelColumnFormat.DateTime,
        _ => ExcelColumnFormat.Text,
    };

    private static double DefaultWidth(ExcelColumnFormat format) => format switch
    {
        ExcelColumnFormat.Date => 12,
        ExcelColumnFormat.DateTime => 17,
        ExcelColumnFormat.Number => 12,
        ExcelColumnFormat.Money => 16,
        _ => 24,
    };

    /// <summary>Cell value typed for its column: numbers stay numbers, "dd/MM/yyyy [HH:mm]" becomes a date.</summary>
    private static object? Value(JsonElement cell, ExcelColumnFormat format)
    {
        switch (cell.ValueKind)
        {
            case JsonValueKind.Number:
                return cell.GetDecimal();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.String:
                var text = cell.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                if (format is ExcelColumnFormat.Date or ExcelColumnFormat.DateTime
                    && DateTime.TryParseExact(text.Trim(), ["dd/MM/yyyy HH:mm", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    return date;
                }

                if (format is ExcelColumnFormat.Number or ExcelColumnFormat.Money
                    && decimal.TryParse(text.Replace(".", string.Empty, StringComparison.Ordinal).Replace("₫", string.Empty, StringComparison.Ordinal).Replace(',', '.').Trim(),
                        NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    return number;
                }

                return Trim(text);
            default:
                return null;
        }
    }

    private static string? Trim(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Length > MaxCellLength ? text[..MaxCellLength] : text.Trim();

    /// <summary>"Phiếu đề xuất" → "Phieu-de-xuat.xlsx" (ASCII, no path characters).</summary>
    private static string FileName(string? name)
    {
        var plain = new string((name ?? "danh-sach").Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .Select(ch => ch is 'đ' ? 'd' : ch is 'Đ' ? 'D' : char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')
            .ToArray()).Trim('-');
        while (plain.Contains("--", StringComparison.Ordinal))
        {
            plain = plain.Replace("--", "-", StringComparison.Ordinal);
        }

        return (plain.Length == 0 ? "danh-sach" : plain[..Math.Min(plain.Length, 80)]) + ".xlsx";
    }
}
