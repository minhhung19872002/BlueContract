namespace BlueContract.Domain.Common;

/// <summary>
/// Per-year counters for document numbers (DX-2026-0001...). The row's xmin makes concurrent allocations conflict
/// instead of producing duplicates; the unique index on the generated numbers is the final guard.
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
        return $"{Prefix}-{Year}-{Last:0000}";
    }
}
