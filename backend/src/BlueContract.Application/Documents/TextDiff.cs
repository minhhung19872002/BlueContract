namespace BlueContract.Application.Documents;

public enum DiffKind
{
    Same = 0,
    Added = 1,
    Removed = 2,
}

/// <summary>One paragraph of a comparison: kept, only in the newer version, or only in the older one.</summary>
public sealed record DiffLine(DiffKind Kind, string Text);

/// <summary>Result of comparing two versions of a document (spec §9.1-6 "so sánh phiên bản").</summary>
public sealed record DocumentCompareDto(int FromVersion, int ToVersion, int Added, int Removed, IReadOnlyList<DiffLine> Lines);

/// <summary>
/// Paragraph diff (longest common subsequence). A changed paragraph shows as the old text removed then the new text
/// added. Common head and tail are skipped first; when the middle is still too large for the table the middle is
/// reported as removed then added.
/// </summary>
public static class TextDiff
{
    private const int MaxCells = 4_000_000;

    public static IReadOnlyList<DiffLine> Lines(IReadOnlyList<string> older, IReadOnlyList<string> newer)
    {
        var head = 0;
        while (head < older.Count && head < newer.Count && older[head] == newer[head])
        {
            head++;
        }

        var tail = 0;
        while (tail < older.Count - head && tail < newer.Count - head && older[older.Count - 1 - tail] == newer[newer.Count - 1 - tail])
        {
            tail++;
        }

        var result = new List<DiffLine>(older.Count + newer.Count);
        result.AddRange(older.Take(head).Select(t => new DiffLine(DiffKind.Same, t)));

        var a = older.Skip(head).Take(older.Count - head - tail).ToArray();
        var b = newer.Skip(head).Take(newer.Count - head - tail).ToArray();
        if ((long)(a.Length + 1) * (b.Length + 1) > MaxCells)
        {
            result.AddRange(a.Select(t => new DiffLine(DiffKind.Removed, t)));
            result.AddRange(b.Select(t => new DiffLine(DiffKind.Added, t)));
        }
        else
        {
            result.AddRange(Middle(a, b));
        }

        result.AddRange(older.Skip(older.Count - tail).Select(t => new DiffLine(DiffKind.Same, t)));
        return result;
    }

    private static IEnumerable<DiffLine> Middle(string[] a, string[] b)
    {
        // lcs[i, j] = length of the LCS of a[i..] and b[j..].
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y])
            {
                yield return new DiffLine(DiffKind.Same, a[x]);
                x++;
                y++;
            }
            else if (x < a.Length && (y == b.Length || lcs[x + 1, y] >= lcs[x, y + 1]))
            {
                yield return new DiffLine(DiffKind.Removed, a[x]);
                x++;
            }
            else
            {
                yield return new DiffLine(DiffKind.Added, b[y]);
                y++;
            }
        }
    }
}
