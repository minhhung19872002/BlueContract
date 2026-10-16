using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
using BlueContract.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>AUDIT-1 §10.4: Σ SL gọi ≤ SL duyệt must hold under truly parallel requests, the losers get a Vietnamese 4xx.</summary>
    [Fact]
    public async Task Parallel_calls_never_exceed_the_approved_quantity()
    {
        var (summaryId, lineId, warehouse) = await ApprovedSummaryAsync(10);
        using var aggregator = await factory.LoginAsync("tonghopvt");
        var deadline = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => aggregator.PostAsJsonAsync("/api/call-orders",
            new CreateCallsRequest(summaryId, warehouse, deadline, null, null, [new CallLineRequest(lineId, 4)]), JsonDefaults.Options)));
        try
        {
            var ok = responses.Count(r => r.IsSuccessStatusCode);
            var outcomes = string.Join(" | ", await Task.WhenAll(responses.Select(async r => $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}")));
            ok.ShouldBe(2, "10 approved fit exactly two calls of 4: " + outcomes);
            foreach (var failed in responses.Where(r => !r.IsSuccessStatusCode))
            {
                ((int)failed.StatusCode).ShouldBeInRange(400, 499);
                var body = await failed.Content.ReadAsStringAsync();
                body.ShouldContain("CALL_QTY_EXCEEDS_APPROVED");
            }

            var summary = await GetSummaryAsync(aggregator, summaryId);
            summary.Lines.Single(l => l.Id == lineId).CalledQuantity.ShouldBe(8);
            var orders = await aggregator.GetFromJsonAsync<PagedResult<CallOrderListItemDto>>($"/api/call-orders?summaryId={summaryId}&pageSize=100", JsonDefaults.Options);
            orders!.Total.ShouldBe(2);
        }
        finally
        {
            foreach (var r in responses)
            {
                r.Dispose();
            }
        }
    }

    /// <summary>AUDIT-1 §10.4: calls from every summary together stay within the contract quantity.</summary>
    [Fact]
    public async Task Calls_never_exceed_the_contract_quantity_across_summaries()
    {
        var (summaryId, lineId, warehouse) = await ApprovedSummaryAsync(10);
        using var aggregator = await factory.LoginAsync("tonghopvt");
        var itemId = (await GetSummaryAsync(aggregator, summaryId)).Lines.Single().ContractItemId;

        // Other summaries already called most of the contract: only 3 are left although this summary approved 10.
        decimal original;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            original = await db.Database.SqlQuery<decimal>($"SELECT quantity AS \"Value\" FROM contract_items WHERE id = {itemId}").SingleAsync();
            var called = await db.CallOrders.Where(o => o.Status != CallOrderStatus.Cancelled && o.Status != CallOrderStatus.Rejected)
                .SelectMany(o => o.Lines).Where(l => l.ContractItemId == itemId).SumAsync(l => l.ConfirmedQuantity ?? l.Quantity);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE contract_items SET quantity = {called + 3} WHERE id = {itemId}");
        }

        try
        {
            var deadline = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
            using var over = await aggregator.PostAsJsonAsync("/api/call-orders",
                new CreateCallsRequest(summaryId, warehouse, deadline, null, null, [new CallLineRequest(lineId, 4)]), JsonDefaults.Options);
            over.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await over.Content.ReadAsStringAsync()).ShouldContain("CALL_QTY_EXCEEDS_CONTRACT");
            using var fits = await aggregator.PostAsJsonAsync("/api/call-orders",
                new CreateCallsRequest(summaryId, warehouse, deadline, null, null, [new CallLineRequest(lineId, 3)]), JsonDefaults.Options);
            fits.StatusCode.ShouldBe(HttpStatusCode.OK, await fits.Content.ReadAsStringAsync());
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
                .ExecuteSqlInterpolatedAsync($"UPDATE contract_items SET quantity = {original} WHERE id = {itemId}");
        }
    }

    /// <summary>
    /// AUDIT-1 §12.4: a call order shows its timeline (lập, gửi NCC, xác nhận, nhập kho, hủy kèm lý do) to the hospital and
    /// to its supplier, not to another supplier.
    /// </summary>
    [Fact]
    public async Task A_call_order_has_a_timeline_and_is_cancelled_with_a_reason()
    {
        var (summaryId, lineId, warehouse) = await ApprovedSummaryAsync(2);
        using var aggregator = await factory.LoginAsync("tonghopvt");
        using var created = await aggregator.PostAsJsonAsync("/api/call-orders",
            new CreateCallsRequest(summaryId, warehouse, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)), null, null, [new CallLineRequest(lineId, 1)]), JsonDefaults.Options);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var orderId = (await created.Content.ReadFromJsonAsync<List<Guid>>())!.Single();

        var timeline = await aggregator.GetFromJsonAsync<List<JsonElement>>($"/api/call-orders/{orderId}/timeline");
        timeline!.Select(e => e.GetProperty("kind").GetString()).ShouldBe(["created", "sent"]);
        timeline[0].GetProperty("actor").GetString().ShouldNotBeNullOrWhiteSpace();

        using (var cancelled = await aggregator.PostAsJsonAsync($"/api/call-orders/{orderId}/cancel", new { reason = "Khoa đổi nhu cầu" }))
        {
            cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancelled.Content.ReadAsStringAsync());
        }

        timeline = await aggregator.GetFromJsonAsync<List<JsonElement>>($"/api/call-orders/{orderId}/timeline");
        var cancel = timeline!.Last();
        cancel.GetProperty("kind").GetString().ShouldBe("cancelled");
        cancel.GetProperty("note").GetString().ShouldBe("Khoa đổi nhu cầu");
        cancel.GetProperty("actor").GetString().ShouldNotBeNullOrWhiteSpace();
        timeline.Select(e => e.GetProperty("at").GetDateTimeOffset()).ShouldBeInOrder(SortDirection.Ascending);

        string ownSupplierUser, otherSupplierUser;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var supplierId = await db.CallOrders.Where(o => o.Id == orderId).Select(o => o.SupplierId).SingleAsync();
            string[] demo = ["ncc1", "ncc2", "ncc3"];
            ownSupplierUser = await db.Users.Where(u => u.SupplierId == supplierId && demo.Contains(u.UserName)).Select(u => u.UserName).FirstAsync();
            otherSupplierUser = await db.Users.Where(u => u.SupplierId != null && u.SupplierId != supplierId && demo.Contains(u.UserName)).Select(u => u.UserName).FirstAsync();
        }

        using var own = await factory.LoginAsync(ownSupplierUser);
        (await own.GetFromJsonAsync<List<JsonElement>>($"/api/call-orders/{orderId}/timeline"))!.ShouldNotBeEmpty();
        using var other = await factory.LoginAsync(otherSupplierUser);
        (await other.GetAsync(new Uri($"/api/call-orders/{orderId}/timeline", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>AUDIT-1 §10.5: no call for a contract that is liquidated, expired or not yet in force.</summary>
    [Theory]
    [InlineData("liquidated")]
    [InlineData("expired")]
    [InlineData("not-yet-effective")]
    public async Task Calls_need_a_contract_in_force(string change)
    {
        var (summaryId, lineId, warehouse) = await ApprovedSummaryAsync(2);
        using var aggregator = await factory.LoginAsync("tonghopvt");
        var contractId = (await GetSummaryAsync(aggregator, summaryId)).Lines.Single().ContractId;

        (string Status, DateOnly? Effective, DateOnly? Expiry) original;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = await db.Contracts.AsNoTracking().SingleAsync(x => x.Id == contractId);
            original = (c.Status.ToString(), c.EffectiveDate, c.ExpiryDate);
            FormattableString sql = change switch
            {
                "liquidated" => $"UPDATE contracts SET status = 'Liquidated' WHERE id = {contractId}",
                "expired" => $"UPDATE contracts SET expiry_date = CURRENT_DATE - 1 WHERE id = {contractId}",
                _ => $"UPDATE contracts SET effective_date = CURRENT_DATE + 30 WHERE id = {contractId}",
            };
            await db.Database.ExecuteSqlInterpolatedAsync(sql);
        }

        try
        {
            using var call = await aggregator.PostAsJsonAsync("/api/call-orders", new CreateCallsRequest(summaryId, warehouse,
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)), null, null, [new CallLineRequest(lineId, 1)]), JsonDefaults.Options);
            call.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await call.Content.ReadAsStringAsync());
            (await call.Content.ReadAsStringAsync()).ShouldContain("CALL_CONTRACT_NOT_IN_FORCE");
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE contracts SET status = {original.Status}, effective_date = {original.Effective}, expiry_date = {original.Expiry} WHERE id = {contractId}");
        }
    }

    /// <summary>AUDIT-1 §10.6: document numbers are allocated atomically — parallel creations all succeed with distinct numbers.</summary>
    [Fact]
    public async Task Parallel_proposals_get_distinct_numbers()
    {
        using var department = await factory.LoginAsync("khoanoi");
        var departments = await department.GetFromJsonAsync<List<LookupItem>>("/api/lookups/departments");
        var warehouses = await department.GetFromJsonAsync<List<LookupItem>>("/api/lookups/warehouses");
        var knoi = departments!.Single(d => d.Code == "KNOI").Id;
        var warehouse = warehouses!.Single(w => w.Code == "KHO_VTYT").Id;
        var item = (await department.GetFromJsonAsync<ProposalCandidatesDto>(
            $"/api/proposals/candidates?domain=MedicalSupply&warehouseId={warehouse}&from=2026-10-01&to=2026-10-31&departmentId={knoi}", JsonDefaults.Options))!.Items[0];

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            department.PostAsJsonAsync("/api/proposals", Proposal(knoi, warehouse, (item.ContractItemId, 1)), JsonDefaults.Options)));
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK, string.Join(" | ", bodies));
        var numbers = new List<string>();
        foreach (var r in responses)
        {
            var id = await r.Content.ReadFromJsonAsync<Guid>();
            numbers.Add((await department.GetFromJsonAsync<ProposalDto>($"/api/proposals/{id}", JsonDefaults.Options))!.Number);
            r.Dispose();
        }

        numbers.Distinct().Count().ShouldBe(4);
    }

    [Fact]
    public async Task Proposals_need_a_contract_in_force()
    {
        using var department = await factory.LoginAsync("khoanoi");
        var departments = await department.GetFromJsonAsync<List<LookupItem>>("/api/lookups/departments");
        var warehouses = await department.GetFromJsonAsync<List<LookupItem>>("/api/lookups/warehouses");
        var knoi = departments!.Single(d => d.Code == "KNOI").Id;
        var warehouse = warehouses!.Single(w => w.Code == "KHO_VTYT").Id;
        const string query = "/api/proposals/candidates?domain=MedicalSupply&from=2026-10-01&to=2026-10-31";
        var item = (await department.GetFromJsonAsync<ProposalCandidatesDto>($"{query}&warehouseId={warehouse}&departmentId={knoi}", JsonDefaults.Options))!.Items[0];

        DateOnly? expiry;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            expiry = (await db.Contracts.AsNoTracking().SingleAsync(c => c.Id == item.ContractId)).ExpiryDate;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE contracts SET expiry_date = CURRENT_DATE - 1 WHERE id = {item.ContractId}");
        }

        try
        {
            var candidates = await department.GetFromJsonAsync<ProposalCandidatesDto>($"{query}&warehouseId={warehouse}&departmentId={knoi}", JsonDefaults.Options);
            candidates!.Items.ShouldNotContain(i => i.ContractId == item.ContractId);
            using var refused = await department.PostAsJsonAsync("/api/proposals", Proposal(knoi, warehouse, (item.ContractItemId, 1)), JsonDefaults.Options);
            (await refused.Content.ReadAsStringAsync()).ShouldContain("PROPOSAL_ITEM_NOT_CALLABLE");
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
                .ExecuteSqlInterpolatedAsync($"UPDATE contracts SET expiry_date = {expiry} WHERE id = {item.ContractId}");
        }
    }

    /// <summary>AUDIT-1 §11.4 / spec §6.2 B3: with "trả về người tổng hợp ký lại" off, the head's edit goes straight on to the director.</summary>
    [Fact]
    public async Task Head_edit_can_go_on_without_the_aggregator_signing_again()
    {
        using var admin = await factory.LoginAsync("admin");
        const string key = "proposal.summary_resign_after_head_edit";
        (await admin.PutAsJsonAsync($"/api/admin/settings/{key}", new UpdateSettingRequest("false"), JsonDefaults.Options)).EnsureSuccessStatusCode().Dispose();
        try
        {
            using var department = await factory.LoginAsync("khoanoi");
            var departments = await department.GetFromJsonAsync<List<LookupItem>>("/api/lookups/departments");
            var warehouses = await department.GetFromJsonAsync<List<LookupItem>>("/api/lookups/warehouses");
            var knoi = departments!.Single(d => d.Code == "KNOI").Id;
            var warehouse = warehouses!.Single(w => w.Code == "KHO_VTYT").Id;
            var item = (await department.GetFromJsonAsync<ProposalCandidatesDto>(
                $"/api/proposals/candidates?domain=MedicalSupply&warehouseId={warehouse}&from=2026-10-01&to=2026-10-31&departmentId={knoi}", JsonDefaults.Options))!
                .Items.First(i => i.ContractRemaining >= 5);
            var proposal = await CreateAndSignProposalAsync(department, Proposal(knoi, warehouse, (item.ContractItemId, 5)));

            using var aggregator = await factory.LoginAsync("tonghopvt");
            using var created = await aggregator.PostAsJsonAsync("/api/proposal-summaries",
                new SaveSummaryRequest(ProcurementDomain.MedicalSupply, null, "Thử cấu hình ký lại", null, [proposal]), JsonDefaults.Options);
            var summaryId = await created.Content.ReadFromJsonAsync<Guid>();
            var summary = await GetSummaryAsync(aggregator, summaryId);
            (await aggregator.PostAsync(new Uri($"/api/proposal-summaries/{summaryId}/submit", UriKind.Relative), null)).Dispose();
            await SignAsync(aggregator, summary.DocumentId!.Value);

            using var head = await factory.LoginAsync("totruongvattu");
            (await head.PutAsJsonAsync($"/api/proposal-summaries/{summaryId}/approved-quantities",
                new[] { new ApprovedQuantityRequest(summary.Lines.Single().Id, 3) }, JsonDefaults.Options)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await GetSummaryAsync(head, summaryId)).Status.ShouldBe(ProposalSummaryStatus.DomainHeadReview, "not sent back to the aggregator");
            await ApproveAsync(head, summary.DocumentId!.Value);
            using var director = await factory.LoginAsync("giamdoc");
            await SignAsync(director, summary.DocumentId!.Value);

            var approved = await GetSummaryAsync(aggregator, summaryId);
            approved.Status.ShouldBe(ProposalSummaryStatus.Approved);
            approved.Lines.Single().ApprovedQuantity.ShouldBe(3);
            var wf = await aggregator.GetFromJsonAsync<WorkflowDto>($"/api/workflows/by-document/{summary.DocumentId}", JsonDefaults.Options);
            wf!.Events.ShouldNotContain(e => e.Type == WorkflowEventType.Restarted);
        }
        finally
        {
            (await admin.PutAsJsonAsync($"/api/admin/settings/{key}", new UpdateSettingRequest("true"), JsonDefaults.Options)).Dispose();
        }
    }

    /// <summary>One signed proposal of <paramref name="quantity"/> for one item, aggregated and approved by the director.</summary>
    private async Task<(Guid SummaryId, Guid LineId, Guid Warehouse)> ApprovedSummaryAsync(decimal quantity)
    {
        using var department = await factory.LoginAsync("khoanoi");
        var departments = await department.GetFromJsonAsync<List<LookupItem>>("/api/lookups/departments");
        var warehouses = await department.GetFromJsonAsync<List<LookupItem>>("/api/lookups/warehouses");
        var knoi = departments!.Single(d => d.Code == "KNOI").Id;
        var warehouse = warehouses!.Single(w => w.Code == "KHO_VTYT").Id;
        var candidates = await department.GetFromJsonAsync<ProposalCandidatesDto>(
            $"/api/proposals/candidates?domain=MedicalSupply&warehouseId={warehouse}&from=2026-10-01&to=2026-10-31&departmentId={knoi}", JsonDefaults.Options);
        var item = candidates!.Items.First(i => i.ContractRemaining >= quantity);
        var proposal = await CreateAndSignProposalAsync(department, Proposal(knoi, warehouse, (item.ContractItemId, quantity)));

        using var aggregator = await factory.LoginAsync("tonghopvt");
        using var created = await aggregator.PostAsJsonAsync("/api/proposal-summaries",
            new SaveSummaryRequest(ProcurementDomain.MedicalSupply, null, "Thử tranh chấp", null, [proposal]), JsonDefaults.Options);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var summaryId = await created.Content.ReadFromJsonAsync<Guid>();
        var summary = await GetSummaryAsync(aggregator, summaryId);
        (await aggregator.PostAsync(new Uri($"/api/proposal-summaries/{summaryId}/submit", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await SignAsync(aggregator, summary.DocumentId!.Value);
        using (var head = await factory.LoginAsync("totruongvattu"))
        {
            await ApproveAsync(head, summary.DocumentId!.Value);
        }

        using (var director = await factory.LoginAsync("giamdoc"))
        {
            await SignAsync(director, summary.DocumentId!.Value);
        }

        (await GetSummaryAsync(aggregator, summaryId)).Status.ShouldBe(ProposalSummaryStatus.Approved);
        return (summaryId, summary.Lines.Single().Id, warehouse);
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
