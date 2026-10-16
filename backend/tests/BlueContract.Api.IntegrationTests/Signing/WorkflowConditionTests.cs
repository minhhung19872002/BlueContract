using System.Net;
using System.Net.Http.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Admin;
using BlueContract.Application.Catalog;
using BlueContract.Application.Common;
using BlueContract.Application.Contracts;
using BlueContract.Application.Documents;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Workflows;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Signing;

/// <summary>
/// AUDIT-1 §11.3 / spec §5.7: the condition of a workflow template is read — a non-bid service contract without warehouse
/// receipt follows its own (shorter) workflow, while another contract keeps the default one.
/// </summary>
[Collection(ApiCollection.Name)]
public class WorkflowConditionTests(ApiFactory factory)
{
    [Fact]
    public async Task Contracts_follow_the_most_specific_matching_workflow()
    {
        using var admin = await factory.LoginAsync("admin");
        var code = "WF_HD_DV_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        using (var invalid = await admin.PostAsJsonAsync("/api/workflow-templates", Template(code, "Kind=Lump"), JsonDefaults.Options))
        {
            (await invalid.Content.ReadAsStringAsync()).ShouldContain("WORKFLOW_CONDITION_INVALID");
        }

        using var created = await admin.PostAsJsonAsync("/api/workflow-templates",
            Template(code, "ProcurementType=NonBidding; RequiresWarehouseReceipt=false"), JsonDefaults.Options);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var templateId = await created.Content.ReadFromJsonAsync<Guid>();
        try
        {
            using var buyer = await factory.LoginAsync("muasam");
            (await SubmitAsync(buyer, requiresWarehouseReceipt: false)).TemplateCode.ShouldBe(code);
            (await SubmitAsync(buyer, requiresWarehouseReceipt: true)).TemplateCode.ShouldBe("WF_CONTRACT");
        }
        finally
        {
            // Other tests expect the default contract workflow.
            (await admin.PutAsJsonAsync($"/api/workflow-templates/{templateId}",
                Template(code, "ProcurementType=NonBidding; RequiresWarehouseReceipt=false") with { IsActive = false }, JsonDefaults.Options)).Dispose();
        }
    }

    private static SaveWorkflowTemplateRequest Template(string code, string condition) =>
        new(code, "Hợp đồng dịch vụ không qua kho", DocumentType.Contract, condition, false, true,
        [
            new("REVIEW", "Tổ rà soát kiểm tra", WorkflowActionType.Review, AssigneeType.Role, RoleCatalog.Reviewer, null, true, false, [0], null, 24),
            new("DIRECTOR", "Giám đốc ký số", WorkflowActionType.Sign, AssigneeType.Role, RoleCatalog.Director, null, false, false, [0], null, 24),
        ]);

    private static async Task<WorkflowDto> SubmitAsync(HttpClient buyer, bool requiresWarehouseReceipt)
    {
        var categories = await buyer.GetFromJsonAsync<List<ItemCategoryDto>>("/api/items/categories", JsonDefaults.Options);
        var templates = await buyer.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates?kind=Contract", JsonDefaults.Options);
        var suppliers = await buyer.GetFromJsonAsync<List<LookupItem>>("/api/lookups/suppliers?search=Hồng Lam", JsonDefaults.Options);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        using var created = await buyer.PostAsJsonAsync("/api/contracts", new SaveContractRequest(
            ProcurementType.NonBidding, null, suppliers!.First().Id, null, $"DK-{tag}/2026/HĐ", "Bảo trì thiết bị", ContractKind.LumpSum, requiresWarehouseReceipt,
            categories!.Single(c => c.Code == "SUA_CHUA").Id, null, null, today, today, today.AddMonths(3), "30 ngày", null, null, null, null, null, null, null,
            templates!.Single(t => t.Code == "HD_DICHVU_KHONGTHAU").Id,
            [new ContractLineRequest(null, new NewItemRequest($"DK-{tag}", "Bảo trì tháng", "Lần", null, null, null, null, null), 1, 1_000_000, 8)]), JsonDefaults.Options);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var id = await created.Content.ReadFromJsonAsync<Guid>();
        using (var submit = await buyer.PostAsync(new Uri($"/api/contracts/{id}/submit", UriKind.Relative), null))
        {
            submit.StatusCode.ShouldBe(HttpStatusCode.NoContent, await submit.Content.ReadAsStringAsync());
        }

        var documentId = (await buyer.GetFromJsonAsync<ContractDto>($"/api/contracts/{id}", JsonDefaults.Options))!.DocumentId;
        return (await buyer.GetFromJsonAsync<WorkflowDto>($"/api/workflows/by-document/{documentId}", JsonDefaults.Options))!;
    }
}
