using BlueContract.Domain.Common;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Payments;
using BlueContract.Domain.Receipts;
using BlueContract.Domain.Workflows;
using Shouldly;

namespace BlueContract.Domain.Tests.Payments;

public class PaymentRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 10, 16);

    private static AcceptanceRecord Acceptance()
    {
        var a = new AcceptanceRecord("NT-1", Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "Tháng 09/2026", false);
        var item = Guid.NewGuid();
        a.SetContent("Tháng 09/2026", null,
        [
            new AcceptanceReceiptDraft(Guid.NewGuid(), "PN1", Day, ReceiptType.Receipt, "0001", 1_000_000),
            new AcceptanceReceiptDraft(Guid.NewGuid(), "HT1", Day, ReceiptType.Return, null, 200_000),
        ],
        [new AcceptanceItemDraft(item, "VT001", "Bơm tiêm", "Cái", 800, 1_000)]);
        return a;
    }

    [Fact]
    public void Acceptance_total_subtracts_returns_and_needs_receipts()
    {
        var a = Acceptance();
        a.TotalAmount.ShouldBe(800_000);
        a.Receipts.Single(r => r.Type == ReceiptType.Return).Amount.ShouldBe(-200_000);
        a.Items.Single().Amount.ShouldBe(800_000);
        Should.Throw<DomainException>(() => a.SetContent("x", null, [], [])).Code.ShouldBe("ACCEPTANCE_RECEIPTS_REQUIRED");
        Should.Throw<DomainException>(() => a.SetContent("x", null,
            [new AcceptanceReceiptDraft(Guid.NewGuid(), "HT2", Day, ReceiptType.Return, null, 5)], [])).Code.ShouldBe("ACCEPTANCE_AMOUNT_INVALID");
        Should.Throw<DomainException>(() => new AcceptanceRecord("NT", Guid.NewGuid(), Guid.NewGuid(), Day, Day.AddDays(-1), "x", false)).Code
            .ShouldBe("ACCEPTANCE_PERIOD_INVALID");
    }

    [Fact]
    public void Acceptance_goes_to_one_valuation_only_after_signing()
    {
        var a = Acceptance();
        var valuation = Guid.NewGuid();
        Should.Throw<DomainException>(() => a.ReserveForValuation(valuation)).Code.ShouldBe("ACCEPTANCE_NOT_COMPLETED");
        a.ApplyWorkflow(WorkflowStatus.Running, null, Now);
        a.Status.ShouldBe(PaymentRecordStatus.Signing);
        Should.Throw<DomainException>(() => a.SetContent("x", null, [], [])).Code.ShouldBe("ACCEPTANCE_LOCKED");
        a.ApplyWorkflow(WorkflowStatus.Completed, null, Now);
        a.ReserveForValuation(valuation);
        Should.Throw<DomainException>(() => a.ReserveForValuation(Guid.NewGuid())).Code.ShouldBe("ACCEPTANCE_ALREADY_IN_VALUATION");
        Should.Throw<DomainException>(a.Cancel).Code.ShouldBe("ACCEPTANCE_NOT_CANCELLABLE");
        a.ConfirmInValuation(valuation);
        a.IsIncludedInValuation.ShouldBeTrue();
        a.ReleaseFromValuation(valuation);
        a.ValuationId.ShouldBeNull();
        a.IsIncludedInValuation.ShouldBeFalse();
    }

    private static ValuationRecord Valuation(decimal previous = 2_000_000, decimal advance = 0, decimal recovered = 0)
    {
        var v = new ValuationRecord("XD-1", "DN-1", Guid.NewGuid(), Guid.NewGuid(), 2, "Tháng 09/2026");
        v.SetContent("Tháng 09/2026", new ValuationFigures(10_000_000, advance, previous, recovered),
            [new ValuationAcceptanceDraft(Guid.NewGuid(), "NT-1", "Tháng 09/2026", 3_000_000)],
            [new ValuationItemDraft(Guid.NewGuid(), "Bơm tiêm", "Cái", 10_000, 1_000, 2_000, 3_000)]);
        return v;
    }

    [Fact]
    public void Valuation_computes_cumulative_percent_and_requested_amount()
    {
        var v = Valuation(advance: 1_000_000, recovered: 500_000);
        v.CurrentPeriodValue.ShouldBe(3_000_000);
        v.CumulativeToDate.ShouldBe(5_000_000);
        v.CompletionPercent.ShouldBe(50);
        v.RequestedAmount.ShouldBe(2_500_000);
        var item = v.Items.Single();
        item.CumulativeQuantity.ShouldBe(5_000);
        item.PreviousAmount.ShouldBe(2_000_000);
        item.CurrentAmount.ShouldBe(3_000_000);
        item.ContractAmount.ShouldBe(10_000_000);
        Should.Throw<DomainException>(() => Valuation(advance: 100, recovered: 200)).Code.ShouldBe("VALUATION_ADVANCE_INVALID");
    }

    [Fact]
    public void Payment_orders_settle_the_valuation_and_never_exceed_the_request()
    {
        var v = Valuation();
        v.ApplyWorkflow(WorkflowStatus.Completed, null, Now);
        v.AddPaymentOrder("UNC-1", Day, 1_000_000, null, null, "Kế toán", Now);
        v.PaymentStatus.ShouldBe(PaymentStatus.Unpaid);
        Should.Throw<DomainException>(() => v.AddPaymentOrder("UNC-2", Day, 2_000_001, null, null, null, Now)).Code.ShouldBe("PAYMENT_ORDER_EXCEEDS_REQUESTED");
        var last = v.AddPaymentOrder("UNC-2", Day, 2_000_000, null, null, null, Now);
        v.PaymentStatus.ShouldBe(PaymentStatus.Paid);
        v.PaidAmount.ShouldBe(3_000_000);
        v.RemovePaymentOrder(last.Id);
        v.PaymentStatus.ShouldBe(PaymentStatus.Unpaid);

        var liquidation = Guid.NewGuid();
        v.ReserveForLiquidation(liquidation);
        Should.Throw<DomainException>(() => v.RemovePaymentOrder(v.PaymentOrders.First().Id)).Code.ShouldBe("VALUATION_LOCKED");
        v.MarkSettled(liquidation);
        v.PaymentStatus.ShouldBe(PaymentStatus.Paid);
        Should.Throw<DomainException>(() => v.AddPaymentOrder("UNC-3", Day, 1, null, null, null, Now)).Code.ShouldBe("VALUATION_LOCKED");
    }

    [Fact]
    public void Payment_request_status_follows_its_own_workflow()
    {
        var v = Valuation();
        v.ApplyPaymentRequestWorkflow(WorkflowStatus.Running);
        v.PaymentRequestStatus.ShouldBe(PaymentRecordStatus.Signing);
        v.Status.ShouldBe(PaymentRecordStatus.Draft);
        v.ApplyPaymentRequestWorkflow(WorkflowStatus.Completed);
        v.PaymentRequestStatus.ShouldBe(PaymentRecordStatus.Completed);
    }

    [Fact]
    public void Liquidation_reconciles_and_settles_debts_with_vouchers()
    {
        var valuationId = Guid.NewGuid();
        var l = new LiquidationRecord("TL-1", Guid.NewGuid(), Guid.NewGuid());
        l.SetFigures(10_000_000, null, [new LiquidationValuationDraft(valuationId, "XD-1", Day, 8_000_000, 7_000_000, "UNC-1")]);
        l.ExecutedValue.ShouldBe(8_000_000);
        l.PaidAmount.ShouldBe(7_000_000);
        l.RemainingAmount.ShouldBe(1_000_000);
        l.ReductionAmount.ShouldBe(2_000_000);

        Should.Throw<DomainException>(() => l.AddSettlement(valuationId, "x", 1_000_001, null, null, null, null, Guid.NewGuid(), null, Now)).Code
            .ShouldBe("SETTLEMENT_EXCEEDS_REMAINING");
        Should.Throw<DomainException>(() => l.AddSettlement(Guid.NewGuid(), "x", 1, null, null, null, null, Guid.NewGuid(), null, Now)).Code
            .ShouldBe("SETTLEMENT_VALUATION_NOT_FOUND");
        var user = Guid.NewGuid();
        var settlement = l.AddSettlement(valuationId, "Bù trừ công nợ", 1_000_000, "CT-01", Day, null, null, user, "Kế toán", Now);
        settlement.MarkedBy.ShouldBe(user);
        l.RemainingAmount.ShouldBe(0);

        l.ApplyWorkflow(WorkflowStatus.Running, null, Now);
        Should.Throw<DomainException>(() => l.RemoveSettlement(settlement.Id)).Code.ShouldBe("LIQUIDATION_LOCKED");
        l.ApplyWorkflow(WorkflowStatus.Completed, null, Now);
        Should.Throw<DomainException>(l.Cancel).Code.ShouldBe("LIQUIDATION_NOT_CANCELLABLE");
    }

    [Fact]
    public void Only_effective_contracts_are_liquidated()
    {
        var c = new Contract("18/2026", "x", ProcurementType.NonBidding, ContractKind.LumpSum, Guid.NewGuid(), Guid.NewGuid(), null);
        Should.Throw<DomainException>(() => c.Liquidate(Now)).Code.ShouldBe("CONTRACT_NOT_EFFECTIVE");
        c.SetItems([new ContractItemDraft(Guid.NewGuid(), "A", "A", "Cái", 1, 1, 0, null)]);
        c.ApplyWorkflow(WorkflowStatus.Completed, null, null, Now);
        c.Liquidate(Now);
        c.Status.ShouldBe(ContractStatus.Liquidated);
        c.LiquidatedAt.ShouldBe(Now);
        c.ApplyWorkflow(WorkflowStatus.Running, null, null, Now); // ignored once closed
        c.Status.ShouldBe(ContractStatus.Liquidated);
    }
}
