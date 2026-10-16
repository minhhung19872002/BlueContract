using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BlueContract.Application.Documents;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace BlueContract.Infrastructure.Documents;

/// <summary>
/// Mustache-like DOCX template engine (spec §10):
/// <list type="bullet">
/// <item><c>{{Contract.Number}}</c>, <c>{{Amount | money}}</c>, <c>{{Amount | words}}</c>, <c>{{SignedDate | datetext}}</c>…</item>
/// <item>Table row loop: put <c>{{#Items}}</c> … <c>{{/Items}}</c> in a row (or spanning rows) → rows repeat per item; <c>{{@index}}</c> = 1-based index.</item>
/// <item>Block loop / condition: a paragraph containing only <c>{{#Items}}</c> / <c>{{#if Path}}</c> up to <c>{{/Items}}</c> / <c>{{/if}}</c>.</item>
/// <item>Inline condition/loop inside one paragraph; <c>{{#if !Path}}</c> negates.</item>
/// <item>Signature anchors <c>{{SIG_DIRECTOR}}</c> stay in the output as tiny white text so the signer can locate them in the PDF.</item>
/// </list>
/// Runs are merged inside paragraphs that contain tags, so tags split by Word's spell-check/revision runs still work
/// (the paragraph keeps the formatting of its first run).
/// </summary>
public sealed partial class DocxTemplateEngine : IDocumentGenerator
{
    public IReadOnlyList<string> ScanTags(byte[] template)
    {
        using var stream = new MemoryStream(template, writable: false);
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);
        var tags = new List<string>();
        foreach (var root in Roots(doc))
        {
            foreach (var paragraph in root.Descendants<Paragraph>())
            {
                var text = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text));
                tags.AddRange(TagPattern().Matches(text).Select(m => m.Groups[1].Value.Trim()));
            }
        }

        return tags;
    }

    public byte[] Render(byte[] template, JsonObject data)
    {
        using var stream = new MemoryStream();
        stream.Write(template);
        stream.Position = 0;
        using (var doc = WordprocessingDocument.Open(stream, isEditable: true))
        {
            var scope = new Scope(data, null, 0, null);
            foreach (var root in Roots(doc))
            {
                foreach (var paragraph in root.Descendants<Paragraph>().ToList())
                {
                    MergeTextRuns(paragraph);
                }

                RenderContainer(root, scope);
            }
        }

        return stream.ToArray();
    }

    private static IEnumerable<OpenXmlElement> Roots(WordprocessingDocument doc)
    {
        var main = doc.MainDocumentPart ?? throw new InvalidOperationException("DOCX has no main document part.");
        if (main.Document?.Body is { } body)
        {
            yield return body;
        }

        foreach (var header in main.HeaderParts)
        {
            if (header.Header is { } h)
            {
                yield return h;
            }
        }

        foreach (var footer in main.FooterParts)
        {
            if (footer.Footer is { } f)
            {
                yield return f;
            }
        }
    }

    // ---------------- Block level ----------------

    /// <summary>Renders the block children (paragraphs, tables...) of a container such as Body, TableCell, Header.</summary>
    private void RenderContainer(OpenXmlElement container, Scope scope) => RenderSequence(container.ChildElements.ToList(), scope);

    /// <summary>Renders a sequence of sibling block elements, expanding block loops/conditions found among them.</summary>
    private void RenderSequence(List<OpenXmlElement> children, Scope scope)
    {
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child.Parent is null)
            {
                continue; // removed by an earlier block expansion
            }

            if (child is Paragraph p && BlockOpen(p) is { } open)
            {
                var closeIndex = FindBlockClose(children, i, open);
                if (closeIndex < 0)
                {
                    RenderParagraph(p, scope);
                    continue;
                }

                var body = children.Skip(i + 1).Take(closeIndex - i - 1).ToList();
                ExpandBlock(open, p, children[closeIndex], body, scope);
                i = closeIndex;
                continue;
            }

            switch (child)
            {
                case Paragraph paragraph:
                    RenderParagraph(paragraph, scope);
                    break;
                case Table table:
                    RenderTable(table, scope);
                    break;
                case SdtBlock sdt when sdt.SdtContentBlock is { } content:
                    RenderContainer(content, scope);
                    break;
            }
        }
    }

    private void ExpandBlock(BlockTag open, OpenXmlElement openMarker, OpenXmlElement closeMarker, List<OpenXmlElement> body, Scope scope)
    {
        var parent = openMarker.Parent!;
        if (open.IsCondition)
        {
            if (Truthy(scope, open))
            {
                RenderSequence(body, scope);
            }
            else
            {
                body.ForEach(e => e.Remove());
            }
        }
        else
        {
            var items = Resolve(scope, open.Path) as JsonArray ?? [];
            var index = 0;
            foreach (var item in items)
            {
                index++;
                var itemScope = new Scope(scope.Root, item, index, scope);
                var clones = new List<OpenXmlElement>();
                foreach (var element in body)
                {
                    var clone = element.CloneNode(true);
                    parent.InsertBefore(clone, closeMarker);
                    clones.Add(clone);
                }

                RenderSequence(clones, itemScope);
            }

            body.ForEach(e => e.Remove());
        }

        openMarker.Remove();
        closeMarker.Remove();
    }

    private static int FindBlockClose(List<OpenXmlElement> siblings, int openIndex, BlockTag open)
    {
        var depth = 0;
        for (var j = openIndex + 1; j < siblings.Count; j++)
        {
            if (siblings[j] is not Paragraph p)
            {
                continue;
            }

            if (BlockOpen(p) is { } nested && nested.CloseName == open.CloseName)
            {
                depth++;
            }
            else if (BlockClose(p) == open.CloseName)
            {
                if (depth == 0)
                {
                    return j;
                }

                depth--;
            }
        }

        return -1;
    }

    // ---------------- Tables ----------------

    private void RenderTable(Table table, Scope scope)
    {
        var rows = table.Elements<TableRow>().ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var text = RowText(row);
            var open = RowLoopOpen().Match(text);
            if (open.Success)
            {
                var name = open.Groups[1].Value;
                var closeIndex = -1;
                for (var j = i; j < rows.Count; j++)
                {
                    if (RowText(rows[j]).Contains("{{/" + name + "}}", StringComparison.Ordinal))
                    {
                        closeIndex = j;
                        break;
                    }
                }

                if (closeIndex >= 0)
                {
                    ExpandRowLoop(table, rows.GetRange(i, closeIndex - i + 1), name, scope);
                    i = closeIndex;
                    continue;
                }
            }

            foreach (var cell in row.Elements<TableCell>())
            {
                RenderContainer(cell, scope);
            }
        }
    }

    private void ExpandRowLoop(Table table, List<TableRow> templateRows, string path, Scope scope)
    {
        var items = Resolve(scope, path) as JsonArray ?? [];
        var anchor = templateRows[^1];
        OpenXmlElement insertAfter = anchor;
        var index = 0;
        foreach (var item in items)
        {
            index++;
            var itemScope = new Scope(scope.Root, item, index, scope);
            foreach (var templateRow in templateRows)
            {
                var clone = (TableRow)templateRow.CloneNode(true);
                StripMarkers(clone, path);
                table.InsertAfter(clone, insertAfter);
                insertAfter = clone;
                foreach (var cell in clone.Elements<TableCell>())
                {
                    RenderContainer(cell, itemScope);
                }
            }
        }

        templateRows.ForEach(r => r.Remove());
    }

    private static void StripMarkers(OpenXmlElement element, string path)
    {
        foreach (var t in element.Descendants<Text>())
        {
            t.Text = t.Text.Replace("{{#" + path + "}}", string.Empty, StringComparison.Ordinal)
                .Replace("{{/" + path + "}}", string.Empty, StringComparison.Ordinal);
        }
    }

    private static string RowText(TableRow row) => string.Concat(row.Descendants<Text>().Select(t => t.Text));

    // ---------------- Paragraph level ----------------

    private static void RenderParagraph(Paragraph paragraph, Scope scope)
    {
        foreach (var run in paragraph.Elements<Run>().ToList())
        {
            var texts = run.Elements<Text>().ToList();
            if (texts.Count != 1 || !texts[0].Text.Contains("{{", StringComparison.Ordinal))
            {
                continue;
            }

            var rendered = RenderInline(texts[0].Text, scope);
            WriteRun(run, texts[0], rendered);
        }

        foreach (var hyperlink in paragraph.Elements<Hyperlink>())
        {
            foreach (var t in hyperlink.Descendants<Text>().Where(t => t.Text.Contains("{{", StringComparison.Ordinal)))
            {
                t.Text = RenderInline(t.Text, scope);
            }
        }
    }

    private static string RenderInline(string text, Scope scope)
    {
        // Inline conditions and loops first (innermost resolved by repetition).
        for (var guard = 0; guard < 20; guard++)
        {
            var before = text;
            text = InlineIf().Replace(text, m => Truthy(scope, BlockTag.Condition(m.Groups[1].Value)) ? m.Groups[2].Value : string.Empty);
            text = InlineLoop().Replace(text, m =>
            {
                var items = Resolve(scope, m.Groups[1].Value) as JsonArray ?? [];
                var sb = new StringBuilder();
                var index = 0;
                foreach (var item in items)
                {
                    index++;
                    sb.Append(RenderInline(m.Groups[2].Value, new Scope(scope.Root, item, index, scope)));
                }

                return sb.ToString();
            });
            if (text == before)
            {
                break;
            }
        }

        return TagPattern().Replace(text, m =>
        {
            var tag = m.Groups[1].Value.Trim();
            if (tag.StartsWith("SIG_", StringComparison.Ordinal))
            {
                return m.Value; // anchors are emitted as hidden runs by WriteRun
            }

            if (tag.StartsWith('#') || tag.StartsWith('/'))
            {
                return string.Empty; // unmatched block marker
            }

            var (path, filter) = SplitFilter(tag);
            return TemplateValueFormatter.Format(Resolve(scope, path), filter);
        });
    }

    /// <summary>Writes rendered text into the run, splitting out line breaks and signature anchors.</summary>
    private static void WriteRun(Run run, Text text, string rendered)
    {
        var anchors = AnchorPattern().Matches(rendered);
        if (anchors.Count == 0 && !rendered.Contains('\n', StringComparison.Ordinal))
        {
            text.Text = rendered;
            text.Space = SpaceProcessingModeValues.Preserve;
            return;
        }

        var parent = run.Parent!;
        OpenXmlElement last = run;
        var position = 0;
        foreach (Match anchor in anchors)
        {
            last = InsertTextRun(parent, last, run, rendered[position..anchor.Index], hidden: false);
            last = InsertTextRun(parent, last, run, anchor.Value, hidden: true);
            position = anchor.Index + anchor.Length;
        }

        InsertTextRun(parent, last, run, rendered[position..], hidden: false);
        run.Remove();
    }

    private static OpenXmlElement InsertTextRun(OpenXmlElement parent, OpenXmlElement after, Run template, string value, bool hidden)
    {
        if (value.Length == 0)
        {
            return after;
        }

        var run = new Run();
        if (template.RunProperties?.CloneNode(true) is RunProperties props)
        {
            run.RunProperties = props;
        }

        if (hidden)
        {
            // Invisible but extractable from the PDF so the signing step can find the signature position.
            run.RunProperties ??= new RunProperties();
            run.RunProperties.Color = new Color { Val = "FFFFFF" };
            run.RunProperties.FontSize = new FontSize { Val = "2" };
        }

        var lines = value.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                run.AppendChild(new Break());
            }

            run.AppendChild(new Text(lines[i].TrimEnd('\r')) { Space = SpaceProcessingModeValues.Preserve });
        }

        parent.InsertAfter(run, after);
        return run;
    }

    /// <summary>Merges consecutive text-only runs so tags split across runs become contiguous.</summary>
    private static void MergeTextRuns(Paragraph paragraph)
    {
        var text = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text));
        if (!text.Contains("{{", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var proof in paragraph.Elements<ProofError>().ToList())
        {
            proof.Remove();
        }

        Run? head = null;
        foreach (var element in paragraph.ChildElements.ToList())
        {
            if (element is Run run && IsTextOnly(run))
            {
                if (head is null)
                {
                    head = run;
                    var texts = run.Elements<Text>().ToList();
                    var merged = string.Concat(texts.Select(t => t.Text));
                    texts.ForEach(t => t.Remove());
                    run.AppendChild(new Text(merged) { Space = SpaceProcessingModeValues.Preserve });
                }
                else
                {
                    var target = head.GetFirstChild<Text>()!;
                    target.Text += string.Concat(run.Elements<Text>().Select(t => t.Text));
                    run.Remove();
                }
            }
            else if (element is BookmarkStart or BookmarkEnd)
            {
                // Bookmarks don't break a text sequence.
            }
            else
            {
                head = null;
            }
        }
    }

    private static bool IsTextOnly(Run run) =>
        run.ChildElements.All(c => c is RunProperties or Text or LastRenderedPageBreak) && run.Elements<Text>().Any();

    // ---------------- Data resolution ----------------

    private static JsonNode? Resolve(Scope scope, string path)
    {
        path = path.Trim();
        if (path == "@index")
        {
            return JsonValue.Create(scope.Index);
        }

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        // Look up the first segment in the item scopes (innermost first), then the root.
        for (var s = scope; s is not null; s = s.Parent)
        {
            if (s.Item is JsonObject item && item.TryGetPropertyValue(segments[0], out var found))
            {
                return Walk(found, segments.AsSpan(1));
            }
        }

        return scope.Root.TryGetPropertyValue(segments[0], out var rootValue) ? Walk(rootValue, segments.AsSpan(1)) : null;
    }

    private static JsonNode? Walk(JsonNode? node, ReadOnlySpan<string> rest)
    {
        foreach (var segment in rest)
        {
            node = node is JsonObject obj && obj.TryGetPropertyValue(segment, out var next) ? next : null;
        }

        return node;
    }

    private static bool Truthy(Scope scope, BlockTag tag)
    {
        var value = TemplateValueFormatter.IsTruthy(Resolve(scope, tag.Path));
        return tag.Negated ? !value : value;
    }

    private static (string Path, string? Filter) SplitFilter(string tag)
    {
        var pipe = tag.IndexOf('|', StringComparison.Ordinal);
        return pipe < 0 ? (tag, null) : (tag[..pipe].Trim(), tag[(pipe + 1)..].Trim().ToLowerInvariant());
    }

    private static BlockTag? BlockOpen(Paragraph p)
    {
        var text = string.Concat(p.Descendants<Text>().Select(t => t.Text)).Trim();
        var m = BlockOpenPattern().Match(text);
        if (!m.Success)
        {
            return null;
        }

        return m.Groups[1].Success ? BlockTag.Condition(m.Groups[2].Value) : BlockTag.Loop(m.Groups[3].Value);
    }

    private static string? BlockClose(Paragraph p)
    {
        var text = string.Concat(p.Descendants<Text>().Select(t => t.Text)).Trim();
        var m = BlockClosePattern().Match(text);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex(@"\{\{(.*?)\}\}", RegexOptions.Singleline)]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\{\{SIG_[A-Z0-9_]+\}\}")]
    private static partial Regex AnchorPattern();

    [GeneratedRegex(@"\{\{#if\s+(!?[\w.@]+)\s*\}\}(.*?)\{\{/if\}\}", RegexOptions.Singleline)]
    private static partial Regex InlineIf();

    [GeneratedRegex(@"\{\{#(?!if\s)([\w.]+)\}\}(.*?)\{\{/\1\}\}", RegexOptions.Singleline)]
    private static partial Regex InlineLoop();

    [GeneratedRegex(@"\{\{#(?!if\s)([\w.]+)\}\}")]
    private static partial Regex RowLoopOpen();

    [GeneratedRegex(@"^\{\{#(?:(if)\s+(!?[\w.@]+)|([\w.]+))\s*\}\}$")]
    private static partial Regex BlockOpenPattern();

    [GeneratedRegex(@"^\{\{/([\w.]+)\}\}$")]
    private static partial Regex BlockClosePattern();

    private sealed record Scope(JsonObject Root, JsonNode? Item, int Index, Scope? Parent);

    private sealed record BlockTag(string Path, bool IsCondition, bool Negated)
    {
        public string CloseName => IsCondition ? "if" : Path;

        public static BlockTag Condition(string raw) =>
            raw.StartsWith('!') ? new BlockTag(raw[1..], true, true) : new BlockTag(raw, true, false);

        public static BlockTag Loop(string path) => new(path, false, false);
    }
}
