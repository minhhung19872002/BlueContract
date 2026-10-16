using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Signing;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Workflows;
using iText.Forms;
using iText.Kernel.Pdf;
using iText.Signatures;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Signing;

/// <summary>
/// AUDIT-1 §11.5 / spec §8: the visible signature carries the signer's handwritten signature (or initials for "ký nháy")
/// and the organisation seal for "đóng dấu điện tử", uploaded by the user / the administrator.
/// </summary>
[Collection(ApiCollection.Name)]
public class SignatureImageTests(ApiFactory factory)
{
    // 1×1 PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Signatures_and_seals_show_the_uploaded_images()
    {
        using var admin = await factory.LoginAsync("admin");

        // A document signed before any image is uploaded has a text-only appearance.
        var plain = await SignOwnDocumentAsync(admin, WorkflowActionType.Sign);
        ImagesInSignatureAppearances(plain).ShouldBe(0);

        await UploadAsync(admin, "/api/account/signature-images/signature");
        await UploadAsync(admin, "/api/account/signature-images/initial");
        (await admin.GetAsync(new Uri("/api/account/signature-images/signature", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        ImagesInSignatureAppearances(await SignOwnDocumentAsync(admin, WorkflowActionType.Sign)).ShouldBe(1);
        ImagesInSignatureAppearances(await SignOwnDocumentAsync(admin, WorkflowActionType.Initial)).ShouldBe(1);

        await UploadAsync(admin, "/api/admin/organization/seal");
        using var clerk = await factory.LoginAsync("vanthu");
        ImagesInSignatureAppearances(await SignOwnDocumentAsync(admin, WorkflowActionType.OrgSeal, clerk)).ShouldBe(1);

        // Removing the image goes back to the text appearance.
        (await admin.DeleteAsync(new Uri("/api/account/signature-images/signature", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.DeleteAsync(new Uri("/api/admin/organization/seal", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        ImagesInSignatureAppearances(await SignOwnDocumentAsync(admin, WorkflowActionType.Sign)).ShouldBe(0);
        (await admin.DeleteAsync(new Uri("/api/account/signature-images/initial", UriKind.Relative))).Dispose();
    }

    [Fact]
    public async Task Only_png_or_jpeg_images_are_accepted_and_only_admins_change_the_seal()
    {
        using var buyer = await factory.LoginAsync("muasam");
        using var text = new MultipartFormDataContent();
        var file = new ByteArrayContent("not an image"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        text.Add(file, "file", "chu-ky.png");
        using var refused = await buyer.PostAsync(new Uri("/api/account/signature-images/signature", UriKind.Relative), text);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("SIGNATURE_IMAGE_INVALID");

        using var seal = await buyer.PostAsync(new Uri("/api/admin/organization/seal", UriKind.Relative), Form());
        seal.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static MultipartFormDataContent Form()
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "anh.png");
        return form;
    }

    private static async Task UploadAsync(HttpClient client, string path)
    {
        using var form = Form();
        using var response = await client.PostAsync(new Uri(path, UriKind.Relative), form);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Starts a one-step workflow on a sample document — assigned to the creator, or to the clerks' role when
    /// <paramref name="signer"/> is given (the seal is theirs) — signs it and returns the PDF.
    /// </summary>
    private static async Task<byte[]> SignOwnDocumentAsync(HttpClient admin, WorkflowActionType action, HttpClient? signer = null)
    {
        var templates = await admin.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates", JsonDefaults.Options);
        using var created = await admin.PostAsJsonAsync("/api/documents/samples",
            new CreateSampleDocumentRequest(templates!.Single(t => t.Code == "PHIEU_NHAP_KHO").Id, null));
        var docId = await created.Content.ReadFromJsonAsync<Guid>();
        var document = await admin.GetFromJsonAsync<DocumentDto>($"/api/documents/{docId}", JsonDefaults.Options);

        var code = "WF_IMG_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var request = new SaveWorkflowTemplateRequest(
            code, "Ký thử ảnh chữ ký", document!.Type, null, false, true,
            [signer is null
                ? new SaveWorkflowStepRequest("SIGN", "Người lập ký", action, AssigneeType.Creator, null, null, false, false, [0], null, 24)
                : new SaveWorkflowStepRequest("SIGN", "Văn thư đóng dấu", action, AssigneeType.Role, RoleCatalog.Clerk, null, false, false, [0], null, 24)]);
        using var template = await admin.PostAsJsonAsync("/api/workflow-templates", request, JsonDefaults.Options);
        template.StatusCode.ShouldBe(HttpStatusCode.OK, await template.Content.ReadAsStringAsync());
        var templateId = await template.Content.ReadFromJsonAsync<Guid>();
        try
        {
            using var start = await admin.PostAsJsonAsync("/api/workflows", new StartWorkflowRequest(docId, templateId, null, null, null), JsonDefaults.Options);
            start.StatusCode.ShouldBe(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
            var actor = signer ?? admin;
            var wf = await actor.GetFromJsonAsync<WorkflowDto>($"/api/workflows/by-document/{docId}", JsonDefaults.Options);
            wf!.MyStepId.ShouldNotBeNull();
            using var sign = await actor.PostAsJsonAsync($"/api/signing/steps/{wf!.MyStepId}/sign", new BeginSignRequest("MOCK", null, null), JsonDefaults.Options);
            sign.StatusCode.ShouldBe(HttpStatusCode.OK, await sign.Content.ReadAsStringAsync());
            (await sign.Content.ReadFromJsonAsync<SigningRequestDto>(JsonDefaults.Options))!.Status.ShouldBe(SigningRequestStatus.Completed);
        }
        finally
        {
            // Other tests expect the seeded workflows only.
            (await admin.PutAsJsonAsync($"/api/workflow-templates/{templateId}", request with { IsActive = false }, JsonDefaults.Options)).Dispose();
        }

        var signed = await admin.GetFromJsonAsync<DocumentDto>($"/api/documents/{docId}", JsonDefaults.Options);
        var current = signed!.Versions.Single(v => v.IsCurrent);
        return await admin.GetByteArrayAsync(new Uri($"/api/documents/{docId}/versions/{current.Id}/file?format=pdf", UriKind.Relative));
    }

    /// <summary>Counts the signature fields whose appearance draws an image (searched through nested form XObjects).</summary>
    private static int ImagesInSignatureAppearances(byte[] pdf)
    {
        using var document = new PdfDocument(new PdfReader(new MemoryStream(pdf)));
        var form = PdfAcroForm.GetAcroForm(document, false);
        var names = new SignatureUtil(document).GetSignatureNames();
        names.Count.ShouldBeGreaterThan(0);
        return names.Count(name => form!.GetField(name).GetWidgets().Any(w => HasImage(w.GetNormalAppearanceObject(), 0)));
    }

    private static bool HasImage(PdfDictionary? stream, int depth)
    {
        if (stream is null || depth > 6)
        {
            return false;
        }

        var objects = stream.GetAsDictionary(PdfName.Resources)?.GetAsDictionary(PdfName.XObject);
        if (objects is null)
        {
            return false;
        }

        foreach (var key in objects.KeySet())
        {
            var child = objects.GetAsStream(key);
            if (child?.GetAsName(PdfName.Subtype)?.Equals(PdfName.Image) == true || HasImage(child, depth + 1))
            {
                return true;
            }
        }

        return false;
    }
}
