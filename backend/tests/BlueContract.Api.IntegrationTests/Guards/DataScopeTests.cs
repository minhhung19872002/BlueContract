using System.Net;
using System.Net.Http.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Admin;
using BlueContract.Application.Archive;
using BlueContract.Application.Common;
using BlueContract.Application.Contracts;
using BlueContract.Application.Documents;
using BlueContract.Application.Payments;
using BlueContract.Application.Proposals;
using BlueContract.Application.Receipts;
using BlueContract.Application.Reports;
using BlueContract.Application.Signing;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Identity;
using BlueContract.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Guards;

/// <summary>
/// AUDIT-1 §10.7 / spec §4.2: a user limited to a domain sees only that domain's contracts and everything tied to them —
/// in every list, count, report and dashboard — and a deputy signs within the delegator's scope, not the deputy's own.
/// </summary>
[Collection(ApiCollection.Name)]
public class DataScopeTests(ApiFactory factory)
{
    [Fact]
    public async Task Domain_scope_limits_every_list_report_and_detail()
    {
        HashSet<Guid> inScope;
        Guid outContract;
        Guid? outReceipt;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var byDomain = await db.Contracts.AsNoTracking()
                .Select(c => new { c.Id, Domain = db.ItemCategories.Where(x => x.Id == c.CategoryId).Select(x => x.Domain).First() }).ToListAsync();
            inScope = [.. byDomain.Where(c => c.Domain == ProcurementDomain.Drug).Select(c => c.Id)];
            var outside = byDomain.Where(c => c.Domain != ProcurementDomain.Drug).Select(c => c.Id).ToList();
            outside.ShouldNotBeEmpty();
            outContract = outside[0];
            outReceipt = await db.WarehouseReceipts.Where(r => r.ContractId != null && outside.Contains(r.ContractId.Value)).Select(r => (Guid?)r.Id).FirstOrDefaultAsync();
            await SetScopesAsync(db, "ketoan", [(DataScopeType.Domain, nameof(ProcurementDomain.Drug))]);
        }

