using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace BlueContract.Infrastructure.Documents.Templates;

internal enum Align
{
    Left,
    Center,
    Right,
    Justify,
}

internal sealed record Col(string Header, string Cell, int WidthPct, Align Align = Align.Left);

internal sealed record SignatureCol(string Title, string? Subtitle, string? Anchor, string? Name, string? Prefix = null);

/// <summary>
/// Small fluent builder for Vietnamese administrative documents (Nghị định 30/2020/NĐ-CP layout):
/// A4, Times New Roman 13pt, margins 2/2/3/1.5 cm, national motto header, signature blocks.
/// Used to generate the built-in templates shipped in templates/.
/// </summary>
internal sealed class DocxBuilder
{
    private const string Font = "Times New Roman";
    private const int BodySize = 26; // half-points → 13pt
    private const int TableSize = 22; // 11pt
    private readonly List<OpenXmlElement> _elements = [];
    private bool _landscape;

    public DocxBuilder Landscape()
    {
        _landscape = true;
        return this;
    }

    /// <summary>Left: issuing body + number; right: national motto + place/date (Quốc hiệu, tiêu ngữ).</summary>
    public DocxBuilder NationalHeader(string leftTop, string? leftBottom, string? number, string? placeDate)
    {
        var table = BorderlessTable([40, 60]);
        var row = new TableRow();
        row.Append(
            Cell(40, Para(leftTop, Align.Center, bold: false, size: BodySize - 2),
                leftBottom is null ? null : Para(leftBottom, Align.Center, bold: true, size: BodySize - 2),
                number is null ? null : Para(number, Align.Center, size: BodySize - 2)),
            Cell(60, Para("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM", Align.Center, bold: true, size: BodySize - 2),
                Para("Độc lập - Tự do - Hạnh phúc", Align.Center, bold: true, underline: true),
                placeDate is null ? null : Para(placeDate, Align.Center, italic: true)));
        table.Append(row);
        _elements.Add(table);
        _elements.Add(Para(string.Empty));
        return this;
    }

    /// <summary>Centered motto only (contracts).</summary>
    public DocxBuilder Motto()
    {
        _elements.Add(Para("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM", Align.Center, bold: true));
        _elements.Add(Para("Độc lập - Tự do - Hạnh phúc", Align.Center, bold: true, underline: true));
        _elements.Add(Para(string.Empty));
        return this;
    }

    public DocxBuilder Title(string title, params string[] subtitles)
    {
        _elements.Add(Para(title, Align.Center, bold: true, size: BodySize + 2, spacingBefore: 120));
        foreach (var s in subtitles)
        {
            _elements.Add(Para(s, Align.Center, italic: s.StartsWith('(')));
        }

        _elements.Add(Para(string.Empty));
        return this;
    }

    public DocxBuilder Heading(string text)
    {
        _elements.Add(Para(text, Align.Justify, bold: true, spacingBefore: 120));
        return this;
    }

    public DocxBuilder P(string text, bool bold = false, bool italic = false, Align align = Align.Justify, bool indent = true)
    {
        _elements.Add(Para(text, align, bold: bold, italic: italic, firstLineIndent: indent));
        return this;
    }

    /// <summary>Lines prefixed with "- " (or the given marker), no first-line indent.</summary>
    public DocxBuilder List(params string[] lines)
    {
        foreach (var line in lines)
        {
            _elements.Add(Para(line, Align.Justify, firstLineIndent: true));
        }

        return this;
    }

    /// <summary>Block condition: paragraphs between {{#if path}} and {{/if}}.</summary>
    public DocxBuilder If(string path, Action<DocxBuilder> body)
    {
        _elements.Add(Para("{{#if " + path + "}}"));
        body(this);
        _elements.Add(Para("{{/if}}"));
        return this;
    }

