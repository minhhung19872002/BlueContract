using System.Net;
using System.Net.Http.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using ClosedXML.Excel;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Guards;

/// <summary>
/// AUDIT-1 §12.2 / spec §9.3 "mọi danh sách có Xuất Excel": any table of the web app is exported as shown — the page
/// sends the columns and the rows it displays (labels already in Vietnamese), the API only lays out the workbook with
/// real numbers and dates. It reads no data, so it cannot widen what the user sees.
/// </summary>
[Collection(ApiCollection.Name)]
public class TableExportTests(ApiFactory factory)
{
    private const string Url = "/api/export/table";

    [Fact]
    public async Task A_table_is_exported_with_typed_cells()
    {
        using var client = await factory.LoginAsync("khoanoi");
        using var response = await client.PostAsJsonAsync(Url, new
        {
            title = "Phiếu đề xuất",
            fileName = "phieu-de-xuat",
            columns = new object[]
            {
                new { header = "Số phiếu", format = "text" },
                new { header = "Ngày gửi", format = "date" },
                new { header = "Gửi lúc", format = "datetime" },
                new { header = "Số mặt hàng", format = "number" },
                new { header = "Giá trị", format = "money" },
                new { header = "Trạng thái", format = "text" },
            },
            rows = new object?[][]
            {
                ["DX-2026-0001", "17/10/2026", "17/10/2026 08:30", 12, 1234567.5, "Đã gửi"],
                ["DX-2026-0002", null, "", 3, 0, "Nháp"],
            },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("phieu-de-xuat.xlsx");

        using var book = new XLWorkbook(await response.Content.ReadAsStreamAsync());
        var sheet = book.Worksheets.First();
        var header = sheet.FirstRowUsed()!;
        header.Cell(1).GetString().ShouldBe("Số phiếu");
        var first = header.RowBelow();
        first.Cell(1).GetString().ShouldBe("DX-2026-0001");
        first.Cell(2).DataType.ShouldBe(XLDataType.DateTime);
        first.Cell(2).GetDateTime().ShouldBe(new DateTime(2026, 10, 17));
        first.Cell(3).GetDateTime().ShouldBe(new DateTime(2026, 10, 17, 8, 30, 0));
        first.Cell(4).GetDouble().ShouldBe(12);
        first.Cell(5).DataType.ShouldBe(XLDataType.Number);
        first.Cell(5).GetDouble().ShouldBe(1234567.5);
        first.Cell(6).GetString().ShouldBe("Đã gửi");
        first.RowBelow().Cell(2).IsEmpty().ShouldBeTrue();
    }

    [Fact]
    public async Task Oversized_or_malformed_exports_are_refused()
    {
        using var client = await factory.LoginAsync("khoanoi");
        using (var noColumns = await client.PostAsJsonAsync(Url, new { title = "x", columns = Array.Empty<object>(), rows = Array.Empty<object>() }))
        {
            noColumns.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var tooMany = await client.PostAsJsonAsync(Url, new
        {
            title = "x",
            columns = new[] { new { header = "A", format = "text" } },
            rows = Enumerable.Range(0, 10_001).Select(i => new object[] { i.ToString(System.Globalization.CultureInfo.InvariantCulture) }).ToArray(),
        }))
        {
            tooMany.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await tooMany.Content.ReadAsStringAsync()).ShouldContain("EXPORT_TOO_MANY_ROWS");
        }

        using var anonymous = factory.CreateClient();
        using var denied = await anonymous.PostAsJsonAsync(Url, new { title = "x", columns = new[] { new { header = "A", format = "text" } }, rows = Array.Empty<object>() });
        denied.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
