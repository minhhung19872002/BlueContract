using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Admin;
using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Signing;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Workflows;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Signing;

/// <summary>End-to-end Phase 3 acceptance: the five-signature warehouse receipt sample signed through the API.</summary>
[Collection(ApiCollection.Name)]
public class WorkflowSigningApiTests(ApiFactory factory)
{
    private static readonly string[] ReceiptSigners = ["thukho", "ncc1", "ketoankho", "truongkhoa", "ketoantruong"];

    [Fact]
    public async Task Five_signers_sign_the_sample_receipt_and_every_signature_verifies()
    {
        using var admin = await factory.LoginAsync("admin");
        var (docId, _) = await StartSampleAsync(admin);

        foreach (var user in ReceiptSigners)
        {
            using var signer = await factory.LoginAsync(user);
            var count = await signer.GetFromJsonAsync<InboxCountDto>("/api/signing/inbox/count", JsonDefaults.Options);
            count!.Pending.ShouldBeGreaterThanOrEqualTo(1, $"{user} should have the document in the inbox");

            var request = await SignAsync(signer, await MyStepAsync(signer, docId));
            request.Status.ShouldBe(SigningRequestStatus.Completed, $"{user}: {request.Error}");
        }

        var wf = await GetWorkflowAsync(admin, docId);
        wf.Status.ShouldBe(WorkflowStatus.Completed);
        wf.Signatures.Count.ShouldBe(5);
        wf.Signatures.Select(s => s.HashAfter).Distinct().Count().ShouldBe(5);

        var document = await admin.GetFromJsonAsync<DocumentDto>($"/api/documents/{docId}", JsonDefaults.Options);
        var verify = await admin.GetFromJsonAsync<VerifyResultDto>(
            $"/api/signing/documents/{docId}/versions/{document!.Versions[0].Id}/verify", JsonDefaults.Options);
        verify!.SignatureCount.ShouldBe(5);
        verify.AllValid.ShouldBeTrue();
        verify.Signatures[^1].CoversWholeDocument.ShouldBeTrue();
    }

