using BlueContract.Domain.Common;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Proposals;
using BlueContract.Domain.Workflows;
using Shouldly;

namespace BlueContract.Domain.Tests.Proposals;

public class ProposalTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 2, 0, 0, TimeSpan.Zero);

    private static ProposalLineDraft Line(decimal proposed, decimal remaining = 1_000, Guid? contractItemId = null) =>
        new(Guid.NewGuid(), "16/2026/HĐ", contractItemId ?? Guid.NewGuid(), Guid.NewGuid(), "VT001", "Bơm tiêm 5ml", "Cái", "K1",
            100, 500, 450, 150, remaining, proposed, null);

    private static Proposal NewProposal() =>
        new("DX-2026-0001", Guid.NewGuid(), null, Guid.NewGuid(), ProcurementDomain.MedicalSupply, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "Đặng Thị Thu");

    [Fact]
    public void Proposed_quantity_cannot_exceed_the_contract_remaining()
    {
        var p = NewProposal();
        Should.Throw<DomainException>(() => p.SetLines([Line(1_001)])).Code.ShouldBe("PROPOSAL_QTY_EXCEEDS_CONTRACT");
        Should.Throw<DomainException>(() => p.SetLines([Line(0)])).Code.ShouldBe("PROPOSAL_QUANTITY_INVALID");
        var item = Guid.NewGuid();
        Should.Throw<DomainException>(() => p.SetLines([Line(1, contractItemId: item), Line(2, contractItemId: item)])).Code.ShouldBe("PROPOSAL_LINE_DUPLICATE");
        p.SetLines([Line(1_000)]);
        p.Lines.Single().ProposedQuantity.ShouldBe(1_000);
        p.PeriodText.ShouldBe("Từ 01/10/2026 đến 31/10/2026");
    }

    [Fact]
    public void Signing_locks_the_proposal_and_completion_submits_it()
    {
        var p = NewProposal();
        p.SetLines([Line(10)]);
        p.ApplyWorkflow(WorkflowStatus.Running, T0);
        p.Status.ShouldBe(ProposalStatus.Signing);
        Should.Throw<DomainException>(() => p.SetLines([Line(5)])).Code.ShouldBe("PROPOSAL_LOCKED");

        p.ApplyWorkflow(WorkflowStatus.Completed, T0);
        p.Status.ShouldBe(ProposalStatus.Submitted);
        p.SubmittedAt.ShouldBe(T0);

        var summaryId = Guid.NewGuid();
        p.MarkAggregated(summaryId);
        p.Status.ShouldBe(ProposalStatus.Aggregated);
        Should.Throw<DomainException>(() => p.Cancel()).Code.ShouldBe("PROPOSAL_NOT_CANCELLABLE");
        p.ReleaseFromSummary();
        p.Status.ShouldBe(ProposalStatus.Submitted);
    }

    [Fact]
    public void Period_must_be_ordered()
    {
        Should.Throw<DomainException>(() => new Proposal("DX", Guid.NewGuid(), null, Guid.NewGuid(), ProcurementDomain.Drug,
            new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 1), null)).Code.ShouldBe("PROPOSAL_PERIOD_INVALID");
    }
}

public class ProposalSummaryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 2, 0, 0, TimeSpan.Zero);

    private static SummaryLineDraft Line(decimal proposed, decimal remaining) =>
        new(Guid.NewGuid(), "16/2026/HĐ", Guid.NewGuid(), Guid.NewGuid(), "TBYT Phương Đông", "VT001", "Bơm tiêm 5ml", "Cái", 850, proposed,
            100, 500, 450, 150, remaining);

    private static WorkflowInstance Workflow()
    {
        var t = new WorkflowTemplate("WF_PROPOSAL_SUMMARY", "Ký tổng hợp", BlueContract.Domain.Documents.DocumentType.ProposalSummary, null, true);
        t.SetSteps(
        [
            new("AGGREGATOR", "Người tổng hợp ký", WorkflowActionType.Sign, AssigneeType.Creator, null, null, false, false, [], null, null),
            new("DOMAIN_HEAD", "Tổ trưởng duyệt", WorkflowActionType.Approve, AssigneeType.Role, "DOMAIN_HEAD", null, true, false, [0], null, 24),
            new("DIRECTOR", "Giám đốc ký", WorkflowActionType.Sign, AssigneeType.Role, "DIRECTOR", null, false, false, [0, 2], null, 24),
        ]);
        return new WorkflowInstance(Guid.NewGuid(), t, Guid.NewGuid(), "Tổng hợp", null, T0);
    }

    [Fact]
    public void Approved_defaults_to_proposed_capped_by_the_contract_and_follows_the_workflow()
    {
        var s = new ProposalSummary("TH-2026-0001", ProcurementDomain.MedicalSupply, null, "Tháng 10/2026", Guid.NewGuid(), "Bùi Văn Long");
        s.SetContent([Guid.NewGuid(), Guid.NewGuid()], [Line(1_200, 1_000), Line(300, 5_000)], null);
        s.Lines.Select(l => l.ApprovedQuantity).ShouldBe([1_000m, 300m]);
        s.Sources.Count.ShouldBe(2);

        var wf = Workflow();
        s.ApplyWorkflow(wf.Status, wf.ActiveStep, T0);
        s.Status.ShouldBe(ProposalSummaryStatus.AggregatorSigning);
        Should.Throw<DomainException>(() => s.SetApprovedQuantity(s.Lines.First().Id, 10)).Code.ShouldBe("SUMMARY_NOT_IN_REVIEW");

        wf.Act(wf.ActiveStep!.Id, Guid.NewGuid(), "TH", null, null, Guid.NewGuid(), T0);
        s.ApplyWorkflow(wf.Status, wf.ActiveStep, T0);
        s.Status.ShouldBe(ProposalSummaryStatus.DomainHeadReview);
        Should.Throw<DomainException>(() => s.SetApprovedQuantity(s.Lines.Last().Id, 6_000)).Code.ShouldBe("SUMMARY_APPROVED_EXCEEDS_CONTRACT");
        s.SetApprovedQuantity(s.Lines.Last().Id, 250);

        wf.Act(wf.ActiveStep!.Id, Guid.NewGuid(), "TT", null, null, null, T0);
        s.ApplyWorkflow(wf.Status, wf.ActiveStep, T0);
        s.Status.ShouldBe(ProposalSummaryStatus.DirectorSigning);
        wf.Act(wf.ActiveStep!.Id, Guid.NewGuid(), "GĐ", null, null, Guid.NewGuid(), T0.AddHours(1));
        s.ApplyWorkflow(wf.Status, wf.ActiveStep, T0.AddHours(1));
        s.Status.ShouldBe(ProposalSummaryStatus.Approved);
        s.ApprovedAt.ShouldBe(T0.AddHours(1));
    }

    [Fact]
    public void Calls_never_exceed_the_approved_quantity_and_released_quantity_can_be_called_again()
    {
        var s = new ProposalSummary("TH", ProcurementDomain.MedicalSupply, null, "Kỳ", Guid.NewGuid(), null);
        s.SetContent([Guid.NewGuid()], [Line(100, 1_000)], null);
        var line = s.Lines.Single();
        Should.Throw<DomainException>(() => s.RegisterCall(line.Id, 10)).Code.ShouldBe("SUMMARY_NOT_APPROVED");
        s.ApplyWorkflow(WorkflowStatus.Completed, null, T0);

        s.RegisterCall(line.Id, 60);
        s.RegisterCall(line.Id, 40);
        Should.Throw<DomainException>(() => s.RegisterCall(line.Id, 1)).Code.ShouldBe("CALL_QTY_EXCEEDS_APPROVED");
        s.ReleaseCall(line.Id, 15);
        line.RemainingToCall.ShouldBe(15);
        s.RegisterCall(line.Id, 15);
        Should.Throw<DomainException>(() => s.Cancel()).Code.ShouldBe("SUMMARY_ALREADY_CALLED");
    }
}