        try
        {
            using var accountant = await factory.LoginAsync("ketoan");

            var contracts = await accountant.GetFromJsonAsync<PagedResult<ContractListItemDto>>("/api/contracts?pageSize=200", JsonDefaults.Options);
            contracts!.Items.ShouldNotBeEmpty();
            contracts.Items.ShouldAllBe(c => inScope.Contains(c.Id));
            contracts.Total.ShouldBe(contracts.Items.Count, "the count must come from the same filtered query");
            (await accountant.GetAsync(new Uri($"/api/contracts/{outContract}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

            var receipts = await accountant.GetFromJsonAsync<PagedResult<ReceiptListItemDto>>("/api/warehouse-receipts?pageSize=200", JsonDefaults.Options);
            receipts!.Items.Where(r => r.ContractId != null).ShouldAllBe(r => inScope.Contains(r.ContractId!.Value));
            receipts.Total.ShouldBe(receipts.Items.Count);
            if (outReceipt is { } receiptId)
            {
                (await accountant.GetAsync(new Uri($"/api/warehouse-receipts/{receiptId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            }

            (await accountant.GetFromJsonAsync<PagedResult<AcceptanceListItemDto>>("/api/acceptances?pageSize=200", JsonDefaults.Options))!
                .Items.ShouldAllBe(a => inScope.Contains(a.ContractId));
            (await accountant.GetFromJsonAsync<PagedResult<ValuationListItemDto>>("/api/valuations?pageSize=200", JsonDefaults.Options))!
                .Items.ShouldAllBe(v => inScope.Contains(v.ContractId));
            (await accountant.GetFromJsonAsync<PagedResult<LiquidationListItemDto>>("/api/liquidations?pageSize=200", JsonDefaults.Options))!
                .Items.ShouldAllBe(l => inScope.Contains(l.ContractId));
            (await accountant.GetAsync(new Uri($"/api/liquidations/preview?contractId={outContract}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

            (await accountant.GetFromJsonAsync<PagedResult<ArchiveSearchItemDto>>("/api/archive?pageSize=200", JsonDefaults.Options))!
                .Items.ShouldAllBe(a => inScope.Contains(a.ContractId));
            (await accountant.GetAsync(new Uri($"/api/archive/{outContract}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

            (await accountant.GetFromJsonAsync<List<ContractExecutionRowDto>>("/api/reports/contract-execution", JsonDefaults.Options))!
                .ShouldAllBe(r => inScope.Contains(r.ContractId));

            var dashboard = await accountant.GetFromJsonAsync<InternalDashboardDto>("/api/dashboard", JsonDefaults.Options);
            dashboard!.ExpiringContracts.ShouldAllBe(c => inScope.Contains(c.Id));
            dashboard.LowQuantityItems.ShouldAllBe(i => inScope.Contains(i.ContractId));
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await SetScopesAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), "ketoan", []);
        }
    }

    /// <summary>A deputy limited to "Vật tư y tế" signs a "Thuốc" document for an unlimited delegator.</summary>
    [Fact]
    public async Task Deputy_signs_within_the_delegators_scope()
    {
        using var admin = await factory.LoginAsync("admin");
        var users = await admin.GetFromJsonAsync<PagedResult<UserListItemDto>>("/api/admin/users?pageSize=100", JsonDefaults.Options);
        var chief = users!.Items.Single(u => u.UserName == "ketoantruong").Id;
        var deputy = users.Items.Single(u => u.UserName == "totruongvattu").Id;
        using var delegation = await admin.PostAsJsonAsync("/api/admin/delegations", new CreateDelegationRequest(
            chief, deputy, "Kế toán trưởng", "Tổ trưởng Vật tư", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1), "Thử phạm vi"), JsonDefaults.Options);
        delegation.StatusCode.ShouldBe(HttpStatusCode.OK, await delegation.Content.ReadAsStringAsync());
        var delegationId = await delegation.Content.ReadFromJsonAsync<Guid>();
        try
        {
            var templates = await admin.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates", JsonDefaults.Options);
            using var sample = await admin.PostAsJsonAsync("/api/documents/samples", new CreateSampleDocumentRequest(templates!.Single(t => t.Code == "PHIEU_NHAP_KHO").Id, null));
            var docId = await sample.Content.ReadFromJsonAsync<Guid>();
            var code = "WF_SCOPE_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            using (var created = await admin.PostAsJsonAsync("/api/workflow-templates", new SaveWorkflowTemplateRequest(code, "Thử phạm vi ủy quyền",
                       Domain.Documents.DocumentType.TemplateSample, null, false, true,
                       [new("CHIEF", "Kế toán trưởng ký", Domain.Workflows.WorkflowActionType.Sign, Domain.Workflows.AssigneeType.Role, RoleCatalog.ChiefAccountant, null, false, false, [0], null, null)]),
                       JsonDefaults.Options))
            {
                created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
            }

            var workflowTemplates = await admin.GetFromJsonAsync<List<WorkflowTemplateDto>>("/api/workflow-templates", JsonDefaults.Options);
            using (var start = await admin.PostAsJsonAsync("/api/workflows", new StartWorkflowRequest(docId, workflowTemplates!.Single(t => t.Code == code).Id, null, null,
                       nameof(ProcurementDomain.Drug)), JsonDefaults.Options))
            {
                start.StatusCode.ShouldBe(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
            }

            using var deputyClient = await factory.LoginAsync("totruongvattu");
            var inbox = await deputyClient.GetFromJsonAsync<PagedResult<InboxItemDto>>("/api/signing/inbox?tab=Pending&pageSize=100", JsonDefaults.Options);
            inbox!.Items.ShouldContain(i => i.DocumentId == docId, "the delegated step belongs to the delegator's scope");
            var wf = await deputyClient.GetFromJsonAsync<WorkflowDto>($"/api/workflows/by-document/{docId}", JsonDefaults.Options);
            wf!.MyStepId.ShouldNotBeNull();
            wf.MyCaption.ShouldBe("KT. KẾ TOÁN TRƯỞNG");
            using var sign = await deputyClient.PostAsJsonAsync($"/api/signing/steps/{wf.MyStepId}/sign", new BeginSignRequest("MOCK", null, null), JsonDefaults.Options);
            sign.StatusCode.ShouldBe(HttpStatusCode.OK, await sign.Content.ReadAsStringAsync());
        }
        finally
        {
            (await admin.PostAsync(new Uri($"/api/admin/delegations/{delegationId}/revoke", UriKind.Relative), null)).Dispose();
        }
    }

    /// <summary>Found while writing §10.7: adding a data scope to an existing user failed with 409 (row taken for an existing one).</summary>
    [Fact]
    public async Task Admin_adds_a_data_scope_to_an_existing_user()
    {
        using var admin = await factory.LoginAsync("admin");
        var users = await admin.GetFromJsonAsync<PagedResult<UserListItemDto>>("/api/admin/users?pageSize=100", JsonDefaults.Options);
        var id = users!.Items.Single(u => u.UserName == "xem").Id;
        var user = (await admin.GetFromJsonAsync<UserDetailDto>($"/api/admin/users/{id}", JsonDefaults.Options))!;
        UpdateUserRequest Request(IReadOnlyList<DataScopeDto> scopes) => new(user.FullName, user.Email, user.Phone, user.JobTitle, user.DepartmentId, user.FacilityId,
            user.Status, user.RoleIds, scopes, user.CertificateSerial, user.CertificateIssuer, user.CertificateValidTo);

        using var added = await admin.PutAsJsonAsync($"/api/admin/users/{id}", Request([.. user.DataScopes, new DataScopeDto(DataScopeType.Domain, nameof(ProcurementDomain.Drug))]), JsonDefaults.Options);
        added.IsSuccessStatusCode.ShouldBeTrue(await added.Content.ReadAsStringAsync());
        (await admin.GetFromJsonAsync<UserDetailDto>($"/api/admin/users/{id}", JsonDefaults.Options))!.DataScopes
            .ShouldContain(d => d.Type == DataScopeType.Domain && d.Value == nameof(ProcurementDomain.Drug));

        using var restored = await admin.PutAsJsonAsync($"/api/admin/users/{id}", Request(user.DataScopes), JsonDefaults.Options);
        restored.IsSuccessStatusCode.ShouldBeTrue(await restored.Content.ReadAsStringAsync());
    }

    /// <summary>Plain SQL, so the setup does not depend on the EF mapping under test.</summary>
    private static async Task SetScopesAsync(AppDbContext db, string userName, (DataScopeType Type, string Value)[] scopes)
    {
        var userId = await db.Users.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM user_data_scopes WHERE user_id = {userId}");
        foreach (var (type, value) in scopes)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO user_data_scopes (id, user_id, scope_type, value) VALUES ({Guid.CreateVersion7()}, {userId}, {type.ToString()}, {value})");
        }
    }
}