    [Fact]
    public async Task Signing_twice_is_idempotent_and_wrong_user_is_refused()
    {
        using var admin = await factory.LoginAsync("admin");
        var (docId, _) = await StartSampleAsync(admin);
        using var storekeeper = await factory.LoginAsync("thukho");
        var stepId = await MyStepAsync(storekeeper, docId);

        // Someone else cannot sign the storekeeper's step.
        using var accountant = await factory.LoginAsync("ketoankho");
        using var foreign = await accountant.PostAsJsonAsync($"/api/signing/steps/{stepId}/sign", new BeginSignRequest(null, null, null), JsonDefaults.Options);
        foreign.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Two simultaneous clicks produce exactly one signature.
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            storekeeper.PostAsJsonAsync($"/api/signing/steps/{stepId}/sign", new BeginSignRequest("MOCK", null, null), JsonDefaults.Options)));
        attempts.Count(a => a.IsSuccessStatusCode).ShouldBeGreaterThanOrEqualTo(1);
        attempts.ShouldAllBe(a => a.IsSuccessStatusCode || a.StatusCode == HttpStatusCode.Conflict);

        var again = await SignAsync(storekeeper, stepId);
        again.Status.ShouldBe(SigningRequestStatus.Completed);
        (await GetWorkflowAsync(admin, docId)).Signatures.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Return_to_a_step_reactivates_it_and_records_the_reason()
    {
        using var admin = await factory.LoginAsync("admin");
        var (docId, _) = await StartSampleAsync(admin);
        foreach (var user in ReceiptSigners[..2])
        {
            using var signer = await factory.LoginAsync(user);
            await SignAsync(signer, await MyStepAsync(signer, docId));
        }

        using var accountant = await factory.LoginAsync("ketoankho");
        var stepId = await MyStepAsync(accountant, docId);
        using var empty = await accountant.PostAsJsonAsync($"/api/workflows/steps/{stepId}/return", new ReturnStepRequest(1, ""), JsonDefaults.Options);
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var returned = await accountant.PostAsJsonAsync($"/api/workflows/steps/{stepId}/return", new ReturnStepRequest(1, "Sai số lượng giao"), JsonDefaults.Options);
        returned.StatusCode.ShouldBe(HttpStatusCode.OK);
        var wf = await returned.Content.ReadFromJsonAsync<WorkflowDto>(JsonDefaults.Options);
        wf!.Steps.Single(s => s.Order == 1).Status.ShouldBe(StepStatus.Active);
        wf.Events.ShouldContain(e => e.Type == WorkflowEventType.Returned && e.Note == "Sai số lượng giao");

        // The storekeeper (step 1) has it again in "Chờ tôi xử lý"; the supplier's later step must be signed again too.
        wf.Steps.Single(s => s.Order == 2).Status.ShouldBe(StepStatus.Pending);
        using var storekeeper = await factory.LoginAsync("thukho");
        (await MyStepAsync(storekeeper, docId)).ShouldBe(wf.Steps.Single(s => s.Order == 1).Id);
    }

    [Fact]
    public async Task Remote_signing_is_polled_until_the_user_confirms()
    {
        using var admin = await factory.LoginAsync("admin");
        var (docId, _) = await StartSampleAsync(admin);
        using var storekeeper = await factory.LoginAsync("thukho");

        var begun = await SignAsync(storekeeper, await MyStepAsync(storekeeper, docId), "MOCK_REMOTE");
        begun.Status.ShouldBe(SigningRequestStatus.Pending);
        begun.Mode.ShouldBe(SigningMode.RemoteAsync);
        begun.Message.ShouldNotBeNullOrEmpty();

        var polled = await storekeeper.GetFromJsonAsync<SigningRequestDto>($"/api/signing/requests/{begun.RequestId}", JsonDefaults.Options);
        polled!.Status.ShouldBe(SigningRequestStatus.Completed);
        polled.SignatureId.ShouldNotBeNull();

        // Another user cannot poll someone else's request.
        using var other = await factory.LoginAsync("ketoankho");
        (await other.GetAsync(new Uri($"/api/signing/requests/{begun.RequestId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Usb_token_flow_signs_with_the_value_returned_by_the_plugin()
    {
        using var admin = await factory.LoginAsync("admin");
        var (docId, _) = await StartSampleAsync(admin);
        using var storekeeper = await factory.LoginAsync("thukho");
        var stepId = await MyStepAsync(storekeeper, docId);

        using var rsa = RSA.Create(2048);
        var csr = new CertificateRequest("CN=Thủ kho USB, O=Bệnh viện", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var token = csr.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        var begun = await SignAsync(storekeeper, stepId, "USB_TOKEN", Convert.ToBase64String(token.RawData));
        begun.Status.ShouldBe(SigningRequestStatus.Pending);
        begun.DataToSign.ShouldNotBeNull();

        // A wrong signature value is refused…
        using var bad = await storekeeper.PostAsJsonAsync($"/api/signing/requests/{begun.RequestId}/complete",
            new CompleteSignRequest(Convert.ToBase64String(new byte[256])), JsonDefaults.Options);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // …the plugin's real one completes the step.
        var value = rsa.SignData(Convert.FromBase64String(begun.DataToSign!), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var complete = await storekeeper.PostAsJsonAsync($"/api/signing/requests/{begun.RequestId}/complete",
            new CompleteSignRequest(Convert.ToBase64String(value)), JsonDefaults.Options);
        complete.StatusCode.ShouldBe(HttpStatusCode.OK);

        var wf = await GetWorkflowAsync(admin, docId);
        wf.Signatures.Single().CertificateSubject!.ShouldContain("Thủ kho USB");
        wf.Steps[0].Status.ShouldBe(StepStatus.Done);
    }

    [Fact]
    public async Task Batch_sign_signs_every_pending_step_of_the_user()
    {
        using var admin = await factory.LoginAsync("admin");
        var first = await StartSampleAsync(admin);
        var second = await StartSampleAsync(admin);
        using var storekeeper = await factory.LoginAsync("thukho");
        var steps = new[] { await MyStepAsync(storekeeper, first.DocumentId), await MyStepAsync(storekeeper, second.DocumentId) };

        using var response = await storekeeper.PostAsJsonAsync("/api/signing/batch", new BatchSignRequest(steps, "MOCK"), JsonDefaults.Options);
        var results = await response.Content.ReadFromJsonAsync<List<BatchSignResultDto>>(JsonDefaults.Options);

        results!.Count.ShouldBe(2);
        results.ShouldAllBe(r => r.Success && r.Request!.Status == SigningRequestStatus.Completed);
    }

    [Fact]
    public async Task Delegate_signs_on_behalf_with_KT_caption()
    {
        using var admin = await factory.LoginAsync("admin");
        var users = await admin.GetFromJsonAsync<PagedResult<UserListItemDto>>("/api/admin/users?pageSize=100", JsonDefaults.Options);
        var chief = users!.Items.Single(u => u.UserName == "ketoantruong").Id;
        var deputy = users.Items.Single(u => u.UserName == "tonghopvt").Id;
        using var delegation = await admin.PostAsJsonAsync("/api/admin/delegations", new CreateDelegationRequest(
            chief, deputy, "Kế toán trưởng", "Phó phòng Tài chính kế toán", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1), "Đi công tác"), JsonDefaults.Options);
        delegation.StatusCode.ShouldBe(HttpStatusCode.OK, await delegation.Content.ReadAsStringAsync());

        var (docId, _) = await StartSampleAsync(admin);
        foreach (var user in ReceiptSigners[..4])
        {
            using var signer = await factory.LoginAsync(user);
            await SignAsync(signer, await MyStepAsync(signer, docId));
        }

        using var delegate_ = await factory.LoginAsync("tonghopvt");
        var stepId = await MyStepAsync(delegate_, docId);
        (await GetWorkflowAsync(delegate_, docId)).MyCaption.ShouldBe("KT. KẾ TOÁN TRƯỞNG");
        (await SignAsync(delegate_, stepId)).Status.ShouldBe(SigningRequestStatus.Completed);

        var wf = await GetWorkflowAsync(admin, docId);
        wf.Status.ShouldBe(WorkflowStatus.Completed);
        wf.Signatures[^1].Caption.ShouldBe("KT. KẾ TOÁN TRƯỞNG");
        wf.Signatures[^1].SignerTitle.ShouldBe("Phó phòng Tài chính kế toán");
    }

    [Fact]
    public async Task Initial_needs_no_CA_and_custom_templates_are_validated()
    {
        using var admin = await factory.LoginAsync("admin");
        var code = "WF_INITIAL_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var steps = new List<SaveWorkflowStepRequest>
        {
            new("INITIAL", "Kế toán kho ký nháy", WorkflowActionType.Initial, AssigneeType.Role, "WAREHOUSE_ACCOUNTANT", null, false, false, [0], null, 24),
            new("REVIEW", "Kế toán trưởng duyệt", WorkflowActionType.Approve, AssigneeType.Role, "CHIEF_ACCOUNTANT", null, false, false, [0, 1], null, null),
        };

        // A return target pointing forward is rejected.
        using var invalid = await admin.PostAsJsonAsync("/api/workflow-templates", new SaveWorkflowTemplateRequest(
            code, "Thử ký nháy", DocumentType.TemplateSample, null, false, true, [steps[0] with { ReturnTo = [2] }, steps[1]]), JsonDefaults.Options);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var created = await admin.PostAsJsonAsync("/api/workflow-templates", new SaveWorkflowTemplateRequest(
            code, "Thử ký nháy", DocumentType.TemplateSample, null, false, true, steps), JsonDefaults.Options);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());

        var (docId, _) = await StartSampleAsync(admin, code);
        using var accountant = await factory.LoginAsync("ketoankho");

        // USB token is requested but the initial is stamped internally, without asking for a certificate.
        var initial = await SignAsync(accountant, await MyStepAsync(accountant, docId), "USB_TOKEN");
        initial.Status.ShouldBe(SigningRequestStatus.Completed);
        initial.Provider.ShouldBe(SigningDefaults.InitialStampProvider);

        using var chief = await factory.LoginAsync("ketoantruong");
        using var approve = await chief.PostAsJsonAsync($"/api/workflows/steps/{await MyStepAsync(chief, docId)}/approve",
            new ApproveStepRequest("Đồng ý", false), JsonDefaults.Options);
        (await approve.Content.ReadFromJsonAsync<WorkflowDto>(JsonDefaults.Options))!.Status.ShouldBe(WorkflowStatus.Completed);
        (await GetWorkflowAsync(admin, docId)).Signatures.Single().Kind.ShouldBe(SignatureKind.Initial);
    }

    [Fact]
    public async Task Document_is_locked_while_in_workflow()
    {
        using var admin = await factory.LoginAsync("admin");
        var (docId, _) = await StartSampleAsync(admin);

        using var edit = await admin.PostAsJsonAsync($"/api/documents/{docId}/edit-sessions", new StartEditRequest(EditorMode.Edit), JsonDefaults.Options);
        edit.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await edit.Content.ReadAsStringAsync()).ShouldContain("DOCUMENT_LOCKED_IN_WORKFLOW");
    }

    private static async Task<(Guid DocumentId, Guid WorkflowId)> StartSampleAsync(HttpClient admin, string workflowCode = "WF_SAMPLE")
    {
        var templates = await admin.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates", JsonDefaults.Options);
        using var created = await admin.PostAsJsonAsync("/api/documents/samples",
            new CreateSampleDocumentRequest(templates!.Single(t => t.Code == "PHIEU_NHAP_KHO").Id, null));
        var docId = await created.Content.ReadFromJsonAsync<Guid>();

        var suppliers = await admin.GetFromJsonAsync<List<LookupItem>>("/api/lookups/suppliers");
        var workflowTemplates = await admin.GetFromJsonAsync<List<WorkflowTemplateDto>>("/api/workflow-templates", JsonDefaults.Options);
        using var start = await admin.PostAsJsonAsync("/api/workflows", new StartWorkflowRequest(
            docId, workflowTemplates!.Single(t => t.Code == workflowCode).Id, suppliers!.Single(s => s.Code == "0101234567").Id, null, null), JsonDefaults.Options);
        start.StatusCode.ShouldBe(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
        return (docId, await start.Content.ReadFromJsonAsync<Guid>());
    }

    private static async Task<WorkflowDto> GetWorkflowAsync(HttpClient client, Guid docId) =>
        (await client.GetFromJsonAsync<WorkflowDto>($"/api/workflows/by-document/{docId}", JsonDefaults.Options))!;

    private static async Task<Guid> MyStepAsync(HttpClient client, Guid docId)
    {
        var inbox = await client.GetFromJsonAsync<PagedResult<InboxItemDto>>("/api/signing/inbox?tab=Pending&pageSize=100", JsonDefaults.Options);
        var item = inbox!.Items.SingleOrDefault(i => i.DocumentId == docId);
        item.ShouldNotBeNull("document missing from the signer's inbox");
        var wf = await GetWorkflowAsync(client, docId);
        wf.MyStepId.ShouldBe(item.StepId);
        return item.StepId!.Value;
    }

    private static async Task<SigningRequestDto> SignAsync(HttpClient client, Guid stepId, string provider = "MOCK", string? certificate = null)
    {
        using var response = await client.PostAsJsonAsync($"/api/signing/steps/{stepId}/sign", new BeginSignRequest(provider, certificate, null), JsonDefaults.Options);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SigningRequestDto>(JsonDefaults.Options))!;
    }
}