    /// <summary>Data table whose single template row repeats for each item of <paramref name="collection"/>.</summary>
    public DocxBuilder Table(string collection, IReadOnlyList<Col> cols, IReadOnlyList<string>? totalRow = null, string? totalLabel = null)
    {
        var table = GridTable(cols.Select(c => c.WidthPct).ToArray());
        var header = new TableRow(new TableRowProperties(new TableHeader()));
        foreach (var c in cols)
        {
            header.Append(Cell(c.WidthPct, Para(c.Header, Align.Center, bold: true, size: TableSize)));
        }

        table.Append(header);
        var row = new TableRow();
        for (var i = 0; i < cols.Count; i++)
        {
            var text = cols[i].Cell;
            if (i == 0)
            {
                text = "{{#" + collection + "}}" + text;
            }

            if (i == cols.Count - 1)
            {
                text += "{{/" + collection + "}}";
            }

            row.Append(Cell(cols[i].WidthPct, Para(text, cols[i].Align, size: TableSize)));
        }

        table.Append(row);

        if (totalRow is not null)
        {
            var total = new TableRow();
            var labelSpan = cols.Count - totalRow.Count;
            var labelWidth = cols.Take(labelSpan).Sum(c => c.WidthPct);
            total.Append(Cell(labelWidth, Para(totalLabel ?? "Tổng cộng", Align.Center, bold: true, size: TableSize), span: labelSpan));
            for (var i = 0; i < totalRow.Count; i++)
            {
                var col = cols[labelSpan + i];
                total.Append(Cell(col.WidthPct, Para(totalRow[i], col.Align, bold: true, size: TableSize)));
            }

            table.Append(total);
        }

        _elements.Add(table);
        _elements.Add(Para(string.Empty));
        return this;
    }

    /// <summary>Two-column key/value table (e.g. ĐKCT).</summary>
    public DocxBuilder KeyValueTable(int keyWidthPct, params (string Key, string Value)[] rows)
    {
        var table = GridTable([keyWidthPct, 100 - keyWidthPct]);
        foreach (var (key, value) in rows)
        {
            table.Append(new TableRow(
                Cell(keyWidthPct, Para(key, Align.Left, bold: true, size: TableSize)),
                Cell(100 - keyWidthPct, Para(value, Align.Justify, size: TableSize))));
        }

        _elements.Add(table);
        _elements.Add(Para(string.Empty));
        return this;
    }

    /// <summary>Signature row: each column gets title, "(Ký, ghi rõ họ tên)", hidden anchor, space and name.</summary>
    public DocxBuilder Signatures(params SignatureCol[] cols)
    {
        var width = 100 / cols.Length;
        var table = BorderlessTable(cols.Select(_ => width).ToArray());
        var row = new TableRow(new TableRowProperties(new CantSplit()));
        foreach (var c in cols)
        {
            var paragraphs = new List<Paragraph?>();
            if (c.Prefix is not null)
            {
                paragraphs.Add(Para(c.Prefix, Align.Center, bold: true));
            }

            paragraphs.Add(Para(c.Title, Align.Center, bold: true));
            paragraphs.Add(c.Subtitle is null ? null : Para(c.Subtitle, Align.Center, italic: true));
            paragraphs.Add(c.Anchor is null ? Para(string.Empty) : Para("{{" + c.Anchor + "}}", Align.Center));
            paragraphs.Add(Para(string.Empty));
            paragraphs.Add(Para(string.Empty));
            paragraphs.Add(Para(string.Empty));
            paragraphs.Add(c.Name is null ? null : Para(c.Name, Align.Center, bold: true));
            row.Append(Cell(width, [.. paragraphs]));
        }

        table.Append(row);
        _elements.Add(Para(string.Empty));
        _elements.Add(table);
        return this;
    }

    public DocxBuilder Spacer()
    {
        _elements.Add(Para(string.Empty));
        return this;
    }

    public byte[] Build()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            AddStyles(main);
            var body = new Body();
            foreach (var e in _elements)
            {
                body.Append(e);
            }

            // Normal trailing paragraph so text typed at the end of the document never inherits the hidden anchor formatting.
            body.Append(Para(string.Empty));

