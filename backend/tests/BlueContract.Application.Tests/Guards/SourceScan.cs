namespace BlueContract.Application.Tests.Guards;

/// <summary>Reads the backend sources so guard tests can catch "dead switches" (declared but never read).</summary>
internal static class SourceScan
{
    public static string BackendSrc { get; } = FindBackendSrc();

    /// <summary>Production C# files, without generated code, migrations and seeders.</summary>
    public static IReadOnlyList<(string Path, string Text)> ProductionFiles(params string[] excludeFileNames) =>
        [.. Directory.EnumerateFiles(BackendSrc, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !p.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !p.Contains($"{Path.DirectorySeparatorChar}Seeding{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !excludeFileNames.Contains(Path.GetFileName(p), StringComparer.Ordinal))
            .Select(p => (p, WithoutComments(File.ReadAllLines(p))))];

    /// <summary>Drops line comments so a permission mentioned in XML docs does not count as checked.</summary>
    private static string WithoutComments(string[] lines) =>
        string.Join(Environment.NewLine, lines.Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string FindBackendSrc()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "BlueContract.Domain");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(dir.FullName, "src");
            }
        }

        throw new InvalidOperationException("backend/src not found above " + AppContext.BaseDirectory);
    }
}
