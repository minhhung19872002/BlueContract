using BlueContract.Application.Receipts.Validation;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Receipts;
using BlueContract.Domain.Workflows;
using Shouldly;

namespace BlueContract.Application.Tests.Receipts;

/// <summary>Validation engine of warehouse receipts (spec §5.5, §13): one test group per rule.</summary>
public class ReceiptValidationTests
{
    private static readonly DateOnly Day = new(2026, 10, 16);
    private static readonly ReceiptValidator Engine = new(ReceiptValidator.DefaultRules);

    private static Contract EffectiveContract(DateOnly? expiry = null, ContractKind kind = ContractKind.UnitPriceCallOff, decimal called = 500)
    {
        var c = new Contract("16/2026/HĐ", "VTYT", ProcurementType.NonBidding, kind, Guid.NewGuid(), Guid.NewGuid(), null);
        c.UpdateTerms(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1), expiry ?? new DateOnly(2027, 2, 28), null, null, null, null, null, null, null, null);
        c.SetItems(
        [
            new ContractItemDraft(Guid.NewGuid(), "VT001", "Bơm tiêm 5ml", "Cái", 1000, 850, 5, null),
            new ContractItemDraft(Guid.NewGuid(), "VT002", "Bơm tiêm 10ml", "Cái", 200, 1150, 5, null),
        ]);
        c.ApplyWorkflow(WorkflowStatus.Completed, null, null, DateTimeOffset.UtcNow);
        foreach (var item in c.Items)
        {
            item.Track(0, 0, called, 0, 0, 0);
        }

