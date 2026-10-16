using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Admin;
using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Signing;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Workflows;
using BlueContract.Infrastructure.Documents.Templates;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Signing;

/// <summary>
/// AUDIT-1 §10.1 / spec §5.7: a signed version is immutable. Uploading a new version (DOCX or PDF) while the document is in
/// a signing workflow is refused unless the active step allows editing, and once a signature exists the workflow restarts —
/// both upload formats behave exactly like the online editor.
/// </summary>
[Collection(ApiCollection.Name)]
public class VersionLockTests(ApiFactory factory)
{
    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray();

    public static TheoryData<string> Formats => ["pdf", "docx"];

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task Upload_is_refused_while_the_active_step_is_not_the_uploaders(string format)
    {
        using var admin = await factory.LoginAsync("admin");
        var docId = await StartAsync(admin, editingByAdmin: false);

        using var upload = await admin.PostAsync(new Uri($"/api/documents/{docId}/versions", UriKind.Relative), Form(format));
        upload.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await upload.Content.ReadAsStringAsync());
        (await upload.Content.ReadAsStringAsync()).ShouldContain("DOCUMENT_LOCKED_IN_WORKFLOW");
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task Upload_after_a_signature_restarts_the_workflow(string format)
    {
        using var admin = await factory.LoginAsync("admin");
        var docId = await StartAsync(admin, editingByAdmin: true);

        using (var storekeeper = await factory.LoginAsync("thukho"))
        {
            var wf0 = await WorkflowAsync(storekeeper, docId);
            using var sign = await storekeeper.PostAsJsonAsync($"/api/signing/steps/{wf0.MyStepId}/sign", new BeginSignRequest("MOCK", null, null), JsonDefaults.Options);
            sign.StatusCode.ShouldBe(HttpStatusCode.OK, await sign.Content.ReadAsStringAsync());
        }

        // Step 2 (admin, editing allowed) replaces the content: the storekeeper signed the old one and must sign again.
        using var upload = await admin.PostAsync(new Uri($"/api/documents/{docId}/versions", UriKind.Relative), Form(format));
        upload.StatusCode.ShouldBe(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());

        var wf = await WorkflowAsync(admin, docId);
        wf.Status.ShouldBe(WorkflowStatus.Running);
        wf.Steps.Single(s => s.Order == 1).Status.ShouldBe(StepStatus.Active);
        wf.Steps.Single(s => s.Order == 2).Status.ShouldBe(StepStatus.Pending);
        wf.Events.ShouldContain(e => e.Type == WorkflowEventType.Restarted);
    }

    private static async Task<Guid> StartAsync(HttpClient admin, bool editingByAdmin)
    {
        var users = await admin.GetFromJsonAsync<PagedResult<UserListItemDto>>("/api/admin/users?pageSize=100", JsonDefaults.Options);
        var adminId = users!.Items.Single(u => u.UserName == "admin").Id;
        var code = "WF_LOCK_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        using var created = await admin.PostAsJsonAsync("/api/workflow-templates", new SaveWorkflowTemplateRequest(code, "Thử khóa phiên bản", DocumentType.TemplateSample, null, false, true,
        [
            new("STOREKEEPER", "Thủ kho ký", WorkflowActionType.Sign, AssigneeType.Role, "STOREKEEPER", null, false, false, [0], null, null),
            new("EDITOR", "Người sửa", WorkflowActionType.Approve, AssigneeType.User, null, adminId, editingByAdmin, false, [0, 1], null, null),
        ]), JsonDefaults.Options);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());

        var templates = await admin.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates", JsonDefaults.Options);
        using var sample = await admin.PostAsJsonAsync("/api/documents/samples", new CreateSampleDocumentRequest(templates!.Single(t => t.Code == "PHIEU_NHAP_KHO").Id, null));
        var docId = await sample.Content.ReadFromJsonAsync<Guid>();
        var workflowTemplates = await admin.GetFromJsonAsync<List<WorkflowTemplateDto>>("/api/workflow-templates", JsonDefaults.Options);
        using var start = await admin.PostAsJsonAsync("/api/workflows", new StartWorkflowRequest(docId, workflowTemplates!.Single(t => t.Code == code).Id, null, null, null), JsonDefaults.Options);
        start.StatusCode.ShouldBe(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
        return docId;
    }

    private static async Task<WorkflowDto> WorkflowAsync(HttpClient client, Guid docId) =>
        (await client.GetFromJsonAsync<WorkflowDto>($"/api/workflows/by-document/{docId}", JsonDefaults.Options))!;

    private static MultipartFormDataContent Form(string format)
    {
        var (bytes, type, name) = format == "pdf"
            ? (Pdf, "application/pdf", "ban-moi.pdf")
            : (new DocxBuilder().Title("PHIẾU NHẬP KHO (bản sửa)").Build(), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "ban-moi.docx");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new MultipartFormDataContent { { file, "file", name }, { new StringContent("Bản sửa"), "reason" } };
    }
}
