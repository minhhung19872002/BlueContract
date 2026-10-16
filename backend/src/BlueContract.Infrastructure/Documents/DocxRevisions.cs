using System.Globalization;
using System.Text;
using System.Xml.Linq;
using BlueContract.Application.Documents;
using DocumentFormat.OpenXml.Packaging;

namespace BlueContract.Infrastructure.Documents;

/// <summary>
/// Tracked changes (w:ins, w:del, w:moveFrom/To, w:rPrChange, w:pPrChange, row insert/delete) in the main document part.
/// Revisions are identified by their position in document order ("R1", "R2"...), so a list and a resolve call must use
/// the same file version — the contract service checks that.
/// Paragraph-mark revisions are resolved by dropping the marker (paragraphs are not merged) — see DECISIONS ADR-030.
/// </summary>
internal sealed class DocxRevisions : IDocxRevisions
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public IReadOnlyList<DocxRevision> List(byte[] docx)
    {
        var xml = Load(docx);
        return [.. Revisions(xml).Select((r, i) => Describe(r, i))];
    }

    public byte[] Resolve(byte[] docx, IReadOnlyDictionary<string, bool> decisions)
    {
        if (decisions.Count == 0)
        {
            return docx;
        }

        return Transform(docx, xml =>
        {
            // Resolve from the end so earlier positions stay valid while elements are removed/unwrapped.
            var all = Revisions(xml).ToList();
            for (var i = all.Count - 1; i >= 0; i--)
            {
                if (decisions.TryGetValue(Key(i), out var accept))
                {
                    Apply(all[i], accept);
                }
            }
        });
    }

    public byte[] AcceptAll(byte[] docx) => Transform(docx, xml =>
    {
        var all = Revisions(xml).ToList();
        for (var i = all.Count - 1; i >= 0; i--)
        {
            Apply(all[i], accept: true);
        }
    });

    private static string Key(int index) => "R" + (index + 1).ToString(CultureInfo.InvariantCulture);

    private static XDocument Load(byte[] docx)
    {
        using var stream = new MemoryStream(docx, writable: false);
        using var doc = WordprocessingDocument.Open(stream, false);
        var part = doc.MainDocumentPart ?? throw new InvalidDataException("DOCX has no main document part.");
        using var partStream = part.GetStream(FileMode.Open, FileAccess.Read);
        return XDocument.Load(partStream);
    }

    private static byte[] Transform(byte[] docx, Action<XDocument> change)
    {
        using var stream = new MemoryStream();
        stream.Write(docx);
        using (var doc = WordprocessingDocument.Open(stream, true))
        {
            var part = doc.MainDocumentPart ?? throw new InvalidDataException("DOCX has no main document part.");
            XDocument xml;
            using (var read = part.GetStream(FileMode.Open, FileAccess.Read))
            {
                xml = XDocument.Load(read);
            }

            change(xml);
            using var write = part.GetStream(FileMode.Create, FileAccess.Write);
            xml.Save(write);
        }

        return stream.ToArray();
    }

    /// <summary>Every supported revision element, in document order.</summary>
    private static IEnumerable<XElement> Revisions(XDocument xml) =>
        xml.Descendants().Where(e =>
            (e.Name == W + "ins" || e.Name == W + "del" || e.Name == W + "moveFrom" || e.Name == W + "moveTo")
            || e.Name == W + "rPrChange" || e.Name == W + "pPrChange");

    private static DocxRevision Describe(XElement e, int index)
    {
        var kind = e.Name.LocalName switch
        {
            "ins" or "moveTo" => RevisionKind.Insertion,
            "del" or "moveFrom" => RevisionKind.Deletion,
            _ => RevisionKind.Formatting,
        };
        var author = (string?)e.Attribute(W + "author");
        DateTimeOffset? date = DateTimeOffset.TryParse((string?)e.Attribute(W + "date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d
            : null;

        string text;
        if (kind == RevisionKind.Formatting)
        {
            text = "[Thay đổi định dạng] " + Truncate(VisibleText(e.Ancestors(W + "r").FirstOrDefault() ?? e.Ancestors(W + "p").FirstOrDefault() ?? e), 120);
        }
        else if (IsRowMarker(e))
        {
            text = (kind == RevisionKind.Insertion ? "[Thêm dòng bảng] " : "[Xóa dòng bảng] ") + Truncate(VisibleText(e.Parent!.Parent!), 120);
        }
        else if (IsParagraphMarker(e))
        {
            text = kind == RevisionKind.Insertion ? "[Thêm ngắt đoạn]" : "[Xóa ngắt đoạn]";
        }
        else
        {
            text = string.Concat(e.Descendants().Where(x => x.Name == W + "t" || x.Name == W + "delText").Select(x => x.Value));
        }

        var paragraph = e.Ancestors(W + "p").FirstOrDefault();
        var context = paragraph is null ? string.Empty : Truncate(VisibleText(paragraph), 200);
        return new DocxRevision(Key(index), kind, author, date, text, context);
    }

    private static void Apply(XElement e, bool accept)
    {
        if (e.Parent is null)
        {
            return; // already removed together with an enclosing revision
        }

        switch (e.Name.LocalName)
        {
            case "rPrChange" or "pPrChange":
                if (!accept)
                {
                    // The change element holds the previous properties: restore them.
                    var properties = e.Parent;
                    var previous = e.Elements().FirstOrDefault();
                    properties.RemoveNodes();
                    if (previous is not null)
                    {
                        properties.Add(previous.Elements());
                    }
                }
                else
                {
                    e.Remove();
                }

                return;
        }

        var insertion = e.Name.LocalName is "ins" or "moveTo";
        if (IsRowMarker(e))
        {
            var row = e.Parent!.Parent!;
            if (insertion == accept)
            {
                e.Remove();
            }
            else
            {
                row.Remove();
            }

            return;
        }

        if (IsParagraphMarker(e))
        {
            e.Remove();
            return;
        }

        if (insertion == accept)
        {
            // Accepted insertion / rejected deletion: keep the content.
            if (!insertion)
            {
                foreach (var t in e.Descendants(W + "delText").ToList())
                {
                    t.Name = W + "t";
                }

                foreach (var t in e.Descendants(W + "delInstrText").ToList())
                {
                    t.Name = W + "instrText";
                }
            }

            e.ReplaceWith(e.Nodes());
        }
        else
        {
            e.Remove();
        }
    }

    /// <summary>w:ins / w:del inside w:trPr marks an inserted/deleted table row.</summary>
    private static bool IsRowMarker(XElement e) => e.Parent?.Name == W + "trPr";

    /// <summary>w:ins / w:del inside a run-properties element marks the paragraph mark (or run formatting container).</summary>
    private static bool IsParagraphMarker(XElement e) => e.Parent?.Name == W + "rPr";

    private static string VisibleText(XElement e)
    {
        var sb = new StringBuilder();
        foreach (var t in e.Descendants(W + "t"))
        {
            sb.Append(t.Value);
        }

        return sb.ToString();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
