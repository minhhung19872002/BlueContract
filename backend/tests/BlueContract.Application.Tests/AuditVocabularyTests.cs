using System.Text.RegularExpressions;
using BlueContract.Application.Common;
using BlueContract.Domain.Common;
using Shouldly;

namespace BlueContract.Application.Tests.Common;

/// <summary>AUDIT-1 §12.5: the audit trail never shows a code — every action the code writes and every audited record has a Vietnamese name.</summary>
public partial class AuditVocabularyTests
{
    // audit.Log("X", ...), audit.LogNowAsync(AuditActions.X, ...), cond ? "A" : "B" as the first argument.
    [GeneratedRegex(@"audit\.Log\w*\(\s*(?<first>[^,]+),\s*(?<subject>[^,]+),")]
    private static partial Regex AuditCall();

    [GeneratedRegex("\"([A-Za-z]+)\"")]
    private static partial Regex Literal();

    [GeneratedRegex(@"AuditActions\.([A-Za-z]+)")]
    private static partial Regex Constant();

    [GeneratedRegex(@"nameof\(([A-Za-z]+)\)")]
    private static partial Regex NameOf();

    [Fact]
    public void Every_action_written_by_the_code_has_a_vietnamese_name()
    {
        var calls = AuditCall().Matches(Source()).ToList();
        calls.Count.ShouldBeGreaterThan(50);
        var actions = calls.SelectMany(m => Literal().Matches(m.Groups["first"].Value).Select(x => x.Groups[1].Value)
                .Concat(Constant().Matches(m.Groups["first"].Value).Select(x => x.Groups[1].Value)))
            .Concat(["Create", "Update", "Delete"])
            .Distinct().ToList();
        var missing = actions.Where(a => !AuditVocabulary.Actions.ContainsKey(a)).ToList();
        missing.ShouldBeEmpty("audit actions without a Vietnamese name: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_audited_record_kind_has_a_vietnamese_name()
    {
        var entities = typeof(Entity).Assembly.GetTypes().Where(t => t is { IsAbstract: false, IsClass: true } && t.IsSubclassOf(typeof(Entity))).Select(t => t.Name);
        var written = AuditCall().Matches(Source()).SelectMany(m => Literal().Matches(m.Groups["subject"].Value).Select(x => x.Groups[1].Value)
            .Concat(NameOf().Matches(m.Groups["subject"].Value).Select(x => x.Groups[1].Value)));
        var missing = entities.Concat(written).Distinct().Where(e => !AuditVocabulary.Subjects.ContainsKey(e)).ToList();
        missing.ShouldBeEmpty("record kinds without a Vietnamese name: " + string.Join(", ", missing));
    }

    [Fact]
    public void Unknown_codes_never_show_as_such()
    {
        AuditVocabulary.Action("SomethingNew").ShouldBe("Thao tác khác");
        AuditVocabulary.Subject("SomethingNew").ShouldBe("Dữ liệu khác");
    }

    private static string Source()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var src = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(Path.Combine(src, "BlueContract.Domain")))
            {
                return string.Join('\n', Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    .Select(File.ReadAllText));
            }
        }

        throw new DirectoryNotFoundException("backend/src not found");
    }
}
