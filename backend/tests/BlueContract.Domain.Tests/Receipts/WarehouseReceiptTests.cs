using BlueContract.Domain.Common;
using BlueContract.Domain.Proposals;
using BlueContract.Domain.Receipts;
using BlueContract.Domain.Workflows;
using Shouldly;

namespace BlueContract.Domain.Tests.Receipts;

public class WarehouseReceiptTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 3, 0, 0, TimeSpan.Zero);

    private static WarehouseReceipt Receipt(ReceiptSource source = ReceiptSource.His, ReceiptType type = ReceiptType.Receipt)
    {
        var r = new WarehouseReceipt("PN001", type, source, source == ReceiptSource.His ? "HIS-1" : null, new DateOnly(2026, 10, 16), "KV01", null, "0302345678", "126/QĐ");
        r.SetLines(
        [
            new ReceiptLineDraft("K1", "VT001", "Bơm tiêm 5ml", "Cái", "L01", new DateOnly(2028, 1, 1), 1000, 850, 5),
            new ReceiptLineDraft("K2", "VT002", "Bơm tiêm 10ml", "Cái", "L02", null, 3, 1_150.5m, 10),
        ]);
        return r;
    }

    [Fact]
    public void Lines_are_numbered_and_vat_is_extracted_from_prices_that_include_it()
    {
        var r = Receipt();
        r.Lines.Select(l => l.LineNo).ShouldBe([1, 2]);
        var first = r.Lines.First();
        first.Amount.ShouldBe(850_000);
        first.VatAmount.ShouldBe(40_476); // 850 000 × 5 / 105
        r.Lines.Last().Amount.ShouldBe(3_452); // 3 × 1 150,5 rounded
        r.TotalAmount.ShouldBe(853_452);
        r.SignedAmount.ShouldBe(853_452);
    }

    [Fact]
    public void Return_receipts_count_negatively()
    {
        var r = Receipt(type: ReceiptType.Return);
        r.SignedAmount.ShouldBe(-r.TotalAmount);
    }

    [Fact]
    public void His_receipts_need_the_his_id_and_lines_must_be_positive()
    {
        Should.Throw<DomainException>(() => new WarehouseReceipt("PN", ReceiptType.Receipt, ReceiptSource.His, " ", new DateOnly(2026, 1, 1), "KV01", null, "0101", null))
            .Code.ShouldBe("RECEIPT_HIS_ID_REQUIRED");
        var r = Receipt();
        Should.Throw<DomainException>(() => r.SetLines([new ReceiptLineDraft(null, "X", "X", "Cái", null, null, 0, 1, 0)])).Code.ShouldBe("RECEIPT_QUANTITY_INVALID");
        Should.Throw<DomainException>(() => r.SetLines([])).Code.ShouldBe("RECEIPT_LINES_REQUIRED");
    }

    [Fact]
    public void Validation_status_is_the_most_severe_issue()
    {
        var r = Receipt();
        r.ApplyValidation([], Now);
        r.ValidationStatus.ShouldBe(ReceiptValidationStatus.Valid);
        r.IssueCount.ShouldBe(0);

        r.ApplyValidation([new("INVOICE_MISSING", ReceiptValidationStatus.Warning, "w"), new("ITEM_NOT_IN_CONTRACT", ReceiptValidationStatus.Invalid, "e", 2)], Now);
        r.ValidationStatus.ShouldBe(ReceiptValidationStatus.Invalid);
        r.IssueCount.ShouldBe(2);
        r.Issues[0].Severity.ShouldBe(ReceiptValidationStatus.Invalid); // most severe first
    }

    [Fact]
    public void Invoice_is_required_fields_and_locked_after_completion()
    {
        var r = Receipt();
        r.HasInvoice.ShouldBeFalse();
        Should.Throw<DomainException>(() => r.SetInvoice(new InvoiceInfo("1C26TAA", "1", new DateOnly(2026, 10, 16), null, null, 0, null), "x", Now))
            .Code.ShouldBe("INVOICE_TOTAL_INVALID");
        r.SetInvoice(new InvoiceInfo("1c26taa", "0000123", new DateOnly(2026, 10, 16), null, null, 853_452, "0302345678"), "NCC", Now);
        r.HasInvoice.ShouldBeTrue();
        r.InvoiceSymbol.ShouldBe("1C26TAA");

        r.ApplyWorkflow(WorkflowStatus.Running, null, Now);
        r.ApplyWorkflow(WorkflowStatus.Completed, null, Now);
        r.Status.ShouldBe(ReceiptStatus.Completed);
        Should.Throw<DomainException>(() => r.SetInvoice(new InvoiceInfo("1C26TAA", "2", new DateOnly(2026, 10, 16), null, null, 1, null), "x", Now))
            .Code.ShouldBe("RECEIPT_LOCKED");
        Should.Throw<DomainException>(() => r.SetLines([new ReceiptLineDraft(null, "X", "X", "Cái", null, null, 1, 1, 0)])).Code.ShouldBe("RECEIPT_LOCKED");
    }

    [Fact]
    public void Workflow_states_map_onto_the_receipt()
    {
        var r = Receipt();
        r.CountsAsReceived.ShouldBeTrue(); // HIS draft: goods are in the warehouse
        r.ApplyWorkflow(WorkflowStatus.Running, null, Now);
        r.Status.ShouldBe(ReceiptStatus.Signing);
        r.ApplyWorkflow(WorkflowStatus.Returned, "Sai số lô", Now);
        r.Status.ShouldBe(ReceiptStatus.Returned);
        r.LastReturnReason.ShouldBe("Sai số lô");
        r.ApplyWorkflow(WorkflowStatus.Completed, null, Now);
        r.CompletedAt.ShouldBe(Now);

        var portal = Receipt(ReceiptSource.Portal);
        portal.CountsAsReceived.ShouldBeFalse(); // portal draft not sent yet
    }

    [Fact]
    public void Virtual_receipts_complete_without_signing_unless_invalid()
    {
        Should.Throw<DomainException>(() => Receipt(ReceiptSource.Portal).CompleteVirtual(Now)).Code.ShouldBe("RECEIPT_NOT_VIRTUAL");

        var invalid = Receipt(ReceiptSource.VirtualPortal);
        invalid.ApplyValidation([new("QTY_EXCEEDS_CONTRACT", ReceiptValidationStatus.Invalid, "x")], Now);
        Should.Throw<DomainException>(() => invalid.CompleteVirtual(Now)).Code.ShouldBe("RECEIPT_INVALID_BLOCKS_SIGNING");

        var ok = Receipt(ReceiptSource.VirtualPortal);
        ok.ApplyValidation([new("INVOICE_MISSING", ReceiptValidationStatus.Warning, "x")], Now);
        ok.CompleteVirtual(Now);
        ok.Status.ShouldBe(ReceiptStatus.Completed);
        ok.IsVirtual.ShouldBeTrue();
    }

    [Fact]
    public void Acceptance_takes_only_completed_receipts_once()
    {
        var r = Receipt();
        var acceptance = Guid.NewGuid();
        Should.Throw<DomainException>(() => r.IncludeInAcceptance(acceptance)).Code.ShouldBe("RECEIPT_NOT_COMPLETED");
        r.ApplyWorkflow(WorkflowStatus.Completed, null, Now);
        r.IncludeInAcceptance(acceptance);
        r.IncludeInAcceptance(acceptance); // idempotent
        Should.Throw<DomainException>(() => r.IncludeInAcceptance(Guid.NewGuid())).Code.ShouldBe("RECEIPT_ALREADY_ACCEPTED");
        Should.Throw<DomainException>(r.Cancel).Code.ShouldBe("RECEIPT_NOT_CANCELLABLE");
        r.ReleaseFromAcceptance(acceptance);
        r.IsIncludedInAcceptance.ShouldBeFalse();
    }

    [Fact]
    public void Unmapping_the_contract_clears_line_mapping()
    {
        var r = Receipt();
        r.MapLine(1, Guid.NewGuid(), Guid.NewGuid());
        r.MapParties(Guid.NewGuid(), Guid.NewGuid(), "16/2026", Guid.NewGuid());
        r.Lines.First().ContractItemId.ShouldNotBeNull();
        r.MapParties(Guid.NewGuid(), null, "x", Guid.NewGuid());
        r.ContractNumber.ShouldBeNull();
        r.CallOrderId.ShouldBeNull();
        r.Lines.ShouldAllBe(l => l.ContractItemId == null);
    }

    [Fact]
    public void Call_order_delivery_is_recomputed_and_can_reopen()
    {
        var item = Guid.NewGuid();
        var order = new CallOrder("GH-1", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5), null, null);
        order.AddLines([new CallOrderLineDraft(Guid.NewGuid(), Guid.NewGuid(), "16", item, "VT001", "Bơm tiêm", "Cái", 850, 100)]);
        order.Confirm([new CallConfirmation(order.Lines.First().Id, 80, "Thiếu hàng")], Guid.NewGuid(), "NCC", null, Now);
        order.Status.ShouldBe(CallOrderStatus.PartiallyConfirmed);

        order.SetDelivered(item, 50);
        order.Status.ShouldBe(CallOrderStatus.PartiallyConfirmed);
        order.SetDelivered(item, 80);
        order.Status.ShouldBe(CallOrderStatus.Delivered);
        order.SetDelivered(item, 30); // a receipt was cancelled
        order.Status.ShouldBe(CallOrderStatus.PartiallyConfirmed);
        order.Lines.First().DeliveredQuantity.ShouldBe(30);
        order.SetDelivered(Guid.NewGuid(), 5); // other items are ignored
    }
}
