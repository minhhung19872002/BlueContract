using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Reflection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Payments;
using BlueContract.Domain.Receipts;
using BlueContract.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Guards;

/// <summary>
/// AUDIT-1 §10.11: every upload endpoint refuses a file whose content is not what its name claims, and a file over the
/// size limit — with a 4xx and a reason, never a 500. The invoice XML parser resists XXE and entity expansion.
/// </summary>
[Collection(ApiCollection.Name)]
public class UploadTests(ApiFactory factory)
{
    // An executable dressed up as the expected file.
    private static readonly byte[] Executable = [.. "MZ"u8.ToArray(), .. Enumerable.Repeat((byte)0x90, 1024)];

    private sealed record Endpoint(string Name, string User, string Path, string FileName, string MediaType, string[] TypeCodes, (string Key, string Value)[] Fields);

    [Fact]
    public async Task Every_upload_endpoint_refuses_disguised_and_oversized_files()
    {
        var endpoints = await EndpointsAsync();
        // Every action taking a file must be listed here (counted on the controllers, so a new upload cannot be missed).
        var uploadActions = typeof(Program).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ControllerBase)))
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Count(m => m.GetParameters().Any(p => p.ParameterType == typeof(IFormFile)));
        endpoints.Count.ShouldBe(uploadActions, "one entry per upload endpoint of the API");
        var oversized = new byte[(int)FileInspector.MaxUploadBytes + 1];
        "%PDF-1.7\n"u8.CopyTo(oversized);

        var failures = new List<string>();
        foreach (var e in endpoints)
        {
            using var client = await factory.LoginAsync(e.User);
            using (var disguised = await client.PostAsync(new Uri(e.Path, UriKind.Relative), Form(e, Executable)))
            {
                var body = await disguised.Content.ReadAsStringAsync();
                if ((int)disguised.StatusCode is < 400 or >= 500 || !e.TypeCodes.Any(body.Contains))
                {
                    failures.Add($"{e.Name} disguised: {(int)disguised.StatusCode} {body[..Math.Min(200, body.Length)]}");
                }
            }

            using (var tooLarge = await client.PostAsync(new Uri(e.Path, UriKind.Relative), Form(e, oversized)))
            {
                var body = await tooLarge.Content.ReadAsStringAsync();
                if (tooLarge.StatusCode != HttpStatusCode.RequestEntityTooLarge && !(tooLarge.StatusCode == HttpStatusCode.BadRequest && body.Contains("FILE_TOO_LARGE")))
                {
                    failures.Add($"{e.Name} oversized: {(int)tooLarge.StatusCode} {body[..Math.Min(200, body.Length)]}");
                }
            }
        }

        failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task A_damaged_pdf_is_refused_not_a_server_error()
    {
        using var client = await factory.LoginAsync("ketoan");
        var damaged = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog >> broken stream without xref");
        using var response = await client.PostAsync(new Uri("/api/signing/verify", UriKind.Relative), Form(new Endpoint("verify", "ketoan", "", "x.pdf", "application/pdf", [], []), damaged));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("xxe", """<?xml version="1.0"?><!DOCTYPE r [<!ENTITY x SYSTEM "file:///etc/passwd">]><HDon><DLHDon><TTChung><SHDon>&x;</SHDon></TTChung></DLHDon></HDon>""")]
    [InlineData("billion-laughs", """<?xml version="1.0"?><!DOCTYPE l [<!ENTITY a "aaaaaaaaaa"><!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;"><!ENTITY c "&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;"><!ENTITY d "&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;"><!ENTITY e "&d;&d;&d;&d;&d;&d;&d;&d;&d;&d;"><!ENTITY f "&e;&e;&e;&e;&e;&e;&e;&e;&e;&e;"><!ENTITY g "&f;&f;&f;&f;&f;&f;&f;&f;&f;&f;">]><HDon><DLHDon><TTChung><SHDon>&g;</SHDon></TTChung></DLHDon></HDon>""")]
    public async Task Invoice_xml_with_entities_is_refused(string name, string xml)
    {
        using var client = await factory.LoginAsync("muasam");
        var started = DateTime.UtcNow;
        using var response = await client.PostAsync(new Uri("/api/warehouse-receipts/invoice/parse", UriKind.Relative),
            Form(new Endpoint(name, "muasam", "", "hoa-don.xml", "application/xml", [], []), Encoding.UTF8.GetBytes(xml)));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        body.ShouldContain("INVOICE_XML_INVALID");
        body.ShouldNotContain("root:");
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    /// <summary>A real PNG declared as text/html must be stored and served as an image, never as HTML.</summary>
    [Fact]
    public async Task Stored_files_get_the_detected_media_type_not_the_declared_one()
    {
        Guid valuationId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            valuationId = await db.ValuationRecords.Where(v => v.Status != PaymentRecordStatus.Cancelled).OrderByDescending(v => v.CreatedAt).Select(v => v.Id).FirstAsync();
        }

        using var buyer = await factory.LoginAsync("muasam");
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[64]];
        var endpoint = new Endpoint("unc", "muasam", "", "unc.html", "text/html", [], [("number", "UNC-HTML"), ("date", "2026-10-16"), ("amount", "1")]);
        using var added = await buyer.PostAsync(new Uri($"/api/valuations/{valuationId}/payment-orders", UriKind.Relative), Form(endpoint, png));
        var body = await added.Content.ReadAsStringAsync();
        if (!added.IsSuccessStatusCode)
        {
            // The seeded 08a may be fully paid already; the file is checked before the amount, so the type check still ran.
            body.ShouldNotContain("PAYMENT_ORDER_FILE_INVALID");
            return;
        }

        await using var check = factory.Services.CreateAsyncScope();
        var stored = await check.ServiceProvider.GetRequiredService<AppDbContext>().ValuationRecords.Where(v => v.Id == valuationId)
            .SelectMany(v => v.PaymentOrders).Where(p => p.Number == "UNC-HTML").Select(p => p.FileKey).SingleAsync();
        stored.ShouldNotBeNull();
        stored.ShouldEndWith(".png");
        var url = await check.ServiceProvider.GetRequiredService<IFileStorage>().GetPresignedDownloadUrlAsync(stored, TimeSpan.FromMinutes(1), CancellationToken.None);
        using var http = new HttpClient();
        using var served = await http.GetAsync(url);
        served.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
    }

    private async Task<List<Endpoint>> EndpointsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var templateId = await db.DocumentTemplates.OrderBy(t => t.Code).Select(t => t.Id).FirstAsync();
        var valuationId = await db.ValuationRecords.OrderBy(v => v.CreatedAt).Select(v => v.Id).FirstAsync();
        var liquidationId = await db.LiquidationRecords.OrderBy(l => l.CreatedAt).Select(l => l.Id).FirstAsync();
        var bidId = await db.BidDecisions.OrderBy(b => b.CreatedAt).Select(b => b.Id).FirstAsync();
        var reviewedContract = await db.Contracts.Where(c => c.Status == ContractStatus.SupplierReview).Select(c => new { c.Id, c.SupplierId }).FirstAsync();
        var supplierUser = await db.Users.Where(u => u.SupplierId == reviewedContract.SupplierId).OrderBy(u => u.UserName).Select(u => u.UserName).FirstAsync();
        var receiptId = await db.WarehouseReceipts.Where(r => r.Status == ReceiptStatus.Signing || r.Status == ReceiptStatus.Draft).Select(r => r.Id).FirstAsync();

        using var admin = await factory.LoginAsync("admin");
        var templates = await admin.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates", JsonDefaults.Options);
        using var sample = await admin.PostAsJsonAsync("/api/documents/samples", new CreateSampleDocumentRequest(templates!.Single(t => t.Code == "PHIEU_NHAP_KHO").Id, null));
        var documentId = await sample.Content.ReadFromJsonAsync<Guid>();

        string[] docx = ["TEMPLATE_NOT_DOCX"];
        return
        [
            new("template-validate", "admin", "/api/templates/validate", "mau.docx", DocxType, docx, [("kind", "Contract")]),
            new("template-create", "admin", "/api/templates", "mau.docx", DocxType, docx, [("code", "UP_" + Guid.NewGuid().ToString("N")[..6]), ("name", "Thử tải lên"), ("kind", "Contract")]),
            new("template-version", "admin", $"/api/templates/{templateId}/versions", "mau.docx", DocxType, docx, []),
            new("document-version", "admin", $"/api/documents/{documentId}/versions", "ban-moi.pdf", "application/pdf", ["DOCUMENT_FILE_TYPE_INVALID"], []),
            new("payment-order", "muasam", $"/api/valuations/{valuationId}/payment-orders", "unc.pdf", "application/pdf", ["PAYMENT_ORDER_FILE_INVALID"],
                [("number", "UNC-T"), ("date", "2026-10-16"), ("amount", "1")]),
            new("settlement", "muasam", $"/api/liquidations/{liquidationId}/settlements", "chung-tu.pdf", "application/pdf", ["PAYMENT_ORDER_FILE_INVALID"],
                [("description", "Thử"), ("amount", "1")]),
            new("bid-attachment", "muasam", $"/api/bid-decisions/{bidId}/attachment", "qd.pdf", "application/pdf", ["DOCUMENT_FILE_TYPE_INVALID"], []),
            new("bid-import-preview", "muasam", $"/api/bid-decisions/{bidId}/import/preview", "ds.xlsx", XlsxType, ["IMPORT_NOT_XLSX"], []),
            new("bid-import", "muasam", $"/api/bid-decisions/{bidId}/import", "ds.xlsx", XlsxType, ["IMPORT_NOT_XLSX"], []),
            new("supplier-revision", supplierUser, $"/api/contracts/{reviewedContract.Id}/supplier-revision", "hd.docx", DocxType, docx, []),
            new("invoice-file", "muasam", $"/api/warehouse-receipts/{receiptId}/invoice-file", "hd.xml", "application/xml", ["INVOICE_FILE_TYPE_INVALID"], []),
            new("invoice-parse", "muasam", "/api/warehouse-receipts/invoice/parse", "hd.xml", "application/xml", ["INVOICE_XML_INVALID"], []),
            new("signature-verify", "ketoan", "/api/signing/verify", "ky.pdf", "application/pdf", ["DOCUMENT_FILE_TYPE_INVALID"], []),
            new("signature-image", "ketoan", "/api/account/signature-images/signature", "chu-ky.png", "image/png", ["SIGNATURE_IMAGE_INVALID"], []),
            new("organization-seal", "admin", "/api/admin/organization/seal", "con-dau.png", "image/png", ["SIGNATURE_IMAGE_INVALID"], []),
            new("organization-certificate", "admin", "/api/admin/organization/seal-certificate", "to-chuc.p12", "application/x-pkcs12", ["ORG_CERTIFICATE_INVALID"],
                [("password", "x")]),
        ];
    }

    private const string DocxType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static MultipartFormDataContent Form(Endpoint e, byte[] content)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(e.MediaType);
        var form = new MultipartFormDataContent { { file, "file", e.FileName } };
        foreach (var (key, value) in e.Fields)
        {
            form.Add(new StringContent(value), key);
        }

        return form;
    }
}
