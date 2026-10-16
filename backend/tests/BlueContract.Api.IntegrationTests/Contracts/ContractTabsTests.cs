using System.Net;
using System.Net.Http.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Admin;
using BlueContract.Application.Catalog;
using BlueContract.Application.Common;
using BlueContract.Application.Contracts;
using BlueContract.Application.Documents;
using BlueContract.Application.Proposals;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Identity;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Contracts;

/// <summary>
/// AUDIT-1 §11.2 / spec §9.1-5: the contract page has the "Đề xuất &amp; gọi hàng" tab (proposals and call orders of the
/// contract) and a "Lịch sử" tab built from the audit trail of the contract, its documents and appendices.
/// </summary>
[Collection(ApiCollection.Name)]
public class ContractTabsTests(ApiFactory factory)
{
    [Fact]
    public async Task Call_orders_and_proposals_are_listed_per_contract()
    {
        using var buyer = await factory.LoginAsync("muasam");
        var (fresh, _) = await CreateContractAsync(buyer, submit: false);

        var all = await buyer.GetFromJsonAsync<PagedResult<CallOrderListItemDto>>("/api/call-orders?pageSize=100", JsonDefaults.Options);
        all!.Total.ShouldBeGreaterThan(0);
        var order = await buyer.GetFromJsonAsync<CallOrderDto>($"/api/call-orders/{all.Items[0].Id}", JsonDefaults.Options);
        var number = order!.Lines[0].ContractNumber;
        var contracts = await buyer.GetFromJsonAsync<PagedResult<ContractListItemDto>>(
            $"/api/contracts?search={Uri.EscapeDataString(number)}&pageSize=10", JsonDefaults.Options);
        var contractId = contracts!.Items.Single(c => c.Number == number).Id;

        var ofContract = await buyer.GetFromJsonAsync<PagedResult<CallOrderListItemDto>>(
            $"/api/call-orders?contractId={contractId}&pageSize=100", JsonDefaults.Options);
        ofContract!.Items.ShouldContain(o => o.Id == order.Id);
        foreach (var o in ofContract.Items)
        {
            var detail = await buyer.GetFromJsonAsync<CallOrderDto>($"/api/call-orders/{o.Id}", JsonDefaults.Options);
            detail!.Lines.ShouldContain(l => l.ContractNumber == number);
        }

        (await buyer.GetFromJsonAsync<PagedResult<CallOrderListItemDto>>(
            $"/api/call-orders?contractId={fresh}&pageSize=100", JsonDefaults.Options))!.Total.ShouldBe(0);

        // A department drafts a proposal on an item of an effective contract.
        using var department = await factory.LoginAsync("khoanoi");
        var departments = await department.GetFromJsonAsync<List<LookupItem>>("/api/lookups/departments");
        var warehouses = await department.GetFromJsonAsync<List<LookupItem>>("/api/lookups/warehouses");
        var knoi = departments!.Single(d => d.Code == "KNOI").Id;
        var warehouse = warehouses!.Single(w => w.Code == "KHO_VTYT").Id;
        var candidates = await department.GetFromJsonAsync<ProposalCandidatesDto>(
            $"/api/proposals/candidates?domain=MedicalSupply&warehouseId={warehouse}&from=2026-10-01&to=2026-10-31&departmentId={knoi}", JsonDefaults.Options);
        var item = candidates!.Items[0];
        using var drafted = await department.PostAsJsonAsync("/api/proposals", new SaveProposalRequest(
            knoi, warehouse, ProcurementDomain.MedicalSupply, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), null,
            [new ProposalLineRequest(item.ContractItemId, 1, null)]), JsonDefaults.Options);
        drafted.StatusCode.ShouldBe(HttpStatusCode.OK, await drafted.Content.ReadAsStringAsync());
        var proposalId = await drafted.Content.ReadFromJsonAsync<Guid>();

        (await department.GetFromJsonAsync<PagedResult<ProposalListItemDto>>(
            $"/api/proposals?contractId={item.ContractId}&pageSize=100", JsonDefaults.Options))!.Items.ShouldContain(p => p.Id == proposalId);
        (await department.GetFromJsonAsync<PagedResult<ProposalListItemDto>>(
            $"/api/proposals?contractId={fresh}&pageSize=100", JsonDefaults.Options))!.Total.ShouldBe(0);
    }

    [Fact]
    public async Task The_history_tab_lists_the_audit_trail_of_the_contract_and_its_document()
    {
        using var buyer = await factory.LoginAsync("muasam");
        var (id, documentId) = await CreateContractAsync(buyer, submit: true);

        using var response = await buyer.GetAsync(new Uri($"/api/contracts/{id}/history", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var history = (await response.Content.ReadFromJsonAsync<List<HistoryView>>(JsonDefaults.Options))!;
        history.ShouldContain(h => h.Action == "ContractCreate" && h.Subject == "Contract");
        history.ShouldContain(h => h.Action == "WorkflowStart" && h.Subject == "Document");
        history.ShouldAllBe(h => h.Action != "View" && h.Action != "Download");
        history.Select(h => h.At).ShouldBeInOrder(SortDirection.Descending);
        history.ShouldContain(h => h.UserName == "muasam");

        // Another supplier cannot read it.
        using var other = await factory.LoginAsync("ncc1");
        using var hidden = await other.GetAsync(new Uri($"/api/contracts/{id}/history", UriKind.Relative));
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        documentId.ShouldNotBe(Guid.Empty);
    }

    private sealed record HistoryView(DateTimeOffset At, string? UserName, string Action, string Subject, string? Note, string? Details);

    private static async Task<(Guid Id, Guid DocumentId)> CreateContractAsync(HttpClient buyer, bool submit)
    {
        var categories = await buyer.GetFromJsonAsync<List<ItemCategoryDto>>("/api/items/categories", JsonDefaults.Options);
        var templates = await buyer.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates?kind=Contract", JsonDefaults.Options);
        var suppliers = await buyer.GetFromJsonAsync<List<LookupItem>>("/api/lookups/suppliers?search=Hồng Lam", JsonDefaults.Options);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        using var created = await buyer.PostAsJsonAsync("/api/contracts", new SaveContractRequest(
            ProcurementType.NonBidding, null, suppliers!.First().Id, null, $"LS-{tag}/2026/HĐ", "Bảo trì thiết bị", ContractKind.LumpSum, true,
            categories!.Single(c => c.Code == "SUA_CHUA").Id, null, null, today, today, today.AddMonths(3), "30 ngày", null, null, null, null, null, null, null,
            templates!.Single(t => t.Code == "HD_DICHVU_KHONGTHAU").Id,
            [new ContractLineRequest(null, new NewItemRequest($"LS-{tag}", "Bảo trì tháng", "Lần", null, null, null, null, null), 1, 1_000_000, 8)]), JsonDefaults.Options);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var id = await created.Content.ReadFromJsonAsync<Guid>();
        if (submit)
        {
            using var sent = await buyer.PostAsync(new Uri($"/api/contracts/{id}/submit", UriKind.Relative), null);
            sent.StatusCode.ShouldBe(HttpStatusCode.NoContent, await sent.Content.ReadAsStringAsync());
        }

        var contract = await buyer.GetFromJsonAsync<ContractDto>($"/api/contracts/{id}", JsonDefaults.Options);
        return (id, contract!.DocumentId ?? Guid.Empty);
    }
}
