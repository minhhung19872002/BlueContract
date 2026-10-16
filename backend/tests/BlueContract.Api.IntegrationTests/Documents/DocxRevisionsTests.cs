using System.Xml.Linq;
using BlueContract.Application.Documents;
using BlueContract.Infrastructure.Documents;
using BlueContract.Infrastructure.Documents.Templates;
using DocumentFormat.OpenXml.Packaging;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Documents;

/// <summary>Track Changes handling used by the supplier review loop (no containers needed).</summary>
public class DocxRevisionsTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private readonly DocxRevisions _revisions = new();

    private static byte[] Document(params XElement[] paragraphs)
    {
        var docx = new DocxBuilder().P("placeholder").Build();
        using var stream = new MemoryStream();
        stream.Write(docx);
        using (var doc = WordprocessingDocument.Open(stream, true))
        {
            var part = doc.MainDocumentPart!;
            var xml = new XDocument(new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W), new XElement(W + "body", paragraphs)));
            using var write = part.GetStream(FileMode.Create, FileAccess.Write);
            xml.Save(write);
        }

        return stream.ToArray();
    }

    private static XElement Run(string text, bool bold = false) =>
        new(W + "r", bold ? new XElement(W + "rPr", new XElement(W + "b")) : null, new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text));

    private static XElement Ins(string author, params XElement[] runs) =>
        new(W + "ins", new XAttribute(W + "id", "1"), new XAttribute(W + "author", author), new XAttribute(W + "date", "2026-10-16T08:00:00Z"), runs);

    private static XElement Del(string author, string text) =>
        new(W + "del", new XAttribute(W + "id", "2"), new XAttribute(W + "author", author),
            new XElement(W + "r", new XElement(W + "delText", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));

    private static string Text(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(stream, false);
        using var part = doc.MainDocumentPart!.GetStream(FileMode.Open, FileAccess.Read);
        return string.Concat(XDocument.Load(part).Descendants(W + "t").Select(t => t.Value));
    }

    [Fact]
    public void Lists_insertions_deletions_and_formatting_in_document_order()
    {
        var formatted = new XElement(W + "r",
            new XElement(W + "rPr", new XElement(W + "b"), new XElement(W + "rPrChange", new XAttribute(W + "id", "3"), new XAttribute(W + "author", "NCC"), new XElement(W + "rPr"))),
            new XElement(W + "t", "đậm"));
        var docx = Document(
            new XElement(W + "p", Run("Giao hàng trong "), Del("NCC", "05"), Ins("NCC", Run("03")), Run(" ngày.")),
            new XElement(W + "p", formatted));

        var list = _revisions.List(docx);

        list.Select(r => (r.Id, r.Kind, r.Text)).ShouldBe(
        [
            ("R1", RevisionKind.Deletion, "05"),
            ("R2", RevisionKind.Insertion, "03"),
            ("R3", RevisionKind.Formatting, "[Thay đổi định dạng] đậm"),
        ]);
        list[0].Author.ShouldBe("NCC");
        list[1].Date.ShouldBe(new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero));
        list[0].Context.ShouldBe("Giao hàng trong 03 ngày.");
    }

    [Fact]
    public void Accept_and_reject_produce_the_expected_text()
    {
        var docx = Document(new XElement(W + "p", Run("Giao hàng trong "), Del("NCC", "05"), Ins("NCC", Run("03")), Run(" ngày.")));

        Text(_revisions.AcceptAll(docx)).ShouldBe("Giao hàng trong 03 ngày.");
        Text(_revisions.Resolve(docx, new Dictionary<string, bool> { ["R1"] = false, ["R2"] = false })).ShouldBe("Giao hàng trong 05 ngày.");

        var partial = _revisions.Resolve(docx, new Dictionary<string, bool> { ["R2"] = true });
        _revisions.List(partial).Single().Kind.ShouldBe(RevisionKind.Deletion);
    }

    [Fact]
    public void Rejecting_a_format_change_restores_the_previous_properties_and_rows_follow_markers()
    {
        var formatted = new XElement(W + "r",
            new XElement(W + "rPr", new XElement(W + "b"), new XElement(W + "rPrChange", new XAttribute(W + "author", "NCC"), new XElement(W + "rPr", new XElement(W + "i")))),
            new XElement(W + "t", "x"));
        var insertedRow = new XElement(W + "tr", new XElement(W + "trPr", new XElement(W + "ins", new XAttribute(W + "author", "NCC"))),
            new XElement(W + "tc", new XElement(W + "p", Run("dòng mới"))));
        var docx = Document(new XElement(W + "p", formatted), new XElement(W + "tbl", insertedRow));

        _revisions.List(docx).Select(r => r.Kind).ShouldBe([RevisionKind.Formatting, RevisionKind.Insertion]);
        var rejected = _revisions.Resolve(docx, new Dictionary<string, bool> { ["R1"] = false, ["R2"] = false });
        Text(rejected).ShouldBe("x");
        _revisions.List(rejected).ShouldBeEmpty();

        using var stream = new MemoryStream(rejected);
        using var doc = WordprocessingDocument.Open(stream, false);
        using var part = doc.MainDocumentPart!.GetStream(FileMode.Open, FileAccess.Read);
        var rPr = XDocument.Load(part).Descendants(W + "rPr").First();
        rPr.Elements().Select(e => e.Name.LocalName).ShouldBe(["i"]);

        Text(_revisions.AcceptAll(docx)).ShouldBe("xdòng mới");
    }
}
