using BlueContract.Application.Common;
using Shouldly;

namespace BlueContract.Application.Tests.Common;

/// <summary>AUDIT-1 §13.8: the quantity ceilings of bids, contracts, appendices and call orders at their boundaries.</summary>
public class QuantityRulesTests
{
    [Theory]
    [InlineData(100, 0, 100, false)] // the whole bid quantity in one contract
    [InlineData(100, 0, 100.001, true)]
    [InlineData(100, 60, 40, false)] // what the other contracts left
    [InlineData(100, 60, 41, true)]
    [InlineData(100, 100, 0.5, true)] // nothing left
    [InlineData(100, 120, 0, false)] // over-contracted data never turns into a negative ceiling
    public void Contract_and_appendix_quantities_stay_within_the_bid(decimal bid, decimal elsewhere, decimal requested, bool exceeds) =>
        QuantityRules.ExceedsBid(requested, bid, elsewhere).ShouldBe(exceeds);

    [Fact]
    public void Remaining_bid_quantity_is_never_negative()
    {
        QuantityRules.BidRemaining(100, 30).ShouldBe(70);
        QuantityRules.BidRemaining(100, 130).ShouldBe(0);
    }

    [Theory]
    [InlineData(10, 0, 10, false)]
    [InlineData(10, 7, 3, false)] // exactly the rest of the contract
    [InlineData(10, 7, 4, true)]
    [InlineData(10, 10, 0.001, true)]
    [InlineData(12.5, 12, 0.5, false)] // decimal quantities (litres, kg…)
    public void All_calls_on_a_contract_item_stay_within_the_contract(decimal contract, decimal called, decimal newCall, bool exceeds) =>
        QuantityRules.CallExceedsContract(newCall, called, contract).ShouldBe(exceeds);
}
