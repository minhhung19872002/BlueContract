namespace BlueContract.Domain.Common;

/// <summary>
/// Per-year counters for document numbers (DX-2026-0001...). Allocation is one atomic upsert in the database
/// (<c>IAppDbContext.NextNumberAsync</c>); the unique index on each generated number is the final guard.
/// </summary>
public sealed class NumberSequence
{
    private NumberSequence()
    {
        Prefix = string.Empty;
    }

    public NumberSequence(string prefix, int year)
    {
        Prefix = prefix;
        Year = year;
    }

    public string Prefix { get; private set; }

    public int Year { get; private set; }

    public int Last { get; private set; }

    public uint RowVersion { get; private set; }

    public string Next()
    {
        Last++;
        return Format(Prefix, Year, Last);
    }

    public static string Format(string prefix, int year, int value) => $"{prefix}-{year}-{value:0000}";
}
