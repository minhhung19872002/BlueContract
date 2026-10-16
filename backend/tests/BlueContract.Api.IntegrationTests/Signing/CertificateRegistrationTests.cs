using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Admin;
using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Signing;
using BlueContract.Application.Workflows;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Signing;

/// <summary>
/// AUDIT-1 §11.12 / spec §9.2-7 "Tài khoản công ty: đăng ký chứng thư số": a user (supplier staff included) registers the
/// certificate they sign with; a supplier certificate must carry the company's tax code; signing with a USB token then
/// only accepts the registered certificate.
/// </summary>
[Collection(ApiCollection.Name)]
public class CertificateRegistrationTests(ApiFactory factory)
{
    private const string Url = "/api/account/certificate";

    [Fact]
    public async Task Supplier_staff_register_a_certificate_with_the_company_tax_code()
    {
        using var supplier = await factory.LoginAsync("ncc1");
        try
        {
            (await supplier.GetFromJsonAsync<JsonElement>(Url)).GetProperty("serial").ValueKind.ShouldBe(JsonValueKind.Null);

            using (var garbage = await UploadAsync(supplier, "not a certificate"u8.ToArray(), "a.cer"))
            {
                garbage.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
                (await Code(garbage)).ShouldBe("ACCOUNT_CERTIFICATE_INVALID");
            }

            using var otherCompany = Create("CN=Nguyễn Văn B, O=Công ty khác, OID.0.9.2342.19200300.100.1.1=MST:0309999999", DateTimeOffset.UtcNow.AddYears(1));
            using (var mismatch = await UploadAsync(supplier, otherCompany.RawData, "khac.cer"))
            {
                (await Code(mismatch)).ShouldBe("ACCOUNT_CERTIFICATE_TAX_CODE_MISMATCH");
            }

            using var expired = Create("CN=Nguyễn Văn B, OID.0.9.2342.19200300.100.1.1=MST:0101234567", DateTimeOffset.UtcNow.AddDays(-1));
            using (var old = await UploadAsync(supplier, expired.RawData, "cu.cer"))
            {
                (await Code(old)).ShouldBe("ACCOUNT_CERTIFICATE_EXPIRED");
            }

            using var valid = Create("CN=Nguyễn Văn B, O=Công ty Minh An, OID.0.9.2342.19200300.100.1.1=MST:0101234567", DateTimeOffset.UtcNow.AddMonths(6));
            var pem = System.Text.Encoding.ASCII.GetBytes(valid.ExportCertificatePem());
            using (var ok = await UploadAsync(supplier, pem, "minhan.pem"))
            {
                ok.StatusCode.ShouldBe(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
            }

            var registered = await supplier.GetFromJsonAsync<JsonElement>(Url);
            registered.GetProperty("serial").GetString().ShouldBe(valid.SerialNumber);
            registered.GetProperty("subject").GetString()!.ShouldContain("Minh An");
            registered.GetProperty("issuer").GetString().ShouldNotBeNullOrWhiteSpace();
            registered.GetProperty("validTo").GetDateTimeOffset().ShouldBe(valid.NotAfter.ToUniversalTime(), TimeSpan.FromSeconds(1));

            // The company admin sees which staff have registered a certificate.
            var staff = await supplier.GetFromJsonAsync<JsonElement>("/api/supplier/company/staff");
            var me = staff.EnumerateArray().Single(s => s.GetProperty("userName").GetString() == "ncc1");
            me.GetProperty("certificateSerial").GetString().ShouldBe(valid.SerialNumber);
            me.GetProperty("certificateValidTo").ValueKind.ShouldBe(JsonValueKind.String);
        }
        finally
        {
            using var removed = await supplier.DeleteAsync(new Uri(Url, UriKind.Relative));
            removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await supplier.GetFromJsonAsync<JsonElement>(Url)).GetProperty("serial").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Usb_token_signing_accepts_only_the_registered_certificate()
    {
        using var admin = await factory.LoginAsync("admin");
        var docId = await StartSampleAsync(admin);
        using var storekeeper = await factory.LoginAsync("thukho");
        var stepId = await MyStepAsync(storekeeper, docId);

        using var rsa = RSA.Create(2048);
        using var registered = new CertificateRequest("CN=Thủ kho, O=Bệnh viện", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        using var another = Create("CN=Thủ kho (token khác), O=Bệnh viện", DateTimeOffset.UtcNow.AddYears(1));
        try
        {
            using (var upload = await UploadAsync(storekeeper, registered.RawData, "thukho.cer"))
            {
                upload.StatusCode.ShouldBe(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
            }

            using (var wrong = await storekeeper.PostAsJsonAsync($"/api/signing/steps/{stepId}/sign",
                new BeginSignRequest("USB_TOKEN", Convert.ToBase64String(another.RawData), null), JsonDefaults.Options))
            {
                wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
                (await Code(wrong)).ShouldBe("SIGNING_CERTIFICATE_NOT_REGISTERED");
            }

            using var right = await storekeeper.PostAsJsonAsync($"/api/signing/steps/{stepId}/sign",
                new BeginSignRequest("USB_TOKEN", Convert.ToBase64String(registered.RawData), null), JsonDefaults.Options);
            right.StatusCode.ShouldBe(HttpStatusCode.OK, await right.Content.ReadAsStringAsync());
        }
        finally
        {
            using var _ = await storekeeper.DeleteAsync(new Uri(Url, UriKind.Relative));
        }
    }

    private static X509Certificate2 Create(string subject, DateTimeOffset notAfter)
    {
        using var key = RSA.Create(2048);
        return new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(notAfter.AddYears(-2), notAfter);
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] content, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pkix-cert");
        form.Add(file, "file", fileName);
        return await client.PostAsync(new Uri(Url, UriKind.Relative), form);
    }

    private static async Task<string?> Code(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static async Task<Guid> StartSampleAsync(HttpClient admin)
    {
        var templates = await admin.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates", JsonDefaults.Options);
        using var created = await admin.PostAsJsonAsync("/api/documents/samples",
            new CreateSampleDocumentRequest(templates!.Single(t => t.Code == "PHIEU_NHAP_KHO").Id, null));
        var docId = await created.Content.ReadFromJsonAsync<Guid>();
        var suppliers = await admin.GetFromJsonAsync<List<LookupItem>>("/api/lookups/suppliers");
        var workflowTemplates = await admin.GetFromJsonAsync<List<WorkflowTemplateDto>>("/api/workflow-templates", JsonDefaults.Options);
        using var start = await admin.PostAsJsonAsync("/api/workflows", new StartWorkflowRequest(
            docId, workflowTemplates!.Single(t => t.Code == "WF_SAMPLE").Id, suppliers!.Single(s => s.Code == "0101234567").Id, null, null), JsonDefaults.Options);
        start.StatusCode.ShouldBe(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
        return docId;
    }

    private static async Task<Guid> MyStepAsync(HttpClient client, Guid docId)
    {
        var inbox = await client.GetFromJsonAsync<PagedResult<InboxItemDto>>("/api/signing/inbox?tab=Pending&pageSize=100", JsonDefaults.Options);
        return inbox!.Items.Single(i => i.DocumentId == docId).StepId!.Value;
    }
}