        return c;
    }

    private static WarehouseReceipt Receipt(Contract? contract, params (string Code, decimal Qty, decimal Price, DateOnly? Expiry)[] lines) =>
        Receipt(contract, ReceiptType.Receipt, lines);

    private static WarehouseReceipt Receipt(Contract? contract, ReceiptType type, params (string Code, decimal Qty, decimal Price, DateOnly? Expiry)[] lines)
    {
        var r = new WarehouseReceipt("PN1", type, ReceiptSource.His, "HIS-1", Day, "KV01", null, "0302345678", "126/QĐ");
        r.SetLines([.. lines.Select(l => new ReceiptLineDraft(null, l.Code, l.Code, "Cái", "L1", l.Expiry, l.Qty, l.Price, 5))]);
        if (contract is not null)
        {
            foreach (var line in r.Lines)
            {
                var item = contract.Items.FirstOrDefault(i => i.ItemCode == line.ItemCode);
                r.MapLine(line.LineNo, item?.ItemId, item?.Id);
            }

            r.MapParties(contract.SupplierId, contract.Id, contract.Number, null);
        }

        return r;
    }

    private static ReceiptValidationContext Context(WarehouseReceipt r, Contract? c, Dictionary<Guid, decimal>? received = null, bool supplier = true,
        IReadOnlyDictionary<Guid, decimal>? callOrder = null, IReadOnlyList<string>? duplicates = null) => new()
        {
            Receipt = r,
            Contract = c,
            SupplierFound = supplier,
            ReceivedByOthers = received ?? [],
            CallOrderOutstanding = callOrder,
            CallOrderNumber = callOrder is null ? null : "GH-2026-0001",
            DuplicateInvoiceReceipts = duplicates ?? [],
            InvoiceRoundingTolerance = 1000,
            ExpiryWarningDays = 180,
        };

    private static List<string> Codes(ReceiptValidationContext context) => [.. Engine.Validate(context).Select(i => i.Code)];

    private static void WithInvoice(WarehouseReceipt r, decimal? total = null, string? seller = "0302345678") =>
        r.SetInvoice(new InvoiceInfo("1C26TAA", "0000123", Day, null, null, total ?? r.TotalAmount, seller), "test", DateTimeOffset.UtcNow);

    [Fact]
    public void A_receipt_matching_the_contract_with_its_invoice_is_valid()
    {
        var c = EffectiveContract();
        var r = Receipt(c, ("VT001", 100, 850, Day.AddYears(2)));
        WithInvoice(r);
        Engine.Validate(Context(r, c)).ShouldBeEmpty();
    }

    [Fact]
    public void Unknown_supplier_or_contract_is_invalid()
    {
        var r = Receipt(null, ("VT001", 1, 850, null));
        Codes(Context(r, null, supplier: false)).ShouldContain("SUPPLIER_NOT_FOUND");
        var issues = Engine.Validate(Context(r, null));
        issues.Single(i => i.Code == "CONTRACT_NOT_FOUND").Severity.ShouldBe(ReceiptValidationStatus.Invalid);
        issues.Single(i => i.Code == "CONTRACT_NOT_FOUND").Message.ShouldContain("126/QĐ");
    }

    [Fact]
    public void Expired_contract_warns_and_liquidated_contract_is_invalid()
    {
        var expired = EffectiveContract(expiry: Day.AddDays(-1));
        var r = Receipt(expired, ("VT001", 1, 850, null));
        Engine.Validate(Context(r, expired)).Single(i => i.Code == "CONTRACT_EXPIRED").Severity.ShouldBe(ReceiptValidationStatus.Warning);

        var draft = new Contract("17/2026", "x", ProcurementType.NonBidding, ContractKind.LumpSum, Guid.NewGuid(), Guid.NewGuid(), null);
        draft.SetItems([new ContractItemDraft(Guid.NewGuid(), "VT001", "x", "Cái", 1, 850, 5, null)]);
        Codes(Context(Receipt(draft, ("VT001", 1, 850, null)), draft)).ShouldContain("CONTRACT_NOT_EFFECTIVE");
    }

    [Fact]
    public void Item_outside_the_contract_is_invalid()
    {
        var c = EffectiveContract();
        var issues = Engine.Validate(Context(Receipt(c, ("VT001", 1, 850, null), ("XX999", 1, 10, null)), c));
        var issue = issues.Single(i => i.Code == "ITEM_NOT_IN_CONTRACT");
        issue.Severity.ShouldBe(ReceiptValidationStatus.Invalid);
        issue.LineNo.ShouldBe(2);
    }

    [Fact]
    public void Price_above_contract_is_invalid_and_below_is_a_warning()
    {
        var c = EffectiveContract();
        Engine.Validate(Context(Receipt(c, ("VT001", 1, 851, null)), c)).Single(i => i.Code == "UNIT_PRICE_ABOVE_CONTRACT").Severity
            .ShouldBe(ReceiptValidationStatus.Invalid);
        Engine.Validate(Context(Receipt(c, ("VT001", 1, 800, null)), c)).Single(i => i.Code == "UNIT_PRICE_MISMATCH").Severity
            .ShouldBe(ReceiptValidationStatus.Warning);
    }

    [Fact]
    public void Quantity_beyond_the_contract_remaining_is_invalid_counting_other_receipts()
    {
        var c = EffectiveContract(called: 5000);
        var item = c.Items.First(i => i.ItemCode == "VT001");
        var r = Receipt(c, ("VT001", 300, 850, null), ("VT001", 200, 850, null));
        Codes(Context(r, c, new() { [item.Id] = 500 })).ShouldNotContain("QTY_EXCEEDS_CONTRACT");
        var issue = Engine.Validate(Context(r, c, new() { [item.Id] = 501 })).Single(i => i.Code == "QTY_EXCEEDS_CONTRACT");
        issue.Severity.ShouldBe(ReceiptValidationStatus.Invalid);
        issue.Message.ShouldContain("499"); // còn lại 1000 − 501
    }

    [Fact]
    public void A_return_cannot_exceed_what_was_received()
    {
        var c = EffectiveContract();
        var item = c.Items.First(i => i.ItemCode == "VT001");
        var r = Receipt(c, ReceiptType.Return, ("VT001", 10, 850, null));
        Codes(Context(r, c, new() { [item.Id] = 10 })).ShouldNotContain("RETURN_EXCEEDS_RECEIVED");
        Codes(Context(r, c, new() { [item.Id] = 9 })).ShouldContain("RETURN_EXCEEDS_RECEIVED");
        Codes(Context(r, c)).ShouldNotContain("INVOICE_TOTAL_MISMATCH");
    }

    [Fact]
    public void Exceeding_the_called_quantity_is_a_warning()
    {
        var c = EffectiveContract(called: 100);
        var item = c.Items.First(i => i.ItemCode == "VT001");
        Codes(Context(Receipt(c, ("VT001", 60, 850, null)), c, new() { [item.Id] = 40 })).ShouldNotContain("QTY_EXCEEDS_CALLED");
        Engine.Validate(Context(Receipt(c, ("VT001", 61, 850, null)), c, new() { [item.Id] = 40 })).Single(i => i.Code == "QTY_EXCEEDS_CALLED").Severity
            .ShouldBe(ReceiptValidationStatus.Warning);

        // With a linked call order, its outstanding quantity is the limit.
        var issue = Engine.Validate(Context(Receipt(c, ("VT001", 30, 850, null)), c, callOrder: new Dictionary<Guid, decimal> { [item.Id] = 20 }))
            .Single(i => i.Code == "QTY_EXCEEDS_CALLED");
        issue.Message.ShouldContain("GH-2026-0001");

        // Lump-sum contracts are not called off.
        var lump = EffectiveContract(kind: ContractKind.LumpSum, called: 0);
        Codes(Context(Receipt(lump, ("VT001", 61, 850, null)), lump)).ShouldNotContain("QTY_EXCEEDS_CALLED");
    }

    [Fact]
    public void Missing_invoice_and_invoice_total_mismatch_beyond_tolerance_are_warnings()
    {
        var c = EffectiveContract();
        var r = Receipt(c, ("VT001", 100, 850, null)); // 85 000 ₫
        Codes(Context(r, c)).ShouldContain("INVOICE_MISSING");

        WithInvoice(r, 85_000 + 1000);
        Codes(Context(r, c)).ShouldNotContain("INVOICE_TOTAL_MISMATCH"); // within the rounding tolerance
        WithInvoice(r, 85_000 + 1001);
        Codes(Context(r, c)).ShouldContain("INVOICE_TOTAL_MISMATCH");
        WithInvoice(r, 85_000, seller: "0109999999");
        Codes(Context(r, c)).ShouldContain("INVOICE_SELLER_MISMATCH");
    }

    [Fact]
    public void Expired_lots_are_invalid_and_short_expiry_is_a_warning()
    {
        var c = EffectiveContract();
        Engine.Validate(Context(Receipt(c, ("VT001", 1, 850, Day)), c)).Single(i => i.Code == "ITEM_EXPIRED").Severity.ShouldBe(ReceiptValidationStatus.Invalid);
        Engine.Validate(Context(Receipt(c, ("VT001", 1, 850, Day.AddDays(179))), c)).Single(i => i.Code == "EXPIRY_TOO_SOON").Severity
            .ShouldBe(ReceiptValidationStatus.Warning);
        Codes(Context(Receipt(c, ("VT001", 1, 850, Day.AddDays(180))), c)).ShouldNotContain("EXPIRY_TOO_SOON");
    }

    [Fact]
    public void Reused_invoice_number_is_invalid()
    {
        var c = EffectiveContract();
        var r = Receipt(c, ("VT001", 1, 850, null));
        WithInvoice(r);
        var issue = Engine.Validate(Context(r, c, duplicates: ["PN000099"])).Single(i => i.Code == "INVOICE_DUPLICATE");
        issue.Severity.ShouldBe(ReceiptValidationStatus.Invalid);
        issue.Message.ShouldContain("PN000099");
    }

    [Fact]
    public void Overall_status_follows_the_most_severe_rule()
    {
        var c = EffectiveContract();
        var r = Receipt(c, ("VT001", 1, 800, Day.AddDays(30)));
        r.ApplyValidation(Engine.Validate(Context(r, c)), DateTimeOffset.UtcNow);
        r.ValidationStatus.ShouldBe(ReceiptValidationStatus.Warning);

        var bad = Receipt(c, ("VT001", 1, 900, null));
        bad.ApplyValidation(Engine.Validate(Context(bad, c)), DateTimeOffset.UtcNow);
        bad.ValidationStatus.ShouldBe(ReceiptValidationStatus.Invalid);
    }
}
