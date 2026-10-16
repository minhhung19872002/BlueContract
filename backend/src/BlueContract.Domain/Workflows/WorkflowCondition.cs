using BlueContract.Domain.Common;

namespace BlueContract.Domain.Workflows;

/// <summary>
/// Điều kiện áp dụng của một luồng ký (spec §5.7): <c>Khóa=giá trị[,giá trị]; Khóa=…</c>, every clause must match, a clause
/// with several values matches any of them. Keys: <c>Domain</c> (Drug, MedicalSupply, Chemical, Equipment, Administration), <c>Facility</c> (mã cơ sở),
/// <c>ProcurementType</c> (Bidding / NonBidding), <c>ContractKind</c> (UnitPriceCallOff / LumpSum),
/// <c>RequiresWarehouseReceipt</c> (true / false). Keys and values are case-insensitive.
/// </summary>
public static class WorkflowCondition
{
    public const string Domain = "Domain";
    public const string Facility = "Facility";
    public const string ProcurementType = "ProcurementType";
    public const string ContractKind = "ContractKind";
    public const string RequiresWarehouseReceipt = "RequiresWarehouseReceipt";

    public static IReadOnlyList<string> Keys { get; } = [Domain, Facility, ProcurementType, ContractKind, RequiresWarehouseReceipt];

    /// <summary>Parsed clauses; throws <c>WORKFLOW_CONDITION_INVALID</c> on an unknown key or an empty value.</summary>
    public static IReadOnlyDictionary<string, string[]> Parse(string? condition)
    {
        var clauses = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(condition))
        {
            return clauses;
        }

        foreach (var part in condition.Split([';', '&'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split('=', 2, StringSplitOptions.TrimEntries);
            var key = Keys.FirstOrDefault(k => string.Equals(k, pieces[0], StringComparison.OrdinalIgnoreCase));
            var values = pieces.Length == 2 ? pieces[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
            if (key is null || values.Length == 0 || clauses.ContainsKey(key))
            {
                throw new DomainException("WORKFLOW_CONDITION_INVALID", part);
            }

            clauses[key] = values;
        }

        return clauses;
    }

    /// <summary>True when every clause matches the document's context (a key missing from the context does not match).</summary>
    public static bool Matches(string? condition, IReadOnlyDictionary<string, string?> context) =>
        Parse(condition).All(c => context.TryGetValue(c.Key, out var value) && value is not null
            && c.Value.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Specificity: a template with more clauses wins over a more general one.</summary>
    public static int Specificity(string? condition) => Parse(condition).Count;
}
