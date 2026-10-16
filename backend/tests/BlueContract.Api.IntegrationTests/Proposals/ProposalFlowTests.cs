using System.Net;
using System.Net.Http.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Admin;
using BlueContract.Application.Common;
using BlueContract.Application.Contracts;
using BlueContract.Application.Proposals;
using BlueContract.Application.Signing;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Proposals;
using BlueContract.Domain.Workflows;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Proposals;

/// <summary>E2E #2 at API level (spec §13): đề xuất → tổng hợp → tổ trưởng sửa SL → GĐ ký → gọi hàng → NCC xác nhận.</summary>
[Collection(ApiCollection.Name)]
public class ProposalFlowTests(ApiFactory factory)
{
    [Fact]
    public async Task Proposal_to_supplier_confirmation_keeps_every_quantity_rule()
    {
        using var department = await factory.LoginAsync("khoanoi");
        var departments = await department.GetFromJsonAsync<List<LookupItem>>("/api/lookups/departments");
        var warehouses = await department.GetFromJsonAsync<List<LookupItem>>("/api/lookups/warehouses");
        var knoi = departments!.Single(d => d.Code == "KNOI").Id;
        var warehouse = warehouses!.Single(w => w.Code == "KHO_VTYT").Id;

        var candidates = await department.GetFromJsonAsync<ProposalCandidatesDto>(
            $"/api/proposals/candidates?domain=MedicalSupply&warehouseId={warehouse}&from=2026-10-01&to=2026-10-31&departmentId={knoi}", JsonDefaults.Options);
        candidates!.HisAvailable.ShouldBeTrue();
        candidates.Items.Count.ShouldBeGreaterThanOrEqualTo(2);
        candidates.Items.ShouldAllBe(i => i.ContractRemaining > 0 && i.CurrentStock == i.OpeningStock + i.Received - i.Issued);
        var a = candidates.Items[0];
        var b = candidates.Items[1];

        // Rule: SL đề xuất ≤ SL còn lại của HĐ (server side).
        using (var tooMuch = await department.PostAsJsonAsync("/api/proposals", Proposal(knoi, warehouse, (a.ContractItemId, a.ContractRemaining + 1)), JsonDefaults.Options))
        {
            (await tooMuch.Content.ReadAsStringAsync()).ShouldContain("PROPOSAL_QTY_EXCEEDS_CONTRACT");
        }

        // Another department is refused.
        using (var otherDepartment = await department.PostAsJsonAsync("/api/proposals",
                   Proposal(departments!.Single(d => d.Code == "KNHI").Id, warehouse, (a.ContractItemId, 1)), JsonDefaults.Options))
        {
            otherDepartment.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        var first = await CreateAndSignProposalAsync(department, Proposal(knoi, warehouse, (a.ContractItemId, 30), (b.ContractItemId, 20)));
        var second = await CreateAndSignProposalAsync(department, Proposal(knoi, warehouse, (a.ContractItemId, 10)));
        (await department.GetFromJsonAsync<ProposalDto>($"/api/proposals/{first}", JsonDefaults.Options))!.Status.ShouldBe(ProposalStatus.Submitted);

        // Người tổng hợp gộp 2 phiếu theo mặt hàng.
        using var aggregator = await factory.LoginAsync("tonghopvt");
        var available = await aggregator.GetFromJsonAsync<List<ProposalListItemDto>>("/api/proposal-summaries/candidates?domain=MedicalSupply", JsonDefaults.Options);
        available!.Select(p => p.Id).ShouldContain(first);
        using var created = await aggregator.PostAsJsonAsync("/api/proposal-summaries",
            new SaveSummaryRequest(ProcurementDomain.MedicalSupply, null, "Tháng 10/2026", null, [first, second]), JsonDefaults.Options);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var summaryId = await created.Content.ReadFromJsonAsync<Guid>();
        var summary = await GetSummaryAsync(aggregator, summaryId);
        summary.Lines.Count.ShouldBe(2);
        var lineA = summary.Lines.Single(l => l.ContractItemId == a.ContractItemId);
        lineA.ProposedQuantity.ShouldBe(40);
        lineA.ApprovedQuantity.ShouldBe(40);
        (await department.GetFromJsonAsync<ProposalDto>($"/api/proposals/{first}", JsonDefaults.Options))!.Status.ShouldBe(ProposalStatus.Aggregated);

        (await aggregator.PostAsync(new Uri($"/api/proposal-summaries/{summaryId}/submit", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await SignAsync(aggregator, summary.DocumentId!.Value);
        (await GetSummaryAsync(aggregator, summaryId)).Status.ShouldBe(ProposalSummaryStatus.DomainHeadReview);

        // Tổ trưởng sửa SL duyệt → bản mới → người tổng hợp ký lại → tổ trưởng duyệt → GĐ ký.
        using var head = await factory.LoginAsync("totruongvattu");
        var atHead = await GetSummaryAsync(head, summaryId);
        atHead.CanEditApproved.ShouldBeTrue();
        using (var over = await head.PutAsJsonAsync($"/api/proposal-summaries/{summaryId}/approved-quantities",
                   new[] { new ApprovedQuantityRequest(lineA.Id, lineA.ContractRemaining + 1) }, JsonDefaults.Options))
        {
            (await over.Content.ReadAsStringAsync()).ShouldContain("SUMMARY_APPROVED_EXCEEDS_CONTRACT");
        }

        (await head.PutAsJsonAsync($"/api/proposal-summaries/{summaryId}/approved-quantities",
            new[] { new ApprovedQuantityRequest(lineA.Id, 35) }, JsonDefaults.Options)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetSummaryAsync(aggregator, summaryId)).Status.ShouldBe(ProposalSummaryStatus.AggregatorSigning);
        await SignAsync(aggregator, summary.DocumentId!.Value);
        await ApproveAsync(head, summary.DocumentId!.Value);
        using var director = await factory.LoginAsync("giamdoc");
        await SignAsync(director, summary.DocumentId!.Value);
        var approved = await GetSummaryAsync(aggregator, summaryId);
        approved.Status.ShouldBe(ProposalSummaryStatus.Approved);
        approved.Lines.Single(l => l.Id == lineA.Id).ApprovedQuantity.ShouldBe(35);
        approved.CanCall.ShouldBeTrue();

        // Contract execution columns (post-commit recalculation).
        using var buyer = await factory.LoginAsync("muasam");
        var contract = await buyer.GetFromJsonAsync<ContractDto>($"/api/contracts/{a.ContractId}", JsonDefaults.Options);
        var tracked = contract!.Items.Single(i => i.Id == a.ContractItemId);
        tracked.ProposedQuantity.ShouldBeGreaterThanOrEqualTo(40);
        tracked.ApprovedQuantity.ShouldBeGreaterThanOrEqualTo(35);

        // Gọi hàng: Σ gọi ≤ SL duyệt; tách theo NCC.
        var deadline = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        using (var tooMany = await aggregator.PostAsJsonAsync("/api/call-orders",
                   new CreateCallsRequest(summaryId, warehouse, deadline, null, null, [new CallLineRequest(lineA.Id, 36)]), JsonDefaults.Options))
        {
            (await tooMany.Content.ReadAsStringAsync()).ShouldContain("CALL_QTY_EXCEEDS_APPROVED");
        }

        var lineB = approved.Lines.Single(l => l.Id != lineA.Id);
        using var call = await aggregator.PostAsJsonAsync("/api/call-orders",
            new CreateCallsRequest(summaryId, warehouse, deadline, "Mai Văn Tú", null, [new CallLineRequest(lineA.Id, 25), new CallLineRequest(lineB.Id, 20)]), JsonDefaults.Options);
        call.StatusCode.ShouldBe(HttpStatusCode.OK, await call.Content.ReadAsStringAsync());
        var orderId = (await call.Content.ReadFromJsonAsync<List<Guid>>())!.ShouldHaveSingleItem();
        (await GetSummaryAsync(aggregator, summaryId)).Lines.Single(l => l.Id == lineA.Id).RemainingToCall.ShouldBe(10);

        // Only the supplier of the order sees it; it confirms less for one line (with a reason).
        using var otherSupplier = await factory.LoginAsync("ncc1");
        (await otherSupplier.GetAsync(new Uri($"/api/call-orders/{orderId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var supplier = await factory.LoginAsync("ncc2");
        var order = await supplier.GetFromJsonAsync<CallOrderDto>($"/api/call-orders/{orderId}", JsonDefaults.Options);
        order!.CanConfirm.ShouldBeTrue();
        order.DocumentId.ShouldNotBeNull();
        var orderLineA = order.Lines.Single(l => l.Quantity == 25);
        var orderLineB = order.Lines.Single(l => l.Quantity == 20);
        using (var noReason = await supplier.PostAsJsonAsync($"/api/call-orders/{orderId}/confirm",
                   new ConfirmCallRequest([new(orderLineA.Id, 25, null), new(orderLineB.Id, 15, null)], null), JsonDefaults.Options))
        {
            (await noReason.Content.ReadAsStringAsync()).ShouldContain("CALL_SHORTAGE_REASON_REQUIRED");
        }

        (await supplier.PostAsJsonAsync($"/api/call-orders/{orderId}/confirm",
            new ConfirmCallRequest([new(orderLineA.Id, 25, null), new(orderLineB.Id, 15, "Lô hàng về muộn")], "Giao trong 3 ngày"), JsonDefaults.Options))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var confirmed = await aggregator.GetFromJsonAsync<CallOrderDto>($"/api/call-orders/{orderId}", JsonDefaults.Options);
        confirmed!.Status.ShouldBe(CallOrderStatus.PartiallyConfirmed);
        confirmed.Lines.Single(l => l.Id == orderLineB.Id).ShortageReason.ShouldBe("Lô hàng về muộn");
        var afterConfirm = await GetSummaryAsync(aggregator, summaryId);
        afterConfirm.Lines.Single(l => l.Id == lineB.Id).RemainingToCall.ShouldBe(5); // 5 not delivered can be called again

        var notifications = await aggregator.GetFromJsonAsync<Application.Notifications.NotificationListDto>("/api/notifications", JsonDefaults.Options);
        notifications!.Items.ShouldContain(n => n.Title.Contains(confirmed.Number, StringComparison.Ordinal));

        var supplierOrders = await supplier.GetFromJsonAsync<PagedResult<CallOrderListItemDto>>("/api/call-orders?pageSize=100", JsonDefaults.Options);
        supplierOrders!.Items.ShouldAllBe(o => o.SupplierId == confirmed.SupplierId);
    }

    [Fact]
    public async Task Demo_data_has_a_summary_waiting_for_approval_and_a_call_waiting_for_the_supplier()
    {
        using var head = await factory.LoginAsync("totruongvattu");
        var waiting = await head.GetFromJsonAsync<PagedResult<SummaryListItemDto>>("/api/proposal-summaries?status=DomainHeadReview", JsonDefaults.Options);
        waiting!.Items.ShouldNotBeEmpty();

        using var supplier = await factory.LoginAsync("ncc2");
        var calls = await supplier.GetFromJsonAsync<PagedResult<CallOrderListItemDto>>("/api/call-orders?status=Sent", JsonDefaults.Options);
        calls!.Items.ShouldNotBeEmpty();
        calls.Items.ShouldAllBe(c => c.SupplierName == "Công ty TNHH Thiết bị Y tế Phương Đông");
    }

    private static SaveProposalRequest Proposal(Guid department, Guid warehouse, params (Guid Item, decimal Qty)[] lines) =>
        new(department, warehouse, ProcurementDomain.MedicalSupply, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), null,
            [.. lines.Select(l => new ProposalLineRequest(l.Item, l.Qty, null))]);

    private static async Task<Guid> CreateAndSignProposalAsync(HttpClient client, SaveProposalRequest request)
    {
        using var created = await client.PostAsJsonAsync("/api/proposals", request, JsonDefaults.Options);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var id = await created.Content.ReadFromJsonAsync<Guid>();
        (await client.PostAsync(new Uri($"/api/proposals/{id}/submit", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var proposal = await client.GetFromJsonAsync<ProposalDto>($"/api/proposals/{id}", JsonDefaults.Options);
        proposal!.Status.ShouldBe(ProposalStatus.Signing);
        await SignAsync(client, proposal.DocumentId!.Value);
        return id;
    }

    private static async Task<SummaryDto> GetSummaryAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<SummaryDto>($"/api/proposal-summaries/{id}", JsonDefaults.Options))!;

    private static async Task SignAsync(HttpClient client, Guid documentId)
    {
        var wf = await client.GetFromJsonAsync<WorkflowDto>($"/api/workflows/by-document/{documentId}", JsonDefaults.Options);
        wf!.MyStepId.ShouldNotBeNull("the user should have the signing step");
        using var response = await client.PostAsJsonAsync($"/api/signing/steps/{wf.MyStepId}/sign", new BeginSignRequest("MOCK", null, null), JsonDefaults.Options);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task ApproveAsync(HttpClient client, Guid documentId)
    {
        var wf = await client.GetFromJsonAsync<WorkflowDto>($"/api/workflows/by-document/{documentId}", JsonDefaults.Options);
        wf!.MyStepId.ShouldNotBeNull("the user should have the active step");
        using var response = await client.PostAsJsonAsync($"/api/workflows/steps/{wf.MyStepId}/approve", new ApproveStepRequest("Đồng ý", false), JsonDefaults.Options);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}
