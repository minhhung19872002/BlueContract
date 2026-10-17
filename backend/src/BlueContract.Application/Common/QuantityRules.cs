namespace BlueContract.Application.Common;

/// <summary>
/// The quantity ceilings of the procurement chain (spec §5–6), as pure functions so they are unit-tested on their own:
/// what a supplier won in the bid caps its contracts and appendices; a contract caps every call order on it.
/// </summary>
public static class QuantityRules
{
    /// <summary>Bid quantity of the supplier not yet taken by its other (non-cancelled) contracts; never negative.</summary>
    public static decimal BidRemaining(decimal bidQuantity, decimal contractedElsewhere) => Math.Max(0, bidQuantity - contractedElsewhere);

    /// <summary>A contract line / appendix quantity above what the bid still allows (equal is allowed).</summary>
    public static bool ExceedsBid(decimal requested, decimal bidQuantity, decimal contractedElsewhere) =>
        requested > BidRemaining(bidQuantity, contractedElsewhere);

    /// <summary>
    /// Calls of every summary on one contract item stay within the contract quantity: confirmed (or, until confirmed,
    /// called) quantities of live orders plus the new call.
    /// </summary>
    public static bool CallExceedsContract(decimal newCall, decimal calledOnLiveOrders, decimal contractQuantity) =>
        calledOnLiveOrders + newCall > contractQuantity;
}
