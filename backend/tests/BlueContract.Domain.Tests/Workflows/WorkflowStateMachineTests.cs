using BlueContract.Domain.Common;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Workflows;
using Shouldly;

namespace BlueContract.Domain.Tests.Workflows;

public class WorkflowStateMachineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 2, 0, 0, TimeSpan.Zero);
    private static readonly Guid Creator = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    /// <summary>Review → (optional) accountant review → supplier sign → director sign → seal.</summary>
    private static WorkflowTemplate ContractTemplate()
    {
        var t = new WorkflowTemplate("hd", "Ký hợp đồng", DocumentType.Contract, null, isDefault: true);
        t.SetSteps(
        [
            new("REVIEW", "Tổ rà soát", WorkflowActionType.Review, AssigneeType.Role, "REVIEWER", null, true, false, [0], null, 24),
            new("ACCOUNTANT_REVIEW", "Kế toán xem thay đổi", WorkflowActionType.Review, AssigneeType.Role, "ACCOUNTANT", null, false, true, [0, 1], null, null),
            new("SUPPLIER", "NCC ký", WorkflowActionType.SupplierSign, AssigneeType.DocumentSupplier, null, null, false, false, [1], null, null),
            new("DIRECTOR", "Giám đốc ký", WorkflowActionType.Sign, AssigneeType.Role, "DIRECTOR", null, false, false, [0, 1, 3], null, null),
            new("CLERK_SEAL", "Văn thư đóng dấu", WorkflowActionType.OrgSeal, AssigneeType.Role, "CLERK", null, false, false, [4], null, null),
        ]);
        return t;
    }

    private static WorkflowInstance Start(WorkflowTemplate? t = null) =>
        new(Guid.NewGuid(), t ?? ContractTemplate(), Creator, "Người lập", Guid.NewGuid(), T0);

    private static WorkflowStepInstance Step(WorkflowInstance wf, string code) => wf.Steps.Single(s => s.Code == code);

    [Fact]
    public void Start_activates_first_step_and_copies_steps()
    {
        var wf = Start();

        wf.Status.ShouldBe(WorkflowStatus.Running);
        wf.ActiveStep!.Code.ShouldBe("REVIEW");
        wf.ActiveStep.DueAt.ShouldBe(T0.AddHours(24));
        wf.Steps.Select(s => s.Order).ShouldBe([1, 2, 3, 4, 5]);
        wf.Steps.Skip(1).ShouldAllBe(s => s.Status == StepStatus.Pending);
        Step(wf, "DIRECTOR").SignatureAnchor.ShouldBe("SIG_DIRECTOR");
    }

    [Fact]
    public void Steps_advance_in_order_until_completed()
    {
        var wf = Start();

        wf.Act(Step(wf, "REVIEW").Id, Alice, "A", null, "OK", null, T0).ShouldBe(ActOutcome.Advanced);
        wf.ActiveStep!.Code.ShouldBe("ACCOUNTANT_REVIEW");
        wf.Act(Step(wf, "ACCOUNTANT_REVIEW").Id, Bob, "B", null, null, null, T0).ShouldBe(ActOutcome.Advanced);
        wf.Act(Step(wf, "SUPPLIER").Id, Alice, "NCC", null, null, Guid.NewGuid(), T0).ShouldBe(ActOutcome.Advanced);
        wf.Act(Step(wf, "DIRECTOR").Id, Bob, "GĐ", null, null, Guid.NewGuid(), T0).ShouldBe(ActOutcome.Advanced);
        wf.Act(Step(wf, "CLERK_SEAL").Id, Alice, "VT", null, null, Guid.NewGuid(), T0.AddHours(1)).ShouldBe(ActOutcome.Completed);

        wf.Status.ShouldBe(WorkflowStatus.Completed);
        wf.CompletedAt.ShouldBe(T0.AddHours(1));
        wf.ActiveStep.ShouldBeNull();
        wf.Events.Last().Type.ShouldBe(WorkflowEventType.Completed);
    }

    [Fact]
    public void Acting_twice_on_the_same_step_is_idempotent_for_the_same_actor()
    {
        var wf = Start();
        var review = Step(wf, "REVIEW");

        wf.Act(review.Id, Alice, "A", null, null, null, T0);
        wf.Act(review.Id, Alice, "A", null, null, null, T0).ShouldBe(ActOutcome.AlreadyDone);

        wf.ActiveStep!.Code.ShouldBe("ACCOUNTANT_REVIEW");
        wf.Events.Count(e => e.Type == WorkflowEventType.Approved).ShouldBe(1);
    }

    [Fact]
    public void Another_actor_on_a_finished_step_is_rejected()
    {
        var wf = Start();
        var review = Step(wf, "REVIEW");
        wf.Act(review.Id, Alice, "A", null, null, null, T0);

        Should.Throw<DomainException>(() => wf.Act(review.Id, Bob, "B", null, null, null, T0)).Code.ShouldBe("WORKFLOW_STEP_NOT_ACTIVE");
    }

    [Fact]
    public void Pending_step_cannot_be_acted_on()
    {
        var wf = Start();

        Should.Throw<DomainException>(() => wf.Act(Step(wf, "DIRECTOR").Id, Bob, "B", null, null, Guid.NewGuid(), T0)).Code.ShouldBe("WORKFLOW_STEP_NOT_ACTIVE");
    }

    [Fact]
    public void Signing_steps_require_a_signature()
    {
        var wf = Start();
        wf.Act(Step(wf, "REVIEW").Id, Alice, "A", null, null, null, T0, skipNextOptional: true);

        Should.Throw<DomainException>(() => wf.Act(Step(wf, "SUPPLIER").Id, Alice, "NCC", null, null, null, T0)).Code.ShouldBe("WORKFLOW_SIGNATURE_REQUIRED");
    }

    [Fact]
    public void Optional_next_step_can_be_skipped()
    {
        var wf = Start();

        wf.Act(Step(wf, "REVIEW").Id, Alice, "A", null, null, null, T0, skipNextOptional: true);

        Step(wf, "ACCOUNTANT_REVIEW").Status.ShouldBe(StepStatus.Skipped);
        wf.ActiveStep!.Code.ShouldBe("SUPPLIER");
    }

    [Fact]
    public void Non_optional_next_step_is_never_skipped()
    {
        var wf = Start();
        wf.Act(Step(wf, "REVIEW").Id, Alice, "A", null, null, null, T0, skipNextOptional: true);

        wf.Act(Step(wf, "SUPPLIER").Id, Alice, "NCC", null, null, Guid.NewGuid(), T0, skipNextOptional: true);

        wf.ActiveStep!.Code.ShouldBe("DIRECTOR");
    }

    [Fact]
    public void Return_goes_back_to_the_chosen_step_and_resets_later_steps()
    {
        var wf = Start();
        wf.Act(Step(wf, "REVIEW").Id, Alice, "A", null, null, null, T0, skipNextOptional: true);
        wf.Act(Step(wf, "SUPPLIER").Id, Alice, "NCC", null, null, Guid.NewGuid(), T0);

        wf.Return(Step(wf, "DIRECTOR").Id, 3, Bob, "GĐ", "Bổ sung chữ ký NCC", T0);

        wf.Status.ShouldBe(WorkflowStatus.Running);
        wf.ActiveStep!.Code.ShouldBe("SUPPLIER");
        Step(wf, "SUPPLIER").SignatureId.ShouldBeNull();
        Step(wf, "DIRECTOR").Status.ShouldBe(StepStatus.Pending);
        Step(wf, "REVIEW").Status.ShouldBe(StepStatus.Done);
        wf.Events.Single(e => e.Type == WorkflowEventType.Returned).Note.ShouldBe("Bổ sung chữ ký NCC");
    }

    [Fact]
    public void Return_to_creator_marks_workflow_returned_and_resubmit_restarts()
    {
        var wf = Start();

        wf.Return(Step(wf, "REVIEW").Id, 0, Alice, "A", "Sai đơn giá", T0);

        wf.Status.ShouldBe(WorkflowStatus.Returned);
        wf.ActiveStep.ShouldBeNull();
        Should.Throw<DomainException>(() => wf.Act(Step(wf, "REVIEW").Id, Alice, "A", null, null, null, T0)).Code.ShouldBe("WORKFLOW_NOT_RUNNING");

        wf.Resubmit(Creator, "Người lập", T0);
        wf.Status.ShouldBe(WorkflowStatus.Running);
        wf.ActiveStep!.Code.ShouldBe("REVIEW");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void Return_to_a_step_not_configured_is_rejected(int target)
    {
        var wf = Start();
        wf.Act(Step(wf, "REVIEW").Id, Alice, "A", null, null, null, T0, skipNextOptional: true);
        wf.Act(Step(wf, "SUPPLIER").Id, Alice, "NCC", null, null, Guid.NewGuid(), T0);

        Should.Throw<DomainException>(() => wf.Return(Step(wf, "DIRECTOR").Id, target, Bob, "GĐ", "x", T0)).Code.ShouldBe("WORKFLOW_RETURN_TARGET_NOT_ALLOWED");
    }

    [Fact]
    public void Return_requires_a_reason()
    {
        var wf = Start();

        Should.Throw<DomainException>(() => wf.Return(Step(wf, "REVIEW").Id, 0, Alice, "A", " ", T0)).Code.ShouldBe("WORKFLOW_RETURN_REASON_REQUIRED");
    }

    [Fact]
    public void Completed_workflow_rejects_any_action()
    {
        var t = new WorkflowTemplate("one", "Một bước", DocumentType.Proposal, null, true);
        t.SetSteps([new("APPROVE", "Duyệt", WorkflowActionType.Approve, AssigneeType.Creator, null, null, false, false, [0], null, null)]);
        var wf = Start(t);
        wf.Act(wf.ActiveStep!.Id, Creator, "C", null, null, null, T0).ShouldBe(ActOutcome.Completed);

        Should.Throw<DomainException>(() => wf.Comment(Alice, "A", "x", T0)).ShouldNotBeNull();
        Should.Throw<DomainException>(() => wf.Cancel(Alice, "A", "x", T0)).Code.ShouldBe("WORKFLOW_CLOSED");
        Should.Throw<DomainException>(() => wf.RestartForNewVersion(Alice, "A", T0)).Code.ShouldBe("WORKFLOW_CLOSED");
    }

    [Fact]
    public void New_document_version_restarts_signing_from_first_step()
    {
        var wf = Start();
        wf.Act(Step(wf, "REVIEW").Id, Alice, "A", null, null, null, T0, skipNextOptional: true);
        wf.Act(Step(wf, "SUPPLIER").Id, Alice, "NCC", null, null, Guid.NewGuid(), T0);

        wf.RestartForNewVersion(Creator, "C", T0);

        wf.ActiveStep!.Code.ShouldBe("REVIEW");
        wf.Steps.Skip(1).ShouldAllBe(s => s.Status == StepStatus.Pending && s.SignatureId == null);
    }

    [Fact]
    public void Delegation_is_recorded_on_the_step()
    {
        var wf = Start();
        var delegator = Guid.NewGuid();

        wf.Act(Step(wf, "REVIEW").Id, Alice, "Phó GĐ", delegator, null, null, T0);

        Step(wf, "REVIEW").OnBehalfOfUserId.ShouldBe(delegator);
    }

    [Fact]
    public void Template_validation_rules()
    {
        var t = new WorkflowTemplate("x", "X", DocumentType.Contract, null, false);

        Should.Throw<DomainException>(() => t.SetSteps([])).Code.ShouldBe("WORKFLOW_STEPS_REQUIRED");
        Should.Throw<DomainException>(() => t.SetSteps(
        [
            new("A", "A", WorkflowActionType.Review, AssigneeType.Creator, null, null, false, false, [], null, null),
            new("A", "A2", WorkflowActionType.Review, AssigneeType.Creator, null, null, false, false, [], null, null),
        ])).Code.ShouldBe("WORKFLOW_STEP_CODE_DUPLICATE");
        Should.Throw<DomainException>(() => t.SetSteps(
        [
            new("A", "A", WorkflowActionType.Review, AssigneeType.Creator, null, null, false, false, [1], null, null),
        ])).Code.ShouldBe("WORKFLOW_RETURN_TARGET_INVALID");
        Should.Throw<DomainException>(() => new WorkflowStepTemplate("S", "S", WorkflowActionType.SupplierSign, AssigneeType.Role, "DIRECTOR", null, false, false, [], null, null))
            .Code.ShouldBe("WORKFLOW_SUPPLIER_STEP_ASSIGNEE");
        Should.Throw<DomainException>(() => new WorkflowStepTemplate("R", "R", WorkflowActionType.Review, AssigneeType.Role, null, null, false, false, [], null, null))
            .Code.ShouldBe("WORKFLOW_STEP_ROLE_REQUIRED");
    }

    [Fact]
    public void Inactive_template_cannot_start()
    {
        var t = ContractTemplate();
        t.Update(t.Name, null, true, isActive: false);

        Should.Throw<DomainException>(() => Start(t)).Code.ShouldBe("WORKFLOW_TEMPLATE_INACTIVE");
    }

    [Fact]
    public void Comments_are_recorded_without_acting()
    {
        var wf = Start();

        wf.Comment(Bob, "B", "Đề nghị kiểm tra lại điều 5", T0);

        wf.ActiveStep!.Code.ShouldBe("REVIEW");
        wf.Events.Last().Type.ShouldBe(WorkflowEventType.Commented);
        wf.Events.Last().StepName.ShouldBe("Tổ rà soát");
    }
}
