using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Auth;
using BlueContract.Application.Documents;
using BlueContract.Application.Signing;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Workflows;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Signing;

/// <summary>
/// AUDIT-1 §11.7 / spec §8: when a time-stamp authority is configured, every signature carries an RFC 3161 timestamp;
/// if the TSA cannot be reached the signature is refused with a clear message instead of silently going without one.
/// </summary>
[Collection(ApiCollection.Name)]
public class TimestampTests(ApiFactory factory)
{
    [Fact]
    public async Task Signatures_carry_a_timestamp_from_the_configured_authority()
    {
        using var admin = await factory.LoginAsync("admin");
        var before = FakeTimestampAuthority.Requests;
        var (docId, stepId) = await StartAsync(admin);

        using (var sign = await admin.PostAsJsonAsync($"/api/signing/steps/{stepId}/sign", new BeginSignRequest("MOCK", null, null), JsonDefaults.Options))
        {
            sign.StatusCode.ShouldBe(HttpStatusCode.OK, await sign.Content.ReadAsStringAsync());
        }

        FakeTimestampAuthority.Requests.ShouldBeGreaterThan(before);
        var signature = (await VerifyAsync(admin, docId)).ShouldHaveSingleItem();
        signature.HasTimestamp.ShouldBeTrue();
        signature.IntegrityValid.ShouldBeTrue();
    }

    [Fact]
    public async Task An_unreachable_authority_refuses_the_signature()
    {
        // Same database (already seeded: seeding signs documents, which this host cannot do), TSA down.
        await using var offline = factory.WithWebHostBuilder(b => b
            .UseSetting("Seed:Demo", "false")
            .ConfigureTestServices(s => s
                .AddHttpClient("tsa")
                .ConfigurePrimaryHttpMessageHandler(() => new FakeTimestampAuthority { Failing = true })));
        using var admin = await LoginAsync(offline, "admin");
        var (docId, stepId) = await StartAsync(admin);

        using var sign = await admin.PostAsJsonAsync($"/api/signing/steps/{stepId}/sign", new BeginSignRequest("MOCK", null, null), JsonDefaults.Options);
        var body = await sign.Content.ReadAsStringAsync();
        ((int)sign.StatusCode).ShouldBeInRange(400, 499, body);
        body.ShouldContain("SIGNING_TSA_UNAVAILABLE");

        var document = await admin.GetFromJsonAsync<DocumentDto>($"/api/documents/{docId}", JsonDefaults.Options);
        document!.Versions.Single(v => v.IsCurrent).SignatureCount.ShouldBe(0);
    }

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> app, string userName)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, ApiFactory.DemoPassword));
        var result = await response.Content.ReadFromJsonAsync<LoginResult>(JsonDefaults.Options);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result!.AccessToken);
        return client;
    }

    /// <summary>A sample document with a one-step "người lập ký" workflow; returns the document and the step to sign.</summary>
    private static async Task<(Guid DocumentId, Guid StepId)> StartAsync(HttpClient admin)
    {
        var templates = await admin.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates", JsonDefaults.Options);
        using var created = await admin.PostAsJsonAsync("/api/documents/samples",
            new CreateSampleDocumentRequest(templates!.Single(t => t.Code == "PHIEU_NHAP_KHO").Id, null));
        var docId = await created.Content.ReadFromJsonAsync<Guid>();
        var document = await admin.GetFromJsonAsync<DocumentDto>($"/api/documents/{docId}", JsonDefaults.Options);
        var request = new SaveWorkflowTemplateRequest("WF_TSA_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "Ký thử dấu thời gian", document!.Type, null, false, true,
            [new SaveWorkflowStepRequest("SIGN", "Người lập ký", WorkflowActionType.Sign, AssigneeType.Creator, null, null, false, false, [0], null, 24)]);
        using var template = await admin.PostAsJsonAsync("/api/workflow-templates", request, JsonDefaults.Options);
        template.StatusCode.ShouldBe(HttpStatusCode.OK, await template.Content.ReadAsStringAsync());
        var templateId = await template.Content.ReadFromJsonAsync<Guid>();
        try
        {
            using var start = await admin.PostAsJsonAsync("/api/workflows", new StartWorkflowRequest(docId, templateId, null, null, null), JsonDefaults.Options);
            start.StatusCode.ShouldBe(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
            var wf = await admin.GetFromJsonAsync<WorkflowDto>($"/api/workflows/by-document/{docId}", JsonDefaults.Options);
            return (docId, wf!.MyStepId!.Value);
        }
        finally
        {
            (await admin.PutAsJsonAsync($"/api/workflow-templates/{templateId}", request with { IsActive = false }, JsonDefaults.Options)).Dispose();
        }
    }

    private static async Task<IReadOnlyList<PdfSignatureInfo>> VerifyAsync(HttpClient client, Guid docId)
    {
        var document = await client.GetFromJsonAsync<DocumentDto>($"/api/documents/{docId}", JsonDefaults.Options);
        var current = document!.Versions.Single(v => v.IsCurrent);
        var verified = await client.GetFromJsonAsync<VerifyResultDto>($"/api/signing/documents/{docId}/versions/{current.Id}/verify", JsonDefaults.Options);
        return verified!.Signatures;
    }
}
