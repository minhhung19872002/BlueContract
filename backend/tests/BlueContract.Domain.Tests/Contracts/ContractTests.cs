using BlueContract.Domain.Bids;
using BlueContract.Domain.Common;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Workflows;
using Shouldly;

namespace BlueContract.Domain.Tests.Contracts;

public class ContractTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 2, 0, 0, TimeSpan.Zero);
    private static readonly Guid Supplier = Guid.NewGuid();
    private static readonly Guid Category = Guid.NewGuid();

    private static ContractItemDraft Line(decimal qty, decimal price, decimal vat = 5, decimal? max = null, Guid? itemId = null) =>
        new(itemId ?? Guid.NewGuid(), "TH001", "Paracetamol 500mg", "Viên", qty, price, vat, max);

    private static Contract NewContract(params ContractItemDraft[] lines)
    {
        var c = new Contract("15/2026/HĐ-BV", "Cung cấp thuốc", ProcurementType.Bidding, ContractKind.UnitPriceCallOff, Supplier, Category, Guid.NewGuid());
        c.SetItems(lines.Length == 0 ? [Line(200_000, 450)] : lines);
        return c;
    }

    private static WorkflowInstance ContractWorkflow()
    {
        var t = new WorkflowTemplate("WF_CONTRACT", "Ký hợp đồng", DocumentType.Contract, null, true);
        t.SetSteps(
        [
            new("REVIEW", "Rà soát", WorkflowActionType.Review, AssigneeType.Role, "REVIEWER", null, true, false, [0], null, 24),
            new("SUPPLIER_REVIEW", "NCC rà soát", WorkflowActionType.SupplierReview, AssigneeType.DocumentSupplier, null, null, true, false, [0], null, 48),
            new("ACCOUNTANT_REVIEW", "Kế toán", WorkflowActionType.Review, AssigneeType.Role, "ACCOUNTANT", null, true, true, [0, 2], null, 24),
            new("SUPPLIER", "NCC ký", WorkflowActionType.SupplierSign, AssigneeType.DocumentSupplier, null, null, false, false, [0], null, 48),
            new("ACCOUNTANT_INITIAL", "Ký nháy", WorkflowActionType.Initial, AssigneeType.Role, "ACCOUNTANT", null, false, false, [0], null, 24),
            new("DIRECTOR", "GĐ ký", WorkflowActionType.Sign, AssigneeType.Role, "DIRECTOR", null, false, false, [0, 5], null, 24),
            new("CLERK_SEAL", "Đóng dấu", WorkflowActionType.OrgSeal, AssigneeType.Role, "CLERK", null, false, false, [6], null, 8),
        ]);
        return new WorkflowInstance(Guid.NewGuid(), t, Guid.NewGuid(), "Mua sắm", Supplier, T0);
    }

    [Fact]
    public void Totals_treat_unit_prices_as_vat_inclusive()
    {
        var c = NewContract(Line(200_000, 450), Line(150_000, 1_150, vat: 10));

        c.TotalAmount.ShouldBe(90_000_000m + 172_500_000m);
        c.Items.First().VatAmount.ShouldBe(4_285_714m); // 90.000.000 × 5/105
        c.Items.Last().VatAmount.ShouldBe(15_681_818m); // 172.500.000 × 10/110
        c.VatAmount.ShouldBe(4_285_714m + 15_681_818m);
        c.AmountBeforeVat.ShouldBe(c.TotalAmount - c.VatAmount);
    }

    [Fact]
    public void Unit_price_may_not_exceed_the_winning_price()
    {
        var c = new Contract("1", "HĐ", ProcurementType.Bidding, ContractKind.LumpSum, Supplier, Category, Guid.NewGuid());

        Should.Throw<DomainException>(() => c.SetItems([Line(10, 451, max: 450)])).Code.ShouldBe("CONTRACT_PRICE_EXCEEDS_BID");
        Should.NotThrow(() => c.SetItems([Line(10, 450, max: 450)]));
    }

    [Theory]
    [InlineData(0, 450, "CONTRACT_QUANTITY_INVALID")]
    [InlineData(10, -1, "CONTRACT_PRICE_INVALID")]
    public void Invalid_lines_are_rejected(decimal qty, decimal price, string code)
    {
        var c = new Contract("1", "HĐ", ProcurementType.NonBidding, ContractKind.LumpSum, Supplier, Category, null);
        Should.Throw<DomainException>(() => c.SetItems([Line(qty, price)])).Code.ShouldBe(code);
    }

    [Fact]
    public void Duplicate_items_and_empty_lists_are_rejected()
    {
        var c = new Contract("1", "HĐ", ProcurementType.NonBidding, ContractKind.LumpSum, Supplier, Category, null);
        var id = Guid.NewGuid();

        Should.Throw<DomainException>(() => c.SetItems([])).Code.ShouldBe("CONTRACT_ITEMS_REQUIRED");
        Should.Throw<DomainException>(() => c.SetItems([Line(1, 1, itemId: id), Line(2, 2, itemId: id)])).Code.ShouldBe("CONTRACT_ITEM_DUPLICATE");
    }

    [Fact]
    public void Bidding_contract_requires_a_bid_decision()
    {
        Should.Throw<DomainException>(() => new Contract("1", "HĐ", ProcurementType.Bidding, ContractKind.LumpSum, Supplier, Category, null))
            .Code.ShouldBe("CONTRACT_BID_REQUIRED");
    }

    [Fact]
    public void Status_follows_the_workflow_and_the_contract_locks_while_signing()
    {
        var c = NewContract();
        var wf = ContractWorkflow();
        c.ApplyWorkflow(wf.Status, wf.ActiveStep, null, T0);
        c.Status.ShouldBe(ContractStatus.InReview);
        c.IsEditable.ShouldBeFalse();
        Should.Throw<DomainException>(() => c.SetItems([Line(1, 1)])).Code.ShouldBe("CONTRACT_LOCKED");

        var expected = new[]
        {
            ContractStatus.SupplierReview, ContractStatus.SupplierSigning, ContractStatus.AccountantInitialing,
            ContractStatus.DirectorSigning, ContractStatus.Stamping,
        };
        var actors = new[] { "REVIEW", "SUPPLIER_REVIEW", "SUPPLIER", "ACCOUNTANT_INITIAL", "DIRECTOR" };
        for (var i = 0; i < actors.Length; i++)
        {
            var step = wf.Steps.Single(s => s.Code == actors[i]);
            wf.Act(step.Id, Guid.NewGuid(), "X", null, null, step.RequiresSignature ? Guid.NewGuid() : null, T0, skipNextOptional: actors[i] == "SUPPLIER_REVIEW");
            c.ApplyWorkflow(wf.Status, wf.ActiveStep, null, T0);
            c.Status.ShouldBe(expected[i]);
        }

        var seal = wf.Steps.Single(s => s.Code == "CLERK_SEAL");
        wf.Act(seal.Id, Guid.NewGuid(), "VT", null, null, Guid.NewGuid(), T0.AddHours(3)).ShouldBe(ActOutcome.Completed);
        c.ApplyWorkflow(wf.Status, wf.ActiveStep, null, T0.AddHours(3));
        c.Status.ShouldBe(ContractStatus.Effective);
        c.EffectiveAt.ShouldBe(T0.AddHours(3));
        c.SignedDate.ShouldBe(new DateOnly(2026, 10, 16));
        c.EffectiveDate.ShouldBe(c.SignedDate);
    }

    [Fact]
    public void Supplier_changes_go_to_the_accountant_and_returns_record_the_reason()
    {
        var c = NewContract();
        var wf = ContractWorkflow();
        wf.Act(wf.ActiveStep!.Id, Guid.NewGuid(), "RS", null, null, null, T0);
        wf.Act(wf.ActiveStep!.Id, Guid.NewGuid(), "NCC", null, "Gửi lại", null, T0, skipNextOptional: false);
        c.ApplyWorkflow(wf.Status, wf.ActiveStep, null, T0);
        c.Status.ShouldBe(ContractStatus.AccountantReview);

        wf.Return(wf.ActiveStep!.Id, 2, Guid.NewGuid(), "KT", "Không chấp nhận giảm hạn dùng", T0);
        c.ApplyWorkflow(wf.Status, wf.ActiveStep, "Không chấp nhận giảm hạn dùng", T0);
        c.Status.ShouldBe(ContractStatus.SupplierReview);
        c.LastReturnReason.ShouldBe("Không chấp nhận giảm hạn dùng");

        wf.Return(wf.ActiveStep!.Id, 0, Guid.NewGuid(), "NCC", "Sai tên hàng", T0);
        c.ApplyWorkflow(wf.Status, wf.ActiveStep, "Sai tên hàng", T0);
        c.Status.ShouldBe(ContractStatus.Returned);
        c.IsEditable.ShouldBeTrue();
    }

    [Fact]
    public void Custom_steps_map_by_action_type()
    {
        Contract.StatusForStep("CUSTOM", WorkflowActionType.Approve).ShouldBe(ContractStatus.InReview);
        Contract.StatusForStep("NCC_KY", WorkflowActionType.SupplierSign).ShouldBe(ContractStatus.SupplierSigning);
        Contract.StatusForStep("PGD", WorkflowActionType.Sign).ShouldBe(ContractStatus.DirectorSigning);
        Contract.StatusForStep("DAU", WorkflowActionType.OrgSeal).ShouldBe(ContractStatus.Stamping);
    }

    [Fact]
    public void Effective_contracts_cannot_be_cancelled_but_drafts_can()
    {
        var draft = NewContract();
        draft.Cancel(T0);
        draft.Status.ShouldBe(ContractStatus.Cancelled);

        var effective = NewContract();
        effective.ApplyWorkflow(WorkflowStatus.Completed, null, null, T0);
        Should.Throw<DomainException>(() => effective.Cancel(T0)).Code.ShouldBe("CONTRACT_NOT_CANCELLABLE");
    }

    [Fact]
    public void Appendix_adds_quantity_reduces_price_and_extends_expiry()
    {
        var c = NewContract(Line(1_000, 450));
        c.UpdateTerms(null, new DateOnly(2026, 3, 1), new DateOnly(2027, 2, 28), null, null, null, null, null, null, null, null);
        c.ApplyWorkflow(WorkflowStatus.Completed, null, null, T0);
        var item = c.Items.Single();

        var more = new ContractAppendix(c, "01", AppendixType.AddQuantity, "Nhu cầu tăng");
        Should.Throw<DomainException>(() => more.SetContent(c, "x", null, null, [new AppendixLineDraft(item.Id, 900, null)]))
            .Code.ShouldBe("APPENDIX_QUANTITY_NOT_INCREASED");
        more.SetContent(c, "Nhu cầu tăng", null, null, [new AppendixLineDraft(item.Id, 1_100, null)]);
        more.ValueChange.ShouldBe(45_000m);
        more.Apply(c, T0);
        item.Quantity.ShouldBe(1_100);
        c.TotalAmount.ShouldBe(495_000m);
        more.Status.ShouldBe(AppendixStatus.Effective);

        var cheaper = new ContractAppendix(c, "02", AppendixType.PriceReduction, "Giảm giá");
        Should.Throw<DomainException>(() => cheaper.SetContent(c, "x", null, null, [new AppendixLineDraft(item.Id, null, 500)]))
            .Code.ShouldBe("APPENDIX_PRICE_NOT_REDUCED");
        cheaper.SetContent(c, "Giảm giá", null, null, [new AppendixLineDraft(item.Id, null, 400)]);
        cheaper.Apply(c, T0);
        c.TotalAmount.ShouldBe(440_000m);

        var longer = new ContractAppendix(c, "03", AppendixType.Extension, "Gia hạn");
        Should.Throw<DomainException>(() => longer.SetContent(c, "x", null, new DateOnly(2027, 1, 1), [])).Code.ShouldBe("APPENDIX_EXPIRY_INVALID");
        longer.SetContent(c, "Gia hạn", null, new DateOnly(2027, 6, 30), []);
        longer.Apply(c, T0);
        c.ExpiryDate.ShouldBe(new DateOnly(2027, 6, 30));
    }

    [Fact]
    public void Appendix_requires_an_effective_contract_and_cannot_go_below_received()
    {
        var draft = NewContract();
        Should.Throw<DomainException>(() => new ContractAppendix(draft, "01", AppendixType.AddQuantity, "x")).Code.ShouldBe("CONTRACT_NOT_EFFECTIVE");

        var c = NewContract(Line(1_000, 450));
        c.ApplyWorkflow(WorkflowStatus.Completed, null, null, T0);
        var item = c.Items.Single();
        item.Track(0, 0, 0, received: 800, 0, 0);
        item.RemainingQuantity.ShouldBe(200);

        var cut = new ContractAppendix(c, "01", AppendixType.InfoChange, "Đổi tài khoản");
        Should.Throw<DomainException>(() => cut.SetContent(c, "x", "STK mới", null, [new AppendixLineDraft(item.Id, 700, null)]))
            .Code.ShouldBe("APPENDIX_QUANTITY_BELOW_RECEIVED");
    }
}

