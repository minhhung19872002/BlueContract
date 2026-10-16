namespace BlueContract.Application.Integration;

/// <summary>Hàng hóa trúng thầu as kept by the HIS for (decision number + supplier tax code) — spec §7.1.</summary>
public sealed record HisItem(
    string HisKey,
    string Code,
    string Name,
    string? ActiveIngredient,
    string? Strength,
    string? Specification,
    string Unit,
    string? Manufacturer,
    string? Country,
    string? RegistrationNumber,
    string? BidGroup,
    string? CategoryCode,
    decimal Quantity,
    decimal UnitPrice);

/// <summary>Xuất – Nhập – Tồn of one item in a warehouse over a period.</summary>
public sealed record HisStockMovement(string HisKey, decimal Opening, decimal Received, decimal Issued, decimal Closing);

/// <summary>
/// Outbound calls from the portal to the hospital information system (spec §7.1). Implementations: a deterministic
/// mock for demo/tests and a configurable REST client with retry + circuit breaker for real HIS vendors.
/// </summary>
public interface IHisClient
{
    /// <summary>Name shown in the UI ("HIS giả lập" / vendor name).</summary>
    string DisplayName { get; }

    Task<IReadOnlyList<HisItem>> GetItemsByBidDecisionAsync(string bidDecisionNumber, string supplierTaxCode, string? facilityCode, CancellationToken ct);

    Task<IReadOnlyList<HisStockMovement>> GetStockMovementAsync(string warehouseCode, IReadOnlyCollection<string> hisKeys, DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>The HIS could not be reached or answered with an error; surfaced to users as HIS_UNAVAILABLE.</summary>
public sealed class HisUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
