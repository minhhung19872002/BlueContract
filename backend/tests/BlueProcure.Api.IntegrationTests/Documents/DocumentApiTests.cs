using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BlueProcure.Api.IntegrationTests.Infrastructure;
using BlueProcure.Application.Documents;
using BlueProcure.Domain.Documents;
using BlueProcure.Infrastructure.Documents.Templates;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shouldly;

namespace BlueProcure.Api.IntegrationTests.Documents;

[Collection(ApiCollection.Name)]
public class DocumentApiTests(ApiFactory factory)
{
    private static readonly string DocxType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    [Fact]
    public async Task Built_in_templates_are_installed()
    {
        using var client = await factory.LoginAsync("admin");

        var list = await client.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates", JsonDefaults.Options);

        list!.Where(t => t.IsBuiltIn).Select(t => t.Code).ShouldBeSubsetOf(BuiltInTemplates.All.Select(t => t.Code));
        list!.Count(t => t.IsBuiltIn).ShouldBe(BuiltInTemplates.All.Count);
        list.ShouldAllBe(t => t.ActiveVersionNumber >= 1);
    }

    [Fact]
    public async Task Validate_endpoint_lists_invalid_placeholders()
    {
        using var client = await factory.LoginAsync("admin");
        var bytes = new DocxBuilder().P("Số {{Contract.Number}} — {{Contract.Bogus}} — {{Contract.TotalAmount | tien}}").Build();

        using var response = await client.PostAsync(new Uri("/api/templates/validate", UriKind.Relative), Upload(bytes, ("kind", "Contract")));
        var result = await response.Content.ReadFromJsonAsync<TemplateValidationResult>(JsonDefaults.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        result!.IsValid.ShouldBeFalse();
        result.Errors.Select(e => e.Tag).ShouldBe(["Contract.Bogus", "Contract.TotalAmount | tien"]);
    }

    [Fact]
    public async Task Non_docx_upload_is_rejected_by_magic_bytes()
    {
        using var client = await factory.LoginAsync("admin");

        using var response = await client.PostAsync(new Uri("/api/templates/validate", UriKind.Relative),
            Upload("%PDF-1.7 fake"u8.ToArray(), ("kind", "Contract")));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("TEMPLATE_NOT_DOCX");
    }

    [Fact]
    public async Task Upload_template_new_version_and_preview_as_pdf()
    {
        using var client = await factory.LoginAsync("admin");
        var v1 = new DocxBuilder().Title("PHỤ LỤC THỬ {{Appendix.Number}}").P("Giá trị mới: {{Appendix.NewTotalAmount | words}}").Build();

        using var create = await client.PostAsync(new Uri("/api/templates", UriKind.Relative),
            Upload(v1, ("code", "PL_THU_" + Guid.NewGuid().ToString("N")[..6]), ("name", "Phụ lục thử"), ("kind", "Appendix")));
        create.StatusCode.ShouldBe(HttpStatusCode.OK);
        var id = await create.Content.ReadFromJsonAsync<Guid>();

        var v2 = new DocxBuilder().Title("PHỤ LỤC THỬ {{Appendix.Number}} (bản 2)").Build();
        using var upload = await client.PostAsync(new Uri($"/api/templates/{id}/versions", UriKind.Relative), Upload(v2, ("note", "Sửa tiêu đề")));
        (await upload.Content.ReadFromJsonAsync<int>()).ShouldBe(2);

        var detail = await client.GetFromJsonAsync<TemplateDetailDto>($"/api/templates/{id}", JsonDefaults.Options);
        detail!.ActiveVersionNumber.ShouldBe(2);
        detail.Versions.Count.ShouldBe(2);

        using var preview = await client.GetAsync(new Uri($"/api/templates/{id}/preview?version=1&format=pdf", UriKind.Relative));
        preview.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var pdf = await preview.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
    }

    [Fact]
    public async Task Sample_document_versions_pdf_and_downloads_are_hashed_and_audited()
    {
        using var client = await factory.LoginAsync("admin");
        var docId = await CreateSampleDocumentAsync(client, "HD_THUOC");

        var document = await client.GetFromJsonAsync<DocumentDto>($"/api/documents/{docId}", JsonDefaults.Options);
        var v1 = document!.Versions.Single();
        v1.VersionNumber.ShouldBe(1);
        v1.HasDocx.ShouldBeTrue();
        v1.DocxSha256!.Length.ShouldBe(64);

        using var render = await client.PostAsync(new Uri($"/api/documents/{docId}/versions/{v1.Id}/pdf", UriKind.Relative), null);
        var rendered = await render.Content.ReadFromJsonAsync<DocumentVersionDto>(JsonDefaults.Options);
        rendered!.HasPdf.ShouldBeTrue();
        rendered.PdfSha256!.Length.ShouldBe(64);

        using var file = await client.GetAsync(new Uri($"/api/documents/{docId}/versions/{v1.Id}/file?format=pdf", UriKind.Relative));
        var bytes = await file.Content.ReadAsByteArrayAsync();
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)).ShouldBe(rendered.PdfSha256);

