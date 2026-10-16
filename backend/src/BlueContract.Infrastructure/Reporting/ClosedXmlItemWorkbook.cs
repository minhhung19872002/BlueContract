using System.Globalization;
using BlueContract.Application.Catalog;
using BlueContract.Domain.Catalog;
using ClosedXML.Excel;

namespace BlueContract.Infrastructure.Reporting;

/// <summary>
/// Item import workbooks (spec §10 items 1–2). Reading is tolerant: the header row is located by its labels (so hospitals
/// may add a title or reorder columns) and numbers typed as text in Vietnamese format are parsed by the validator.
/// </summary>
internal sealed class ClosedXmlItemWorkbook : IItemWorkbook
{
    private const string DataSheet = "Danh mục";
    private const int HeaderRow = 3;

    public IReadOnlyList<ItemSheetRow> Read(byte[] xlsx, ItemImportKind kind)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(new MemoryStream(xlsx, writable: false));
        }
#pragma warning disable CA1031 // Any parsing failure of an uploaded file means "not a workbook".
        catch (Exception ex) when (ex is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            throw new InvalidDataException("Not a readable .xlsx workbook.", ex);
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault(w => w.Name == DataSheet) ?? workbook.Worksheet(1);
            var columns = ItemImportColumns.For(kind);
            var (headerRow, map) = FindHeader(sheet, columns);
            if (headerRow == 0)
            {
                throw new InvalidDataException("Header row not found.");
            }

            var rows = new List<ItemSheetRow>();
            var last = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
            for (var r = headerRow + 1; r <= last; r++)
            {
                var values = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var (key, column) in map)
                {
                    values[key] = CellText(sheet.Cell(r, column));
                }

                if (values.Values.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                rows.Add(new ItemSheetRow(r, values));
            }

            return rows;
        }
    }

    public byte[] CreateTemplate(ItemImportKind kind, IReadOnlyList<(string Code, string Name)> categories)
    {
        var columns = ItemImportColumns.For(kind);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(DataSheet);
        var title = kind == ItemImportKind.Medical
            ? "BIỂU MẪU IMPORT DANH MỤC THUỐC, VẬT TƯ Y TẾ, HÓA CHẤT, SINH PHẨM XÉT NGHIỆM"
            : "BIỂU MẪU IMPORT DANH MỤC HÀNG HÓA, TÀI SẢN, DỊCH VỤ";
        sheet.Cell(1, 1).Value = title;
        sheet.Range(1, 1, 1, columns.Count + 1).Merge().Style.Font.SetBold().Font.SetFontSize(13);
        sheet.Cell(2, 1).Value = "Các cột có dấu (*) là bắt buộc. Nhập dữ liệu từ dòng 4, không đổi tên tiêu đề cột. Số có thể nhập dạng 1.234.567 hoặc 1234567.";
        sheet.Range(2, 1, 2, columns.Count + 1).Merge().Style.Font.SetItalic().Font.SetFontColor(XLColor.DarkGray);

        sheet.Cell(HeaderRow, 1).Value = "STT";
        sheet.Column(1).Width = 6;
        for (var i = 0; i < columns.Count; i++)
        {
            var cell = sheet.Cell(HeaderRow, i + 2);
            cell.Value = columns[i].Header;
            sheet.Column(i + 2).Width = columns[i].Width;
            if (columns[i].Note is { } note)
            {
                cell.CreateComment().AddText(note);
            }

            if (columns[i].Key is ItemImportColumns.Quantity or ItemImportColumns.UnitPrice or ItemImportColumns.VatRate)
            {
                sheet.Range(HeaderRow + 1, i + 2, 2000, i + 2).Style.NumberFormat.Format = "#,##0.###";
            }
        }

        var header = sheet.Range(HeaderRow, 1, HeaderRow, columns.Count + 1);
        header.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#DCE6F2"))
            .Alignment.SetWrapText().Alignment.SetVertical(XLAlignmentVerticalValues.Center)
            .Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetInsideBorder(XLBorderStyleValues.Thin);
        sheet.Row(HeaderRow).Height = 36;
        sheet.SheetView.FreezeRows(HeaderRow);

        // Category list on its own sheet + dropdown validation on the category column.
        var list = workbook.Worksheets.Add("Mã hạng mục");
        list.Cell(1, 1).Value = "Mã hạng mục";
        list.Cell(1, 2).Value = "Tên hạng mục";
        list.Range(1, 1, 1, 2).Style.Font.SetBold();
        for (var i = 0; i < categories.Count; i++)
        {
            list.Cell(i + 2, 1).Value = categories[i].Code;
            list.Cell(i + 2, 2).Value = categories[i].Name;
        }

        list.Columns().AdjustToContents();
        var categoryIndex = columns.ToList().FindIndex(c => c.Key == ItemImportColumns.Category);
        if (categoryIndex >= 0 && categories.Count > 0)
        {
            sheet.Range(HeaderRow + 1, categoryIndex + 2, 2000, categoryIndex + 2).CreateDataValidation()
                .List(list.Range(2, 1, categories.Count + 1, 1), true);
        }

        var guide = workbook.Worksheets.Add("Hướng dẫn");
        guide.Cell(1, 1).Value = "Cột";
        guide.Cell(1, 2).Value = "Bắt buộc";
        guide.Cell(1, 3).Value = "Ghi chú";
        guide.Range(1, 1, 1, 3).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#DCE6F2"));
        for (var i = 0; i < columns.Count; i++)
        {
            guide.Cell(i + 2, 1).Value = columns[i].Header;
            guide.Cell(i + 2, 2).Value = columns[i].Required ? "Có" : string.Empty;
            guide.Cell(i + 2, 3).Value = columns[i].Note ?? string.Empty;
        }

        var tips = new[]
        {
            "Khóa liên kết với HIS là Số quyết định thầu + Mã số thuế NCC; mỗi dòng là một mặt hàng trúng thầu của một NCC.",
            "Nếu mặt hàng đã có trong quyết định (cùng NCC, cùng mã hàng hoặc mã key HIS), dòng import sẽ cập nhật số lượng và đơn giá.",
            "Hệ thống kiểm tra toàn bộ tệp trước; chỉ import khi không còn dòng lỗi (dòng lỗi được tô đỏ trên màn hình xem trước).",
        };
        for (var i = 0; i < tips.Length; i++)
        {
            guide.Cell(columns.Count + 4 + i, 1).Value = "• " + tips[i];
        }

        guide.Column(1).Width = 36;
        guide.Column(2).Width = 10;
        guide.Column(3).Width = 80;

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private static (int Row, Dictionary<string, int> Map) FindHeader(IXLWorksheet sheet, IReadOnlyList<ItemImportColumns.Column> columns)
    {
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (var r = 1; r <= Math.Min(15, sheet.LastRowUsed()?.RowNumber() ?? 0); r++)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var c = 1; c <= lastColumn; c++)
            {
                var text = Normalize(sheet.Cell(r, c).GetString());
                var column = columns.FirstOrDefault(x => Normalize(x.Header) == text);
                if (column is not null)
                {
                    map.TryAdd(column.Key, c);
                }
            }

            if (map.ContainsKey(ItemImportColumns.Code) && map.ContainsKey(ItemImportColumns.Name))
            {
                return (r, map);
            }
        }

        return (0, []);
    }

    private static string Normalize(string header) =>
        header.Replace("(*)", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim().ToUpperInvariant();

    private static string? CellText(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        var value = cell.Value;
        var text = value.Type switch
        {
            XLDataType.Number => value.GetNumber().ToString(CultureInfo.InvariantCulture),
            XLDataType.DateTime => value.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            XLDataType.Boolean => value.GetBoolean() ? "1" : "0",
            _ => cell.GetString(),
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
