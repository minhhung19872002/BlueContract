using System.Net;
using System.Net.Http.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Signing;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Workflows;
using BlueContract.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Signing;

/// <summary>
/// AUDIT-1 §10.3: the permission matrix governs signing. Removing <c>contract.sign</c> / <c>contract.initial</c> /
/// <c>contract.seal</c> from a role must stop its holders from signing / initialling / sealing a contract.
/// </summary>
[Collection(ApiCollection.Name)]
public class SigningPermissionTests(ApiFactory factory)
{
    public static TheoryData<WorkflowActionType, string, string, string> Steps => new()
    {
        { WorkflowActionType.Sign, RoleCatalog.Director, "giamdoc", Permissions.ContractSign },
        { WorkflowActionType.Initial, RoleCatalog.Accountant, "ketoan", Permissions.ContractInitial },
        { WorkflowActionType.OrgSeal, RoleCatalog.Clerk, "vanthu", Permissions.ContractSeal },
    };

    [Theory]
    [MemberData(nameof(Steps))]
    public async Task Contract_step_needs_the_matching_permission(WorkflowActionType action, string role, string user, string permission)
    {
        using var admin = await factory.LoginAsync("admin");
        var docId = await StartContractAsync(admin, action, role);

        await SetPermissionAsync(role, permission, granted: false);
        try
        {
            using var signer = await factory.LoginAsync(user);
            var wf = await signer.GetFromJsonAsync<WorkflowDto>($"/api/workflows/by-document/{docId}", JsonDefaults.Options);
            wf!.MyStepId.ShouldBeNull("the step must not be offered without the permission");
            var stepId = wf.Steps.Single().Id;
            using var refused = await signer.PostAsJsonAsync($"/api/signing/steps/{stepId}/sign", new BeginSignRequest("MOCK", null, null), JsonDefaults.Options);
            refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync());
            (await refused.Content.ReadAsStringAsync()).ShouldContain("WORKFLOW_PERMISSION_MISSING");
        }
        finally
        {
            await SetPermissionAsync(role, permission, granted: true);
        }

        using var allowed = await factory.LoginAsync(user);
        var again = await allowed.GetFromJsonAsync<WorkflowDto>($"/api/workflows/by-document/{docId}", JsonDefaults.Options);
        using var signed = await allowed.PostAsJsonAsync($"/api/signing/steps/{again!.MyStepId}/sign", new BeginSignRequest("MOCK", null, null), JsonDefaults.Options);
        signed.StatusCode.ShouldBe(HttpStatusCode.OK, await signed.Content.ReadAsStringAsync());
    }

    private async Task SetPermissionAsync(string roleCode, string permission, bool granted)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = await db.Roles.Include(r => r.Permissions).SingleAsync(r => r.Code == roleCode);
        var codes = role.Permissions.Select(p => p.PermissionCode).Where(p => p != permission).ToList();
        if (granted)
        {
            codes.Add(permission);
        }

        role.SetPermissions(codes);
        await db.SaveChangesAsync();
    }

    /// <summary>A one-step workflow over a document of type Contract.</summary>
    private async Task<Guid> StartContractAsync(HttpClient admin, WorkflowActionType action, string role)
    {
        var code = "WF_PERM_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        using var created = await admin.PostAsJsonAsync("/api/workflow-templates", new SaveWorkflowTemplateRequest(code, "Thử quyền ký", DocumentType.Contract, null, false, true,
            [new("STEP", "Bước thử", action, AssigneeType.Role, role, null, false, false, [0], null, null)]), JsonDefaults.Options);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());

        var templates = await admin.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates", JsonDefaults.Options);
        using var sample = await admin.PostAsJsonAsync("/api/documents/samples", new CreateSampleDocumentRequest(templates!.Single(t => t.Code == "HD_THUOC").Id, null));
        var docId = await sample.Content.ReadFromJsonAsync<Guid>();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Documents.Where(d => d.Id == docId)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.Type, DocumentType.Contract));
        }

        // Starting a contract's workflow takes contract.create (Tổ Mua sắm).
        using var buyer = await factory.LoginAsync("muasam");
        var workflowTemplates = await admin.GetFromJsonAsync<List<WorkflowTemplateDto>>("/api/workflow-templates", JsonDefaults.Options);
        using var start = await buyer.PostAsJsonAsync("/api/workflows", new StartWorkflowRequest(docId, workflowTemplates!.Single(t => t.Code == code).Id, null, null, null), JsonDefaults.Options);
        start.StatusCode.ShouldBe(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
        return docId;
    }
}