            // A4: 11906 x 16838 twips. Margins: top/bottom 2cm, left 3cm, right 1.5cm.
            body.Append(new SectionProperties(
                _landscape
                    ? new PageSize { Width = 16838, Height = 11906, Orient = PageOrientationValues.Landscape }
                    : new PageSize { Width = 11906, Height = 16838 },
                new PageMargin { Top = 1134, Bottom = 1134, Left = 1701U, Right = 851U, Header = 567U, Footer = 567U, Gutter = 0U }));
            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(body);
            main.Document.Save();
        }

        return stream.ToArray();
    }

    // ---------------- helpers ----------------

    private static void AddStyles(MainDocumentPart main)
    {
        var styles = main.AddNewPart<StyleDefinitionsPart>();
        styles.Styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = Font, HighAnsi = Font, ComplexScript = Font, EastAsia = Font },
                    new FontSize { Val = BodySize.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    new FontSizeComplexScript { Val = BodySize.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    new Languages { Val = "vi-VN" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                    new SpacingBetweenLines { After = "60", Line = "276", LineRule = LineSpacingRuleValues.Auto }))));
        styles.Styles.Save();
    }

    private static Paragraph Para(
        string text,
        Align align = Align.Justify,
        bool bold = false,
        bool italic = false,
        bool underline = false,
        int? size = null,
        bool firstLineIndent = false,
        int spacingBefore = 0)
    {
        var pPr = new ParagraphProperties(new Justification
        {
            Val = align switch
            {
                Align.Center => JustificationValues.Center,
                Align.Right => JustificationValues.Right,
                Align.Left => JustificationValues.Left,
                _ => JustificationValues.Both,
            },
        });
        if (spacingBefore > 0)
        {
            pPr.Append(new SpacingBetweenLines { Before = spacingBefore.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }

        if (firstLineIndent)
        {
            pPr.Append(new Indentation { FirstLine = "567" });
        }

        var paragraph = new Paragraph(pPr);
        if (text.Length == 0)
        {
            return paragraph;
        }

        var rPr = new RunProperties();
        if (bold)
        {
            rPr.Append(new Bold());
        }

        if (italic)
        {
            rPr.Append(new Italic());
        }

        if (underline)
        {
            rPr.Append(new Underline { Val = UnderlineValues.Single });
        }

        if (size is { } s)
        {
            rPr.Append(new FontSize { Val = s.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }

        paragraph.Append(new Run(rPr, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return paragraph;
    }

    private static TableCell Cell(int widthPct, params Paragraph?[] paragraphs) => Cell(widthPct, paragraphs, span: 1);

    private static TableCell Cell(int widthPct, Paragraph paragraph, int span) => Cell(widthPct, [paragraph], span);

    private static TableCell Cell(int widthPct, Paragraph?[] paragraphs, int span)
    {
        var props = new TableCellProperties(
            new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = (widthPct * 50).ToString(System.Globalization.CultureInfo.InvariantCulture) },
            new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });
        if (span > 1)
        {
            props.Append(new GridSpan { Val = span });
        }

        var cell = new TableCell(props);
        var any = false;
        foreach (var p in paragraphs.Where(p => p is not null))
        {
            cell.Append(p!);
            any = true;
        }

        if (!any)
        {
            cell.Append(new Paragraph());
        }

        return cell;
    }

    private static Table GridTable(int[] widthsPct)
    {
        var table = new Table(new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }),
            new TableCellMarginDefault(new TopMargin { Width = "40", Type = TableWidthUnitValues.Dxa }, new BottomMargin { Width = "40", Type = TableWidthUnitValues.Dxa })));
        table.Append(new TableGrid(widthsPct.Select(w => new GridColumn { Width = (w * 90).ToString(System.Globalization.CultureInfo.InvariantCulture) })));
        return table;
    }

    private static Table BorderlessTable(int[] widthsPct)
    {
        var table = new Table(new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
            new TableBorders(
                new TopBorder { Val = BorderValues.None },
                new BottomBorder { Val = BorderValues.None },
                new LeftBorder { Val = BorderValues.None },
                new RightBorder { Val = BorderValues.None },
                new InsideHorizontalBorder { Val = BorderValues.None },
                new InsideVerticalBorder { Val = BorderValues.None })));
        table.Append(new TableGrid(widthsPct.Select(w => new GridColumn { Width = (w * 90).ToString(System.Globalization.CultureInfo.InvariantCulture) })));
        return table;
    }
}