public class CallOrderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 2, 0, 0, TimeSpan.Zero);

    private static CallOrder NewOrder(params decimal[] quantities)
    {
        var order = new CallOrder("GH-2026-0001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 21), "Mai Văn Tú", null);
        order.AddLines([.. quantities.Select(q => new CallOrderLineDraft(Guid.NewGuid(), Guid.NewGuid(), "16/2026", Guid.NewGuid(), "VT", "Bơm tiêm", "Cái", 1_000, q))]);
        return order;
    }

    [Fact]
    public void Full_confirmation_and_shortage_rules()
    {
        var order = NewOrder(100, 50);
        var lines = order.Lines.ToList();
        Should.Throw<DomainException>(() => order.Confirm([new(lines[0].Id, 100, null)], Guid.NewGuid(), "NCC", null, T0)).Code.ShouldBe("CALL_CONFIRMATION_INCOMPLETE");
        Should.Throw<DomainException>(() => order.Confirm([new(lines[0].Id, 101, null), new(lines[1].Id, 50, null)], Guid.NewGuid(), "NCC", null, T0))
            .Code.ShouldBe("CALL_CONFIRM_EXCEEDS_CALLED");
        Should.Throw<DomainException>(() => order.Confirm([new(lines[0].Id, 80, null), new(lines[1].Id, 50, null)], Guid.NewGuid(), "NCC", null, T0))
            .Code.ShouldBe("CALL_SHORTAGE_REASON_REQUIRED");

        var released = order.Confirm([new(lines[0].Id, 80, "Thiếu hàng tại kho NCC"), new(lines[1].Id, 50, null)], Guid.NewGuid(), "NCC", "Giao trước 80", T0);
        order.Status.ShouldBe(CallOrderStatus.PartiallyConfirmed);
        released.ShouldBe([(lines[0].SummaryLineId, 20m)]);
        order.TotalAmount.ShouldBe(130_000m);
        Should.Throw<DomainException>(() => order.Cancel()).Code.ShouldBe("CALL_NOT_CANCELLABLE");

        order.RecordDelivery(lines[0].ContractItemId, 80);
        order.Status.ShouldBe(CallOrderStatus.PartiallyConfirmed);
        order.RecordDelivery(lines[1].ContractItemId, 50);
        order.Status.ShouldBe(CallOrderStatus.Delivered);
    }

    [Fact]
    public void Rejection_releases_everything_and_overdue_is_detected()
    {
        var order = NewOrder(10);
        order.IsOverdue(new DateOnly(2026, 10, 22)).ShouldBeTrue();
        var line = order.Lines.Single();
        order.Confirm([new(line.Id, 0, "Ngừng sản xuất")], Guid.NewGuid(), "NCC", null, T0).Single().Released.ShouldBe(10);
        order.Status.ShouldBe(CallOrderStatus.Rejected);
        order.IsOverdue(new DateOnly(2026, 10, 22)).ShouldBeFalse();

        Should.Throw<DomainException>(() => new CallOrder("X", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 1), null, null))
            .Code.ShouldBe("CALL_DEADLINE_INVALID");
    }

    [Fact]
    public void Numbers_are_sequential_per_prefix_and_year()
    {
        var seq = new NumberSequence("DX", 2026);
        seq.Next().ShouldBe("DX-2026-0001");
        seq.Next().ShouldBe("DX-2026-0002");
    }
}
