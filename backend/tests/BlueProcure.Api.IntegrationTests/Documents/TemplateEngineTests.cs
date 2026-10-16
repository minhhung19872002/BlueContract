using System.Text.Json.Nodes;
using BlueProcure.Application.Documents;
using BlueProcure.Domain.Documents;
using BlueProcure.Infrastructure.Documents;
using BlueProcure.Infrastructure.Documents.Templates;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Shouldly;

namespace BlueProcure.Api.IntegrationTests.Documents;

public class TemplateEngineTests
{
    private readonly DocxTemplateEngine _engine = new();

    private static string TextOf(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx), false);
        return string.Join("\n", doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().Select(p => string.Concat(p.Descendants<Text>().Select(t => t.Text))));
    }

    private static byte[] DocWithParagraphs(params Paragraph[] paragraphs)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new Body(paragraphs));
        }

        return stream.ToArray();
    }

    private static Paragraph SplitRuns(params string[] pieces) =>
        new([.. pieces.Select(p => (OpenXmlElement)new Run(new RunProperties(new Bold()), new Text(p) { Space = SpaceProcessingModeValues.Preserve }))]);

    [Fact]
    public void Tags_split_across_runs_are_rendered()
    {
        var template = DocWithParagraphs(SplitRuns("Số hợp đồng: {{Con", "tract.Num", "ber}} — tổng {{Contract.Total | mo", "ney}} đồng"));
        var data = new JsonObject { ["Contract"] = new JsonObject { ["Number"] = "15/2026/HĐ", ["Total"] = 1_234_567m } };

        TextOf(_engine.Render(template, data)).ShouldBe("Số hợp đồng: 15/2026/HĐ — tổng 1.234.567 đồng");
    }

    [Fact]
    public void Filters_format_vietnamese_values()
    {
        var template = DocWithParagraphs(
            SplitRuns("{{A | words}}"), SplitRuns("{{D | date}}"), SplitRuns("{{D | datetext}}"), SplitRuns("{{Q | number}}"),
            SplitRuns("{{P | percent}}"), SplitRuns("{{N | upper}}"), SplitRuns("{{B}}"), SplitRuns("[{{Missing.Path}}]"));
        var data = new JsonObject
        {
            ["A"] = 1_205_030_000m,
            ["D"] = "2026-03-01",
            ["Q"] = 1234.5m,
            ["P"] = 25.64m,
            ["N"] = "bệnh viện",
            ["B"] = true,
        };

        TextOf(_engine.Render(template, data)).Split('\n').ShouldBe(
        [
            "Một tỷ hai trăm linh năm triệu không trăm ba mươi nghìn đồng", "01/03/2026", "ngày 01 tháng 03 năm 2026", "1.234,5", "25,64%",
            "BỆNH VIỆN", "Có", "[]",
        ]);
    }

    [Fact]
    public void Block_conditions_and_loops_expand()
    {
        var template = DocWithParagraphs(
            SplitRuns("{{#if Show}}"), SplitRuns("Hiện"), SplitRuns("{{/if}}"),
            SplitRuns("{{#if !Show}}"), SplitRuns("Ẩn"), SplitRuns("{{/if}}"),
            SplitRuns("{{#Items}}"), SplitRuns("{{@index}}. {{Name}}"), SplitRuns("{{#if Note}}"), SplitRuns("  ghi chú: {{Note}}"), SplitRuns("{{/if}}"), SplitRuns("{{/Items}}"),
            SplitRuns("Kết thúc"));
        var data = new JsonObject
        {
            ["Show"] = true,
            ["Items"] = new JsonArray(new JsonObject { ["Name"] = "A", ["Note"] = "x" }, new JsonObject { ["Name"] = "B" }),
        };

        TextOf(_engine.Render(template, data)).Split('\n').ShouldBe(["Hiện", "1. A", "  ghi chú: x", "2. B", "Kết thúc"]);
    }

    [Fact]
    public void Inline_conditions_and_loops()
    {
        var template = DocWithParagraphs(SplitRuns("Đại diện: {{Name}}{{#if Auth}} (theo GUQ {{Auth}}){{/if}}{{#if !Auth}}.{{/if}} Mã: {{#Tags}}[{{Code}}]{{/Tags}}"));

        TextOf(_engine.Render(template, new JsonObject
        {
            ["Name"] = "Lan",
            ["Auth"] = "",
            ["Tags"] = new JsonArray(new JsonObject { ["Code"] = "X" }, new JsonObject { ["Code"] = "Y" }),
        })).ShouldBe("Đại diện: Lan. Mã: [X][Y]");
    }

    [Fact]
    public void Table_row_loop_repeats_rows_with_item_scope()
    {
        var builder = new DocxBuilder().Table("Items", [new Col("STT", "{{@index}}", 20), new Col("Tên", "{{Name}}", 50), new Col("Thành tiền", "{{Amount | money}}", 30)],
            ["{{Total | money}}"]);
        var data = new JsonObject
        {
            ["Total"] = 300m,
            ["Items"] = new JsonArray(new JsonObject { ["Name"] = "Thuốc A", ["Amount"] = 100m }, new JsonObject { ["Name"] = "Thuốc B", ["Amount"] = 200m }),
        };

        var rendered = _engine.Render(builder.Build(), data);

        using var doc = WordprocessingDocument.Open(new MemoryStream(rendered), false);
        var rows = doc.MainDocumentPart!.Document!.Body!.Descendants<TableRow>()
            .Select(r => string.Join("|", r.Elements<TableCell>().Select(c => c.InnerText))).ToList();
        rows.ShouldBe(["STT|Tên|Thành tiền", "1|Thuốc A|100", "2|Thuốc B|200", "Tổng cộng|300"]);
    }

    [Fact]
    public void Signature_anchor_is_kept_as_hidden_text()
    {
        var template = DocWithParagraphs(SplitRuns("Giám đốc {{SIG_DIRECTOR}} ký"));

        var rendered = _engine.Render(template, []);

        using var doc = WordprocessingDocument.Open(new MemoryStream(rendered), false);
        var anchorRun = doc.MainDocumentPart!.Document!.Body!.Descendants<Run>().Single(r => r.InnerText == "{{SIG_DIRECTOR}}");
        anchorRun.RunProperties!.Color!.Val!.Value.ShouldBe("FFFFFF");
        TextOf(rendered).ShouldBe("Giám đốc {{SIG_DIRECTOR}} ký");
    }

    [Fact]
    public void Multiline_values_become_line_breaks()
    {
        var rendered = _engine.Render(DocWithParagraphs(SplitRuns("{{Text}}")), new JsonObject { ["Text"] = "Dòng 1\nDòng 2" });

        using var doc = WordprocessingDocument.Open(new MemoryStream(rendered), false);
        doc.MainDocumentPart!.Document!.Body!.Descendants<Break>().Count().ShouldBe(1);
    }

    [Fact]
    public void Scan_lists_tags_in_order()
    {
        var template = DocWithParagraphs(SplitRuns("{{#Items}}{{Name}}{{/Items}} {{Contract.Number | upper}}"));

        _engine.ScanTags(template).ShouldBe(["#Items", "Name", "/Items", "Contract.Number | upper"]);
    }

    [Fact]
    public void Validator_reports_unknown_placeholders_filters_and_unbalanced_blocks()
    {
        var result = TemplateValidator.Validate(TemplateKind.Contract,
            ["Contract.Number", "Contract.Nmuber", "Contract.TotalAmount | tienchu", "#Items", "Name", "/Items", "Name", "#Lines", "#if Contract.IsBidding", "SIG_ANYTHING"]);

        result.IsValid.ShouldBeFalse();
        result.Errors.Select(e => e.Tag).ShouldBe(["Contract.Nmuber", "Contract.TotalAmount | tienchu", "Name", "#Lines", "#if", "#Lines"]);
    }

    public static TheoryData<string> BuiltInCodes() => [.. BuiltInTemplates.All.Select(t => t.Code)];

    [Theory]
    [MemberData(nameof(BuiltInCodes))]
    public void Built_in_template_is_valid_and_renders_sample_data_completely(string code)
    {
        var builtIn = BuiltInTemplates.All.Single(t => t.Code == code);
        var bytes = builtIn.Build();

        var validation = TemplateValidator.Validate(builtIn.Kind, _engine.ScanTags(bytes));
        validation.Errors.ShouldBeEmpty();

        var text = TextOf(_engine.Render(bytes, TemplateFieldCatalog.SampleData(builtIn.Kind)));
        var leftovers = System.Text.RegularExpressions.Regex.Matches(text, @"\{\{(?!SIG_)[^}]*\}\}").Select(m => m.Value).ToList();
        leftovers.ShouldBeEmpty();
        text.ShouldContain("Bệnh viện Đa khoa Thành phố Vinh", Case.Insensitive);
    }

    [Fact]
    public void Drug_contract_renders_items_amount_in_words_and_dates()
    {
        var bytes = BuiltInTemplates.All.Single(t => t.Code == "HD_THUOC").Build();

        var text = TextOf(_engine.Render(bytes, TemplateFieldCatalog.SampleData(TemplateKind.Contract)));

        text.ShouldContain("Số: 15/2026/HĐ-BV");
        text.ShouldContain("Paracetamol 500mg");
        text.ShouldContain("Amoxicillin 500mg");
        text.ShouldContain("Bằng chữ: Một tỷ hai trăm linh năm triệu không trăm ba mươi nghìn đồng.");
        text.ShouldContain("Hôm nay, ngày 01 tháng 03 năm 2026");
        text.ShouldContain("Quyết định số 1234/QĐ-BV ngày 15/02/2026");
        text.ShouldContain("{{SIG_DIRECTOR}}");
    }

    [Fact]
    public void Non_bidding_contract_omits_bid_decision()
    {
        var bytes = BuiltInTemplates.All.Single(t => t.Code == "HD_DICHVU_KHONGTHAU").Build();

        TextOf(_engine.Render(bytes, TemplateFieldCatalog.SampleData(TemplateKind.Contract))).ShouldNotContain("Quyết định số");
    }
}
