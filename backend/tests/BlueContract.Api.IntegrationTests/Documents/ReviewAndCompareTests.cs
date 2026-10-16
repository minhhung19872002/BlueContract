using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Admin;
using BlueContract.Application.Catalog;
using BlueContract.Application.Common;
using BlueContract.Application.Contracts;
using BlueContract.Application.Documents;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Contracts;
using BlueContract.Infrastructure.Documents.Templates;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Documents;

/// <summary>
/// AUDIT-1 §11.1 / spec §9.1-6: the "Rà soát" menu lists the documents waiting at a review step, and two versions of a
/// document can be compared (paragraphs added / removed).
/// </summary>
[Collection(ApiCollection.Name)]
public class ReviewAndCompareTests(ApiFactory factory)
{
    private const string DocxType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    [Fact]
    public async Task The_inbox_can_be_limited_to_review_steps()
    {
        using var buyer = await factory.LoginAsync("muasam");
        var documentId = await SubmitContractAsync(buyer);

        using var reviewer = await factory.LoginAsync("rasoat");
        var review = await reviewer.GetFromJsonAsync<PagedResult<InboxItemDto>>(
            "/api/signing/inbox?tab=Pending&actionType=Review&pageSize=200", JsonDefaults.Options);
        review!.Items.ShouldContain(i => i.DocumentId == documentId);
        review.Items.ShouldAllBe(i => i.ActionType == Domain.Workflows.WorkflowActionType.Review);

        var signing = await reviewer.GetFromJsonAsync<PagedResult<InboxItemDto>>(
            "/api/signing/inbox?tab=Pending&actionType=Sign&pageSize=200", JsonDefaults.Options);
        signing!.Items.ShouldNotContain(i => i.DocumentId == documentId);
    }

    [Fact]
    public async Task Two_versions_of_a_document_are_compared_paragraph_by_paragraph()
    {
        using var client = await factory.LoginAsync("admin");
        var templates = await client.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates", JsonDefaults.Options);
        using var sample = await client.PostAsJsonAsync("/api/documents/samples",
            new CreateSampleDocumentRequest(templates!.Single(t => t.Code == "HD_THUOC").Id, null));
        var docId = await sample.Content.ReadFromJsonAsync<Guid>();

        await UploadAsync(client, docId, new DocxBuilder().P("Điều 1. Hàng hóa").P("Giao hàng trong 05 ngày.").P("Bảo hành 12 tháng.").Build());
        await UploadAsync(client, docId, new DocxBuilder().P("Điều 1. Hàng hóa").P("Giao hàng trong 03 ngày.").P("Bảo hành 12 tháng.").P("Phạt chậm giao 0,5%/ngày.").Build());
        var versions = (await client.GetFromJsonAsync<DocumentDto>($"/api/documents/{docId}", JsonDefaults.Options))!.Versions;
        var older = versions.Single(v => v.VersionNumber == versions.Max(x => x.VersionNumber) - 1);
        var newer = versions.Single(v => v.IsCurrent);

        using var response = await client.GetAsync(new Uri($"/api/documents/{docId}/compare?from={older.Id}&to={newer.Id}", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var diff = (await response.Content.ReadFromJsonAsync<CompareView>(JsonDefaults.Options))!;
        diff.FromVersion.ShouldBe(older.VersionNumber);
        diff.ToVersion.ShouldBe(newer.VersionNumber);
        diff.Removed.ShouldBe(1);
        diff.Added.ShouldBe(2);
        diff.Lines.Select(l => (l.Kind, l.Text)).ShouldBe(
        [
            ("Same", "Điều 1. Hàng hóa"),
            ("Removed", "Giao hàng trong 05 ngày."),
            ("Added", "Giao hàng trong 03 ngày."),
            ("Same", "Bảo hành 12 tháng."),
            ("Added", "Phạt chậm giao 0,5%/ngày."),
        ]);

        // A user who cannot see the document cannot compare it either.
        using var supplier = await factory.LoginAsync("ncc2");
        using var hidden = await supplier.GetAsync(new Uri($"/api/documents/{docId}/compare?from={older.Id}&to={newer.Id}", UriKind.Relative));
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Read as plain JSON so the test states the wire format (enum names) the frontend relies on.
    private sealed record CompareView(int FromVersion, int ToVersion, int Added, int Removed, List<LineView> Lines);

    private sealed record LineView(string Kind, string Text);

    private static async Task UploadAsync(HttpClient client, Guid docId, byte[] docx)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(docx);
        file.Headers.ContentType = new MediaTypeHeaderValue(DocxType);
        form.Add(file, "file", "ban-moi.docx");
        form.Add(new StringContent("Sửa điều khoản giao hàng"), "reason");
        using var upload = await client.PostAsync(new Uri($"/api/documents/{docId}/versions", UriKind.Relative), form);
        upload.StatusCode.ShouldBe(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> SubmitContractAsync(HttpClient buyer)
    {
        var categories = await buyer.GetFromJsonAsync<List<ItemCategoryDto>>("/api/items/categories", JsonDefaults.Options);
        var templates = await buyer.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates?kind=Contract", JsonDefaults.Options);
        var suppliers = await buyer.GetFromJsonAsync<List<LookupItem>>("/api/lookups/suppliers?search=Hồng Lam", JsonDefaults.Options);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        using var created = await buyer.PostAsJsonAsync("/api/contracts", new SaveContractRequest(
            ProcurementType.NonBidding, null, suppliers!.First().Id, null, $"RS-{tag}/2026/HĐ", "Bảo trì thiết bị", ContractKind.LumpSum, true,
            categories!.Single(c => c.Code == "SUA_CHUA").Id, null, null, today, today, today.AddMonths(3), "30 ngày", null, null, null, null, null, null, null,
            templates!.Single(t => t.Code == "HD_DICHVU_KHONGTHAU").Id,
            [new ContractLineRequest(null, new NewItemRequest($"RS-{tag}", "Bảo trì tháng", "Lần", null, null, null, null, null), 1, 1_000_000, 8)]), JsonDefaults.Options);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var id = await created.Content.ReadFromJsonAsync<Guid>();
        using (var submit = await buyer.PostAsync(new Uri($"/api/contracts/{id}/submit", UriKind.Relative), null))
        {
            submit.StatusCode.ShouldBe(HttpStatusCode.NoContent, await submit.Content.ReadAsStringAsync());
        }

        return (await buyer.GetFromJsonAsync<ContractDto>($"/api/contracts/{id}", JsonDefaults.Options))!.DocumentId!.Value;
    }
}