        var link = await client.GetFromJsonAsync<FileLinkDto>($"/api/documents/{docId}/versions/{v1.Id}/link?format=docx", JsonDefaults.Options);
        using var http = new HttpClient();
        (await http.GetByteArrayAsync(link!.Url)).Length.ShouldBe((int)v1.DocxSize!.Value);
    }

    [Fact]
    public async Task Users_without_permission_cannot_read_sample_documents()
    {
        using var admin = await factory.LoginAsync("admin");
        var docId = await CreateSampleDocumentAsync(admin, "PHIEU_NHAP_KHO");
        using var supplier = await factory.LoginAsync("ncc1");

        (await supplier.GetAsync(new Uri($"/api/documents/{docId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Onlyoffice_session_download_and_signed_callback_create_new_version()
    {
        using var client = await factory.LoginAsync("admin");
        var docId = await CreateSampleDocumentAsync(client, "PHU_LUC");

        using var start = await client.PostAsJsonAsync($"/api/documents/{docId}/edit-sessions", new StartEditRequest(EditorMode.Review), JsonDefaults.Options);
        start.StatusCode.ShouldBe(HttpStatusCode.OK);
        var session = await start.Content.ReadFromJsonAsync<EditorSessionDto>(JsonDefaults.Options);
        var config = session!.Config;
        config["document"]!["permissions"]!["edit"]!.GetValue<bool>().ShouldBeFalse();
        config["editorConfig"]!["customization"]!["review"]!["trackChanges"]!.GetValue<bool>().ShouldBeTrue();
        config["token"]!.GetValue<string>().ShouldNotBeNullOrEmpty();

        // The document server downloads the file anonymously with the signed URL…
        var downloadUrl = new Uri(config["document"]!["url"]!.GetValue<string>());
        using var anonymous = factory.CreateClient();
        using var fileResponse = await anonymous.GetAsync(new Uri(downloadUrl.PathAndQuery, UriKind.Relative));
        fileResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // …a forged token is refused.
        using var forged = await anonymous.GetAsync(new Uri(downloadUrl.AbsolutePath + "?token=forged", UriKind.Relative));
        forged.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Save callback (status 2) signed with the shared JWT secret; "url" points to the edited file.
        var key = config["document"]!["key"]!.GetValue<string>();
        var callbackUrl = new Uri(config["editorConfig"]!["callbackUrl"]!.GetValue<string>());
        var token = SignCallback(new Dictionary<string, object> { ["key"] = key, ["status"] = 2, ["url"] = downloadUrl.ToString(), ["users"] = new[] { "u1" } });
        using var unsigned = await anonymous.PostAsync(new Uri(callbackUrl.PathAndQuery, UriKind.Relative),
            new StringContent(JsonSerializer.Serialize(new { key, status = 2, url = downloadUrl.ToString() }), Encoding.UTF8, "application/json"));
        (await unsigned.Content.ReadAsStringAsync()).ShouldContain("\"error\":1");

        using var callback = await anonymous.PostAsync(new Uri(callbackUrl.PathAndQuery, UriKind.Relative),
            new StringContent(JsonSerializer.Serialize(new { token }), Encoding.UTF8, "application/json"));
        (await callback.Content.ReadAsStringAsync()).ShouldContain("\"error\":0");

        var document = await client.GetFromJsonAsync<DocumentDto>($"/api/documents/{docId}", JsonDefaults.Options);
        document!.Versions.Count.ShouldBe(2);
        document.Versions[0].Reason.ShouldBe("Rà soát trực tuyến (có theo dõi thay đổi)");
        document.Versions[0].IsCurrent.ShouldBeTrue();
        document.Versions[0].CreatedByName.ShouldBe("Quản trị hệ thống");

        // A replayed callback for the closed session adds nothing.
        using var replay = await anonymous.PostAsync(new Uri(callbackUrl.PathAndQuery, UriKind.Relative),
            new StringContent(JsonSerializer.Serialize(new { token }), Encoding.UTF8, "application/json"));
        (await client.GetFromJsonAsync<DocumentDto>($"/api/documents/{docId}", JsonDefaults.Options))!.Versions.Count.ShouldBe(2);
    }

    private static async Task<Guid> CreateSampleDocumentAsync(HttpClient client, string templateCode)
    {
        var templates = await client.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates", JsonDefaults.Options);
        var template = templates!.Single(t => t.Code == templateCode);
        using var response = await client.PostAsJsonAsync("/api/documents/samples", new CreateSampleDocumentRequest(template.Id, null));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    private static MultipartFormDataContent Upload(byte[] content, params (string Name, string Value)[] fields)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(DocxType);
        form.Add(file, "file", "mau.docx");
        foreach (var (name, value) in fields)
        {
            form.Add(new StringContent(value), name);
        }

        return form;
    }

    private static string SignCallback(Dictionary<string, object> payload) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = payload,
            Subject = new ClaimsIdentity(),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.OnlyOfficeSecret)), SecurityAlgorithms.HmacSha256),
        });
}
