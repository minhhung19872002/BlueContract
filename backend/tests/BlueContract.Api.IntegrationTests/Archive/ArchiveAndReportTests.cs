using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Archive;
using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Receipts;
using BlueContract.Application.Reports;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Receipts;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Archive;

/// <summary>Spec §13 E2E #4 at API level (tải hồ sơ ZIP + mục lục + hash), dashboard and reports.</summary>
[Collection(ApiCollection.Name)]
public class ArchiveAndReportTests(ApiFactory factory)
{
    private const string LiquidatedContract = "18/2026/HĐ-DV";

    [Fact]
    public async Task Dossier_tree_zip_with_index_and_completion_mark()
    {
        using var buyer = await factory.LoginAsync("muasam");
        var found = await buyer.GetFromJsonAsync<PagedResult<ArchiveSearchItemDto>>($"/api/archive?search={Uri.EscapeDataString(LiquidatedContract)}", JsonDefaults.Options);
        var item = found!.Items.Single(i => i.ContractNumber == LiquidatedContract);
        item.Status.ShouldBe(ContractStatus.Liquidated);

        var tree = await buyer.GetFromJsonAsync<ArchiveTreeDto>($"/api/archive/{item.ContractId}", JsonDefaults.Options);
        tree!.Folders.Select(f => f.Title).ShouldBe(
        [
            "Quyết định thầu", "Hợp đồng và phụ lục", "Đề xuất – Tổng hợp – Gọi hàng", "Phiếu nhập kho", "Nghiệm thu",
            "Xác định giá trị (08a), đề nghị thanh toán, ủy nhiệm chi", "Thanh lý",
        ]);
        string[] expected = ["Hợp đồng", "Phiếu xác nhận khối lượng", "Biên bản nghiệm thu", "Biên bản 08a", "Đề nghị thanh toán", "Biên bản thanh lý"];
        foreach (var title in expected)
        {
            tree.Folders.SelectMany(f => f.Children).ShouldContain(n => n.Title.StartsWith(title, StringComparison.Ordinal) && n.Files.Count > 0, title);
        }

        tree.FileCount.ShouldBeGreaterThanOrEqualTo(expected.Length);
        var signedPdf = tree.Folders.SelectMany(f => f.Children).First(n => n.Title.StartsWith("Biên bản thanh lý", StringComparison.Ordinal)).Files[0];
        signedPdf.Kind.ShouldStartWith("PDF đã ký");
        (await buyer.GetFromJsonAsync<FileLinkDto>($"/api/archive/{item.ContractId}/files/{signedPdf.Id}", JsonDefaults.Options))!.Url.ShouldNotBeNull();
        (await buyer.GetAsync(new Uri($"/api/archive/{item.ContractId}/files/bm90LWluLXRyZWU", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // ZIP: one folder per stage + muc_luc.pdf; every packed file still hashes to the value recorded in the database.
        using var zipResponse = await buyer.GetAsync(new Uri($"/api/archive/{item.ContractId}/zip", UriKind.Relative));
        zipResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await zipResponse.Content.ReadAsStringAsync());
        zipResponse.Content.Headers.ContentType!.MediaType.ShouldBe("application/zip");
        await using var zipStream = await zipResponse.Content.ReadAsStreamAsync();
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
        var index = zip.GetEntry("muc_luc.pdf");
        index.ShouldNotBeNull();
        using (var reader = new BinaryReader(index.Open()))
        {
            System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4)).ShouldBe("%PDF");
        }

        zip.Entries.Count.ShouldBe(tree.FileCount + 1);
        zip.Entries.ShouldContain(e => e.FullName.StartsWith("07_Thanh_ly/", StringComparison.Ordinal));
        var withHash = tree.Folders.SelectMany(f => f.Children).SelectMany(n => n.Files).Where(f => f.Sha256 != null).Select(f => f.Sha256!).ToHashSet();
        var packedHashes = zip.Entries.Where(e => e.FullName != "muc_luc.pdf").Select(e =>
        {
            using var s = e.Open();
            return Convert.ToHexStringLower(SHA256.HashData(s));
        }).ToHashSet();
        withHash.IsSubsetOf(packedHashes).ShouldBeTrue("every recorded SHA-256 must match a packed file");

        // Đánh dấu hồ sơ đã đầy đủ.
        using var complete = await buyer.PutAsJsonAsync($"/api/archive/{item.ContractId}/complete", new ArchiveCompleteRequest(true), JsonDefaults.Options);
        complete.StatusCode.ShouldBe(HttpStatusCode.OK, await complete.Content.ReadAsStringAsync());
        var completed = (await complete.Content.ReadFromJsonAsync<ArchiveTreeDto>(JsonDefaults.Options))!;
        completed.IsArchiveComplete.ShouldBeTrue();
        completed.ArchiveCompletedByName.ShouldNotBeNullOrWhiteSpace();

        // Suppliers have no access to the archive.
        using var supplier = await factory.LoginAsync("ncc3");
        (await supplier.GetAsync(new Uri($"/api/archive/{item.ContractId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Dashboards_show_work_alerts_and_payments_for_each_portal()
    {
        using var director = await factory.LoginAsync("giamdoc");
        var dashboard = await director.GetFromJsonAsync<InternalDashboardDto>("/api/dashboard", JsonDefaults.Options);
        dashboard!.Payments.Count.ShouldBe(12);
        dashboard.PaidTotal.ShouldBeGreaterThanOrEqualTo(63_000_000);
        dashboard.PendingTotal.ShouldBe(dashboard.PendingByType.Sum(p => p.Count));
        dashboard.ExpiryWarningDays.ShouldBe(30);
        (dashboard.WarningReceipts + dashboard.InvalidReceipts).ShouldBeGreaterThan(0);

        using var supplier = await factory.LoginAsync("ncc2");
        (await supplier.GetAsync(new Uri("/api/dashboard", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var supplierDashboard = await supplier.GetFromJsonAsync<SupplierDashboardDto>("/api/dashboard/supplier", JsonDefaults.Options);
        supplierDashboard!.EffectiveContracts.ShouldBeGreaterThanOrEqualTo(1);
        supplierDashboard.CallOrdersAwaiting.ShouldBeGreaterThanOrEqualTo(0);

        // NCC2 never sees NCC3's payment of the liquidated contract.
        supplierDashboard.Payments.Sum(m => m.Paid).ShouldBeLessThan(dashboard.Payments.Sum(m => m.Paid));
    }

    [Fact]
    public async Task The_receipts_needing_attention_list_matches_the_dashboard_count()
    {
        // The dashboard card and the menu badge count open receipts with a warning or invalid check; their link must
        // list exactly those (not the completed ones, not only the warnings).
        using var director = await factory.LoginAsync("giamdoc");
        var dashboard = await director.GetFromJsonAsync<InternalDashboardDto>("/api/dashboard", JsonDefaults.Options);
        var list = await director.GetFromJsonAsync<PagedResult<ReceiptListItemDto>>("/api/warehouse-receipts?needsAttention=true&pageSize=200", JsonDefaults.Options);

        list!.Total.ShouldBe(dashboard!.WarningReceipts + dashboard.InvalidReceipts);
        list.Items.ShouldAllBe(r => r.ValidationStatus != ReceiptValidationStatus.Valid && r.Status != ReceiptStatus.Completed && r.Status != ReceiptStatus.Cancelled);
    }

    [Fact]
    public async Task Reports_compute_execution_debt_payments_and_signing_and_export_excel()
    {
        using var director = await factory.LoginAsync("giamdoc");
        var execution = await director.GetFromJsonAsync<List<ContractExecutionRowDto>>("/api/reports/contract-execution", JsonDefaults.Options);
        var liquidated = execution!.Single(r => r.ContractNumber == LiquidatedContract);
        liquidated.PaidValue.ShouldBe(liquidated.ContractValue);
        liquidated.ExecutionPercent.ShouldBe(100);
        liquidated.RemainingValue.ShouldBe(0);

        var debt = await director.GetFromJsonAsync<List<SupplierDebtRowDto>>("/api/reports/supplier-debt", JsonDefaults.Options);
        debt!.ShouldContain(r => r.TaxCode == "2901234567" && r.AcceptedValue >= 63_000_000);

        var monthly = await director.GetFromJsonAsync<List<PaymentReportRowDto>>("/api/reports/payments?period=Month&dimension=Domain", JsonDefaults.Options);
        monthly!.ShouldContain(r => r.Group == "Hành chính, dịch vụ" && r.PaidAmount >= 63_000_000);
        var yearly = await director.GetFromJsonAsync<List<PaymentReportRowDto>>("/api/reports/payments?period=Year", JsonDefaults.Options);
        yearly!.Sum(r => r.PaidAmount).ShouldBe(monthly!.Sum(r => r.PaidAmount));

        var signing = await director.GetFromJsonAsync<SigningReportDto>("/api/reports/signing", JsonDefaults.Options);
        signing!.Steps.ShouldContain(s => s.WorkflowCode == "WF_RECEIPT" && s.Completed > 0);
        signing.Backlog.ShouldContain(b => b.Pending > 0);

        foreach (var path in new[] { "contract-execution", "supplier-debt", "payments", "signing" })
        {
            using var file = await director.GetAsync(new Uri($"/api/reports/{path}/export", UriKind.Relative));
            file.StatusCode.ShouldBe(HttpStatusCode.OK, path);
            file.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        }

        using var storekeeper = await factory.LoginAsync("thukho");
        (await storekeeper.GetAsync(new Uri("/api/reports/supplier-debt", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
