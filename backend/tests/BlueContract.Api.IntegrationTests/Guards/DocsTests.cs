using System.Text.RegularExpressions;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Guards;

/// <summary>
/// AUDIT-1 §13.9: README and the documents in docs/ describe what is in the repository. Every script they name exists
/// (in both .ps1 and .sh), README lists every script and its links lead to files, and no control character slipped in
/// (a Windows path typed as "scripts\backup" once became a backspace).
/// </summary>
public partial class DocsTests
{
    [GeneratedRegex(@"`scripts(?=(?<rest>[\s\S]{0,40}))")]
    private static partial Regex ScriptMention();

    [GeneratedRegex(@"^[\\/](?<name>[a-z0-9-]+)(?<ext>\.ps1|\.sh)?")]
    private static partial Regex ScriptName();

    [GeneratedRegex(@"\]\((?<target>[^)#\s]+)(#[^)]*)?\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F]")]
    private static partial Regex ControlCharacter();

    private static readonly string Root = RepoRoot();

    private static IEnumerable<string> Documents() =>
        new[] { Path.Combine(Root, "README.md"), Path.Combine(Root, "PROGRESS.md") }
            .Concat(Directory.GetFiles(Path.Combine(Root, "docs"), "*.md"))
            .Where(File.Exists);

    [Fact]
    public void Documents_name_only_scripts_that_exist_in_both_shells()
    {
        var problems = new List<string>();
        foreach (var doc in Documents())
        {
            var text = File.ReadAllText(doc);
            foreach (Match mention in ScriptMention().Matches(text))
            {
                var name = ScriptName().Match(mention.Groups["rest"].Value);
                var where = $"{Path.GetFileName(doc)}:{LineOf(text, mention.Index)}";
                if (!name.Success)
                {
                    problems.Add($"{where} `scripts not followed by /name");
                    continue;
                }

                var basePath = Path.Combine(Root, "scripts", name.Groups["name"].Value);
                if (!File.Exists(basePath + ".ps1") || !File.Exists(basePath + ".sh"))
                {
                    problems.Add($"{where} scripts/{name.Groups["name"].Value} needs both .ps1 and .sh");
                }
            }
        }

        problems.ShouldBeEmpty(string.Join("; ", problems));
    }

    [Fact]
    public void Readme_lists_every_script_and_its_links_lead_to_files()
    {
        var readme = File.ReadAllText(Path.Combine(Root, "README.md"));
        var missing = Directory.GetFiles(Path.Combine(Root, "scripts"), "*.ps1")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !readme.Contains($@"`scripts\{n}.ps1", StringComparison.Ordinal) || !readme.Contains($"`scripts/{n}.sh", StringComparison.Ordinal))
            .ToList();
        missing.ShouldBeEmpty("README → Scripts does not list: " + string.Join(", ", missing));

        var broken = MarkdownLink().Matches(readme)
            .Select(m => m.Groups["target"].Value)
            .Where(t => !t.Contains("://", StringComparison.Ordinal))
            .Where(t => !File.Exists(Path.Combine(Root, t)) && !Directory.Exists(Path.Combine(Root, t)))
            .ToList();
        broken.ShouldBeEmpty("README links to missing files: " + string.Join(", ", broken));
    }

    [Fact]
    public void Documents_contain_no_control_characters()
    {
        var found = Documents()
            .SelectMany(doc =>
            {
                var text = File.ReadAllText(doc);
                return ControlCharacter().Matches(text).Select(m => $"{Path.GetFileName(doc)}:{LineOf(text, m.Index)} U+{(int)m.Value[0]:X4}");
            })
            .ToList();
        found.ShouldBeEmpty(string.Join("; ", found));
    }

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "docs")) && Directory.Exists(Path.Combine(dir.FullName, "backend")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("repository root not found above " + AppContext.BaseDirectory);
    }
}
