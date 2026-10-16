using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Signing;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Workflows;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Signing;

/// <summary>
/// AUDIT-1 §11.6 / spec §8 "Đóng dấu tổ chức": the e-seal is made with the hospital's own certificate installed on the
/// organisation page (PKCS#12 + password), not with a generated one.
/// </summary>
[Collection(ApiCollection.Name)]
public class OrganizationCertificateTests(ApiFactory factory)
{
    private const string Password = "Seal@2026";

    [Fact]
    public async Task The_seal_is_signed_with_the_installed_organization_certificate()
    {
        using var admin = await factory.LoginAsync("admin");
        var (pfx, serial) = CreatePfx("BỆNH VIỆN THỬ NGHIỆM CON DẤU");

        using (var wrong = await admin.PostAsync(new Uri("/api/admin/organization/seal-certificate", UriKind.Relative), Form(pfx, "sai-mat-khau")))
        {
            wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await wrong.Content.ReadAsStringAsync()).ShouldContain("ORG_CERTIFICATE_INVALID");
        }

        using (var installed = await admin.PostAsync(new Uri("/api/admin/organization/seal-certificate", UriKind.Relative), Form(pfx, Password)))
        {
            installed.StatusCode.ShouldBe(HttpStatusCode.OK, await installed.Content.ReadAsStringAsync());
        }

        try
        {
            var info = await admin.GetFromJsonAsync<CertificateView>("/api/admin/organization/seal-certificate", JsonDefaults.Options);
            info!.SerialNumber.ShouldBe(serial);
            info.Subject.ShouldContain("BỆNH VIỆN THỬ NGHIỆM CON DẤU");

            using var clerk = await factory.LoginAsync("vanthu");
            var signatures = await SealAsync(admin, clerk);
            var seal = signatures.ShouldHaveSingleItem();
            // iText prints the serial as a number (no leading zero bytes), .NET as the raw bytes.
            seal.SerialNumber.TrimStart('0').ShouldBe(serial.TrimStart('0'));
            seal.SignerName.ShouldBe("BỆNH VIỆN THỬ NGHIỆM CON DẤU");
            seal.IntegrityValid.ShouldBeTrue();
        }
        finally
        {
            (await admin.DeleteAsync(new Uri("/api/admin/organization/seal-certificate", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await admin.GetAsync(new Uri("/api/admin/organization/seal-certificate", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_administrators_install_the_organization_certificate()
    {
        using var clerk = await factory.LoginAsync("vanthu");
        var (pfx, _) = CreatePfx("BV KHÔNG ĐƯỢC CÀI");
        using var response = await clerk.PostAsync(new Uri("/api/admin/organization/seal-certificate", UriKind.Relative), Form(pfx, Password));
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private sealed record CertificateView(string Subject, string SerialNumber, DateTimeOffset NotAfter);

    private static (byte[] Pfx, string Serial) CreatePfx(string commonName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={commonName}, O={commonName}, C=VN", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        return (cert.Export(X509ContentType.Pfx, Password), cert.SerialNumber);
    }

    private static MultipartFormDataContent Form(byte[] pfx, string password)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(pfx);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/x-pkcs12");
        form.Add(file, "file", "chung-thu-to-chuc.p12");
        form.Add(new StringContent(password), "password");
        return form;
    }

    /// <summary>One "đóng dấu" step for the clerks on a sample document; returns the signatures of the sealed PDF.</summary>
    private static async Task<List<PdfSignatureInfo>> SealAsync(HttpClient admin, HttpClient clerk)
    {
        var templates = await admin.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates", JsonDefaults.Options);
        using var created = await admin.PostAsJsonAsync("/api/documents/samples",
            new CreateSampleDocumentRequest(templates!.Single(t => t.Code == "PHIEU_NHAP_KHO").Id, null));
        var docId = await created.Content.ReadFromJsonAsync<Guid>();
        var document = await admin.GetFromJsonAsync<DocumentDto>($"/api/documents/{docId}", JsonDefaults.Options);
        var request = new SaveWorkflowTemplateRequest("WF_SEAL_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "Đóng dấu thử", document!.Type, null, false, true,
            [new SaveWorkflowStepRequest("SEAL", "Văn thư đóng dấu", WorkflowActionType.OrgSeal, AssigneeType.Role, RoleCatalog.Clerk, null, false, false, [0], null, 24)]);
        using var template = await admin.PostAsJsonAsync("/api/workflow-templates", request, JsonDefaults.Options);
        template.StatusCode.ShouldBe(HttpStatusCode.OK, await template.Content.ReadAsStringAsync());
        var templateId = await template.Content.ReadFromJsonAsync<Guid>();
        try
        {
            using var start = await admin.PostAsJsonAsync("/api/workflows", new StartWorkflowRequest(docId, templateId, null, null, null), JsonDefaults.Options);
            start.StatusCode.ShouldBe(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
            var wf = await clerk.GetFromJsonAsync<WorkflowDto>($"/api/workflows/by-document/{docId}", JsonDefaults.Options);
            // The clerk picks the usual provider: the seal still uses the organisation certificate.
            using var sign = await clerk.PostAsJsonAsync($"/api/signing/steps/{wf!.MyStepId}/sign", new BeginSignRequest("MOCK", null, null), JsonDefaults.Options);
            sign.StatusCode.ShouldBe(HttpStatusCode.OK, await sign.Content.ReadAsStringAsync());
        }
        finally
        {
            (await admin.PutAsJsonAsync($"/api/workflow-templates/{templateId}", request with { IsActive = false }, JsonDefaults.Options)).Dispose();
        }

        var sealedDoc = await admin.GetFromJsonAsync<DocumentDto>($"/api/documents/{docId}", JsonDefaults.Options);
        var current = sealedDoc!.Versions.Single(v => v.IsCurrent);
        var verified = await admin.GetFromJsonAsync<VerifyResultDto>($"/api/signing/documents/{docId}/versions/{current.Id}/verify", JsonDefaults.Options);
        return [.. verified!.Signatures];
    }
}
