using BlueContract.Application.Documents;
using Shouldly;

namespace BlueContract.Application.Tests.Documents;

public class TextDiffTests
{
    private static string Render(IReadOnlyList<DiffLine> lines) =>
        string.Join(" ", lines.Select(l => (l.Kind switch { DiffKind.Added => "+", DiffKind.Removed => "-", _ => "=" }) + l.Text));

    [Fact]
    public void A_changed_paragraph_is_removed_then_added_and_the_rest_kept()
    {
        Render(TextDiff.Lines(["a", "b", "c", "d"], ["a", "x", "c", "d", "e"])).ShouldBe("=a -b +x =c =d +e");
    }

    [Fact]
    public void Moves_inside_the_document_keep_the_longest_common_part()
    {
        Render(TextDiff.Lines(["a", "b", "c"], ["b", "c", "a"])).ShouldBe("-a =b =c +a");
    }

    [Fact]
    public void Empty_versions_compare_cleanly()
    {
        TextDiff.Lines([], []).ShouldBeEmpty();
        Render(TextDiff.Lines([], ["a"])).ShouldBe("+a");
        Render(TextDiff.Lines(["a"], [])).ShouldBe("-a");
    }
}