public class BidDecisionTests
{
    private static BidDecision NewBid() => new("125/QĐ-BV", new DateOnly(2026, 2, 15), "Phê duyệt KQLCNT", "Gói 01: Thuốc generic", Guid.NewGuid());

    [Fact]
    public void Items_must_belong_to_a_winning_supplier()
    {
        var bid = NewBid();
        var winner = Guid.NewGuid();
        Should.Throw<DomainException>(() => bid.UpsertItem(Guid.NewGuid(), winner, 10, 100)).Code.ShouldBe("BID_SUPPLIER_NOT_WINNER");

        bid.UpsertSupplier(winner, 1_000_000);
        var item = Guid.NewGuid();
        bid.UpsertItem(item, winner, 10, 100);
        bid.UpsertItem(item, winner, 20, 90); // re-sync updates the line
        bid.Items.Single().Quantity.ShouldBe(20);
        bid.Items.Single().Amount.ShouldBe(1_800);
        Should.Throw<DomainException>(() => bid.UpsertItem(item, Guid.NewGuid(), 1, 1)).Code.ShouldBe("BID_SUPPLIER_NOT_WINNER");
        Should.Throw<DomainException>(() => bid.RemoveSupplier(winner)).Code.ShouldBe("BID_SUPPLIER_HAS_ITEMS");
    }

    [Fact]
    public void Activation_requires_suppliers_and_items_and_freezes_the_number()
    {
        var bid = NewBid();
        Should.Throw<DomainException>(() => bid.Activate()).Code.ShouldBe("BID_NO_SUPPLIER");
        var s = Guid.NewGuid();
        bid.UpsertSupplier(s, 0);
        Should.Throw<DomainException>(() => bid.Activate()).Code.ShouldBe("BID_NO_ITEMS");
        bid.UpsertItem(Guid.NewGuid(), s, 1, 1);
        bid.Activate();
        bid.Status.ShouldBe(BidDecisionStatus.Active);

        Should.Throw<DomainException>(() => bid.Update("999/QĐ-BV", bid.SignedDate, bid.Summary, bid.PackageName, null, null, null, bid.CategoryId, null, null))
            .Code.ShouldBe("BID_NUMBER_LOCKED");
        bid.Close();
        Should.Throw<DomainException>(() => bid.UpsertSupplier(Guid.NewGuid(), 1)).Code.ShouldBe("BID_CLOSED");
    }
}
