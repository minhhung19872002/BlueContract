using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Xml.Linq;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Admin;
using BlueContract.Application.Bids;
using BlueContract.Application.Catalog;
using BlueContract.Application.Common;
using BlueContract.Application.Contracts;
using BlueContract.Application.Documents;
using BlueContract.Application.Signing;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Catalog;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Workflows;
using DocumentFormat.OpenXml.Packaging;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Contracts;

/// <summary>
/// E2E #1 at API level (spec §13): bid decision → HIS sync → contract → review → supplier edits → accountant decides
/// → supplier agrees → supplier signs → initial → director → seal → effective; then an appendix.
/// </summary>
[Collection(ApiCollection.Name)]
public class ContractFlowTests(ApiFactory factory)
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [Fact]
    public async Task Bid_to_effective_contract_with_supplier_revision_loop_and_appendix()
    {
        using var procurement = await factory.LoginAsync("muasam");
        var (bidId, bidItems) = await CreateActiveBidAsync(procurement, "0101234567");
        var supplierId = bidItems[0].SupplierId;
        var lines = bidItems.Take(3).ToList();
        var ids = await DrugContractIdsAsync(procurement);

        // Rules: price ≤ winning price, quantity ≤ remaining winning quantity.
        using (var tooExpensive = await procurement.PostAsJsonAsync("/api/contracts",
                   ContractRequest(ids, bidId, supplierId, lines, priceDelta: 1, number: Unique("HĐ-GIA")), JsonDefaults.Options))
        {
            tooExpensive.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await tooExpensive.Content.ReadAsStringAsync()).ShouldContain("CONTRACT_PRICE_EXCEEDS_BID");
        }

        using (var tooMany = await procurement.PostAsJsonAsync("/api/contracts",
                   ContractRequest(ids, bidId, supplierId, lines, quantityFactor: 2, number: Unique("HĐ-SL")), JsonDefaults.Options))
        {
            (await tooMany.Content.ReadAsStringAsync()).ShouldContain("CONTRACT_QTY_EXCEEDS_BID");
        }

        var request = ContractRequest(ids, bidId, supplierId, lines, number: Unique("HĐ"));
        using var created = await procurement.PostAsJsonAsync("/api/contracts", request, JsonDefaults.Options);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var contractId = await created.Content.ReadFromJsonAsync<Guid>();
        var contract = await GetContractAsync(procurement, contractId);
        contract.Status.ShouldBe(ContractStatus.Draft);
        contract.TotalAmount.ShouldBe(lines.Sum(l => decimal.Round(l.Quantity * 0.5m * l.UnitPrice, 0, MidpointRounding.AwayFromZero)));
        contract.DocumentId.ShouldNotBeNull();
        var documentId = contract.DocumentId!.Value;

        // The remaining bid quantity now excludes this contract.
        var remaining = await procurement.GetFromJsonAsync<List<BidItemDto>>($"/api/bid-decisions/{bidId}/items?supplierId={supplierId}", JsonDefaults.Options);
        remaining!.Single(i => i.ItemId == lines[0].ItemId).RemainingQuantity.ShouldBe(lines[0].Quantity - (lines[0].Quantity * 0.5m));

        using (var early = await factory.LoginAsync("ncc1"))
        {
            (await early.GetAsync(new Uri($"/api/contracts/{contractId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        (await procurement.PostAsync(new Uri($"/api/contracts/{contractId}/submit", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetContractAsync(procurement, contractId)).Status.ShouldBe(ContractStatus.InReview);

        // Tổ rà soát → gửi NCC.
        using var reviewer = await factory.LoginAsync("rasoat");
        await ApproveAsync(reviewer, documentId, "Nội dung phù hợp, gửi NCC rà soát");

        // Only the contract's supplier sees it.
        using var otherSupplier = await factory.LoginAsync("ncc2");
        (await otherSupplier.GetAsync(new Uri($"/api/contracts/{contractId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var supplier = await factory.LoginAsync("ncc1");
        var forSupplier = await GetContractAsync(supplier, contractId);
        forSupplier.Status.ShouldBe(ContractStatus.SupplierReview);
        forSupplier.Actions.SupplierReviewStepId.ShouldNotBeNull();

        // NCC uploads a tracked-changes revision; "Đồng ý ký" is refused while changes are pending.
        var docx = await DownloadCurrentDocxAsync(supplier, documentId);
        using (var plain = await supplier.PostAsync(new Uri($"/api/contracts/{contractId}/supplier-revision", UriKind.Relative), Upload(docx, "hop-dong.docx")))
        {
            (await plain.Content.ReadAsStringAsync()).ShouldContain("CONTRACT_REVISION_REQUIRED");
        }

        using (var revised = await supplier.PostAsync(new Uri($"/api/contracts/{contractId}/supplier-revision", UriKind.Relative),
                   Upload(WithTrackedChanges(docx), "hop-dong-ncc-sua.docx")))
        {
            revised.StatusCode.ShouldBe(HttpStatusCode.OK, await revised.Content.ReadAsStringAsync());
        }

        using (var agree = await supplier.PostAsJsonAsync($"/api/contracts/{contractId}/supplier-response", new SupplierResponseRequest(true, null), JsonDefaults.Options))
        {
            (await agree.Content.ReadAsStringAsync()).ShouldContain("CONTRACT_SUPPLIER_HAS_CHANGES");
        }

        (await supplier.PostAsJsonAsync($"/api/contracts/{contractId}/supplier-response", new SupplierResponseRequest(false, "Đề nghị sửa thời gian giao hàng"), JsonDefaults.Options))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Kế toán: lists the two changes, accepts the insertion, rejects the deletion, sends back to the supplier.
        using var accountant = await factory.LoginAsync("ketoan");
        var atAccountant = await GetContractAsync(accountant, contractId);
        atAccountant.Status.ShouldBe(ContractStatus.AccountantReview);
        atAccountant.Actions.AccountantReviewStepId.ShouldNotBeNull();
        atAccountant.Actions.PendingRevisionCount.ShouldBe(2);

        var revisions = await accountant.GetFromJsonAsync<RevisionListDto>($"/api/contracts/{contractId}/revisions", JsonDefaults.Options);
        revisions!.Revisions.Select(r => r.Kind).ShouldBe([RevisionKind.Deletion, RevisionKind.Insertion]);
        revisions.Revisions[1].Text.ShouldBe("Giao hàng trong 03 ngày.");
        revisions.Revisions[1].Author.ShouldBe("NCC1");

        using (var stale = await accountant.PostAsJsonAsync($"/api/contracts/{contractId}/revisions",
                   new ResolveRevisionsRequest(Guid.NewGuid(), [new("R1", false)]), JsonDefaults.Options))
        {
            stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        (await accountant.PostAsJsonAsync($"/api/contracts/{contractId}/revisions",
            new ResolveRevisionsRequest(revisions.VersionId, [new("R1", false), new("R2", true)]), JsonDefaults.Options)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var resolvedDocx = await DownloadCurrentDocxAsync(accountant, documentId);
        var resolvedText = BodyText(resolvedDocx);
        resolvedText.ShouldContain("Giao hàng trong 03 ngày.");
        CountRevisions(resolvedDocx).ShouldBe(0);

        await ReturnAsync(accountant, documentId, toOrder: atAccountant.Actions.SupplierReviewOrder!.Value, "Kế toán giữ nguyên điều khoản bị xóa, đề nghị NCC xác nhận");
        (await GetContractAsync(supplier, contractId)).Status.ShouldBe(ContractStatus.SupplierReview);

        // NCC đồng ý bản cuối → chốt bản, PDF → NCC ký số.
        (await supplier.PostAsJsonAsync($"/api/contracts/{contractId}/supplier-response", new SupplierResponseRequest(true, null), JsonDefaults.Options))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetContractAsync(supplier, contractId)).Status.ShouldBe(ContractStatus.SupplierSigning);

        await SignAsync(supplier, documentId);
        (await GetContractAsync(procurement, contractId)).Status.ShouldBe(ContractStatus.AccountantInitialing);
        await SignAsync(accountant, documentId);
        using var director = await factory.LoginAsync("giamdoc");
        await SignAsync(director, documentId);
        (await GetContractAsync(procurement, contractId)).Status.ShouldBe(ContractStatus.Stamping);
        using var clerk = await factory.LoginAsync("vanthu");
        await SignAsync(clerk, documentId);

        var effective = await GetContractAsync(procurement, contractId);
        effective.Status.ShouldBe(ContractStatus.Effective);
        effective.EffectiveAt.ShouldNotBeNull();
        effective.Actions.CanCreateAppendix.ShouldBeTrue();
        effective.Actions.CanEdit.ShouldBeFalse();

        var document = await procurement.GetFromJsonAsync<DocumentDto>($"/api/documents/{documentId}", JsonDefaults.Options);
        var final = document!.Versions.Single(v => v.IsCurrent);
        final.IsFinal.ShouldBeTrue();
        var verify = await procurement.GetFromJsonAsync<VerifyResultDto>($"/api/signing/documents/{documentId}/versions/{final.Id}/verify", JsonDefaults.Options);
        verify!.SignatureCount.ShouldBe(4);
        verify.AllValid.ShouldBeTrue();

        // A cancelled or effective contract can no longer be edited.
        using (var edit = await procurement.PutAsJsonAsync($"/api/contracts/{contractId}", request, JsonDefaults.Options))
        {
            edit.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        // Phụ lục bổ sung số lượng → cùng luồng ký → áp dụng vào hợp đồng.
        var item = effective.Items[0];

        // AUDIT-1 §10.4: the added quantity stays within what is left of the bid decision for this supplier.
        var bidLeft = (await procurement.GetFromJsonAsync<List<BidItemDto>>($"/api/bid-decisions/{bidId}/items?supplierId={supplierId}", JsonDefaults.Options))!
            .Single(i => i.ItemId == item.ItemId).RemainingQuantity;
        using (var overBid = await procurement.PostAsJsonAsync($"/api/contracts/{contractId}/appendices",
                   new SaveAppendixRequest(null, AppendixType.AddQuantity, "Vượt quyết định thầu", null, null, null,
                       [new AppendixLineRequest(item.Id, item.Quantity + bidLeft + 1, null)]), JsonDefaults.Options))
        {
            overBid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await overBid.Content.ReadAsStringAsync()).ShouldContain("APPENDIX_QTY_EXCEEDS_BID");
        }

        bidLeft.ShouldBeGreaterThanOrEqualTo(100);

        // AUDIT-1 §10.6: three simultaneous "Lập phụ lục" → one pending appendix, the others are told why.
        var appendixRequest = new SaveAppendixRequest(null, AppendixType.AddQuantity, "Nhu cầu sử dụng tăng", null, null, null,
            [new AppendixLineRequest(item.Id, item.Quantity + 100, null)]);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            procurement.PostAsJsonAsync($"/api/contracts/{contractId}/appendices", appendixRequest, JsonDefaults.Options)));
        var attemptBodies = await Task.WhenAll(attempts.Select(a => a.Content.ReadAsStringAsync()));
        attempts.Count(a => a.IsSuccessStatusCode).ShouldBe(1, string.Join(" | ", attemptBodies));
        attempts.Where(a => !a.IsSuccessStatusCode).ShouldAllBe(a => a.StatusCode == HttpStatusCode.Conflict, string.Join(" | ", attemptBodies));
        attemptBodies.Where((_, i) => !attempts[i].IsSuccessStatusCode).ShouldAllBe(b => b.Contains("APPENDIX_PENDING_EXISTS"));
        var appendixId = await attempts.Single(a => a.IsSuccessStatusCode).Content.ReadFromJsonAsync<Guid>();
        foreach (var a in attempts)
        {
            a.Dispose();
        }

        var withAppendix = await GetContractAsync(procurement, contractId);
        var appendixDoc = withAppendix.Appendices.Single().DocumentId!.Value;
        withAppendix.Appendices.Single().Number.ShouldBe("01");

        (await procurement.PostAsync(new Uri($"/api/contracts/appendices/{appendixId}/submit", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ApproveAsync(reviewer, appendixDoc, "OK");
        await ApproveAsync(supplier, appendixDoc, "Đồng ý", skipNextOptional: true);
        await SignAsync(supplier, appendixDoc);
        await SignAsync(accountant, appendixDoc);
        await SignAsync(director, appendixDoc);
        await SignAsync(clerk, appendixDoc);

        var applied = await GetContractAsync(procurement, contractId);
        applied.Appendices.Single().Status.ShouldBe(AppendixStatus.Effective);
        applied.Items.Single(i => i.Id == item.Id).Quantity.ShouldBe(item.Quantity + 100);
        applied.TotalAmount.ShouldBe(effective.TotalAmount + (100 * item.UnitPrice));

        // AUDIT-1 §11.8: "thay đổi thông tin" lists the contract fields it changes (old → new); once the appendix is
        // signed they are written into the contract and the change shows in its history.
        using (var infoCreated = await procurement.PostAsJsonAsync($"/api/contracts/{contractId}/appendices", new
        {
            type = "InfoChange",
            reason = "Cập nhật điều khoản thanh toán và bảo lãnh",
            lines = Array.Empty<object>(),
            infoFields = new object[]
                   {
                       new { field = "PaymentTerms", newValue = "Thanh toán trong 30 ngày kể từ ngày nhận đủ hồ sơ." },
                       new { field = "GuaranteeNumber", newValue = "BL-99/2026" },
                       new { field = "GuaranteeAmount", newValue = "150000000" },
                   },
        }, JsonDefaults.Options))
        {
            infoCreated.StatusCode.ShouldBe(HttpStatusCode.OK, await infoCreated.Content.ReadAsStringAsync());
            appendixId = await infoCreated.Content.ReadFromJsonAsync<Guid>();
        }

        var infoAppendix = (await procurement.GetFromJsonAsync<InfoContractView>($"/api/contracts/{contractId}", JsonDefaults.Options))!
            .Appendices.Single(a => a.Id == appendixId);
        infoAppendix.InfoFields.Select(f => (f.Field, f.OldValue, f.NewValue)).ShouldBe(
        [
            ("PaymentTerms", applied.PaymentTerms, "Thanh toán trong 30 ngày kể từ ngày nhận đủ hồ sơ."),
            ("GuaranteeNumber", applied.GuaranteeNumber, "BL-99/2026"),
            ("GuaranteeAmount", applied.GuaranteeAmount?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), "150000000"),
        ]);
        var infoDoc = infoAppendix.DocumentId!.Value;
        (await procurement.PostAsync(new Uri($"/api/contracts/appendices/{appendixId}/submit", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ApproveAsync(reviewer, infoDoc, "OK");
        await ApproveAsync(supplier, infoDoc, "Đồng ý", skipNextOptional: true);
        await SignAsync(supplier, infoDoc);
        await SignAsync(accountant, infoDoc);
        await SignAsync(director, infoDoc);
        await SignAsync(clerk, infoDoc);

        var changed = await GetContractAsync(procurement, contractId);
        changed.PaymentTerms.ShouldBe("Thanh toán trong 30 ngày kể từ ngày nhận đủ hồ sơ.");
        changed.GuaranteeNumber.ShouldBe("BL-99/2026");
        changed.GuaranteeAmount.ShouldBe(150_000_000m);
        changed.TotalAmount.ShouldBe(applied.TotalAmount);
        var history = await procurement.GetFromJsonAsync<List<ContractHistoryEntryDto>>($"/api/contracts/{contractId}/history", JsonDefaults.Options);
        history!.ShouldContain(h => h.Action == "AppendixApplied" && h.Details!.Contains("BL-99/2026"));
    }

    private sealed record InfoFieldView(string Field, string? OldValue, string? NewValue);

    private sealed record InfoAppendixView(Guid Id, Guid? DocumentId, List<InfoFieldView>? InfoFields)
    {
        public List<InfoFieldView> InfoFields { get; init; } = InfoFields ?? [];
    }

    private sealed record InfoContractView(List<InfoAppendixView> Appendices);

    [Fact]
    public async Task Non_bidding_contract_creates_supplier_account_and_items_inline()
    {
        using var procurement = await factory.LoginAsync("muasam");
        var categories = await procurement.GetFromJsonAsync<List<ItemCategoryDto>>("/api/items/categories", JsonDefaults.Options);
        var repair = categories!.Single(c => c.Code == "SUA_CHUA");
        var templates = await procurement.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates?kind=Contract", JsonDefaults.Options);
        var taxCode = "29" + Random.Shared.NextInt64(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var userName = "ncc" + taxCode[^6..];

        var request = new SaveContractRequest(
            ProcurementType.NonBidding, null, null,
            new SaveSupplierRequest(taxCode, "Công ty TNHH Sửa chữa Thiết bị Nghệ An", null, "TP Vinh, Nghệ An", "Nguyễn Văn Sửa", "Giám đốc", null,
                "123456789", "Vietcombank", "suachua@example.vn", "0912 000 000", true, new SupplierAccountRequest(userName, "Nguyễn Văn Sửa", "suachua@example.vn")),
            Unique("HĐ-SC"), "Sửa chữa máy phát điện dự phòng", ContractKind.LumpSum, false, repair.Id, null, null,
            null, null, null, "30 ngày kể từ ngày ký", "Thanh toán một lần sau nghiệm thu", "Bảo hành 06 tháng", null, null, null, null, null,
            templates!.Single(t => t.Code == "HD_DICHVU_KHONGTHAU").Id,
            [
                new ContractLineRequest(null, new NewItemRequest("SC-MPD-01", "Thay bộ điều tốc máy phát điện 250kVA", "Bộ", "Bộ điều tốc điện tử", null, "Cummins", "Ấn Độ", null), 1, 48_000_000, 10),
                new ContractLineRequest(null, new NewItemRequest("SC-MPD-02", "Nhân công sửa chữa, hiệu chỉnh", "Gói", null, null, null, null, null), 1, 6_500_000, 10),
            ]);

        using var preview = await procurement.PostAsJsonAsync("/api/contracts/preview", request with { SupplierId = null }, JsonDefaults.Options);
        (await preview.Content.ReadAsStringAsync()).ShouldContain("CONTRACT_PREVIEW_SUPPLIER_REQUIRED");

        using var created = await procurement.PostAsJsonAsync("/api/contracts", request, JsonDefaults.Options);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var contract = await GetContractAsync(procurement, await created.Content.ReadFromJsonAsync<Guid>());
        contract.ProcurementType.ShouldBe(ProcurementType.NonBidding);
        contract.SupplierTaxCode.ShouldBe(taxCode);
        contract.TotalAmount.ShouldBe(54_500_000m);
        contract.Items.Count.ShouldBe(2);
        contract.RequiresWarehouseReceipt.ShouldBeFalse();

        using var admin = await factory.LoginAsync("admin");
        var users = await admin.GetFromJsonAsync<PagedResult<UserListItemDto>>($"/api/admin/users?search={userName}", JsonDefaults.Options);
        users!.Items.ShouldContain(u => u.UserName == userName);

        var items = await procurement.GetFromJsonAsync<PagedResult<ItemDto>>($"/api/items?search=SC-MPD&categoryId={repair.Id}", JsonDefaults.Options);
        items!.Items.ShouldAllBe(i => i.Source == ItemSource.Manual && i.SupplierName == "Công ty TNHH Sửa chữa Thiết bị Nghệ An");

        // Preview of an existing-supplier draft renders a PDF.
        using var pdf = await procurement.PostAsJsonAsync("/api/contracts/preview", request with { SupplierId = contract.SupplierId, NewSupplier = null }, JsonDefaults.Options);
        pdf.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
    }

    [Fact]
    public async Task Excel_import_previews_errors_and_imports_only_a_clean_file()
    {
        using var procurement = await factory.LoginAsync("muasam");
        var categories = await procurement.GetFromJsonAsync<List<ItemCategoryDto>>("/api/items/categories", JsonDefaults.Options);
        var bidId = await CreateBidAsync(procurement, categories!.Single(c => c.Code == "VTYT").Id);

        var template = await procurement.GetByteArrayAsync(new Uri("/api/items/import-template?kind=Medical", UriKind.Relative));
        var bad = FillWorkbook(template,
        [
            ["", "VT-A", "Bơm tiêm 5ml", "", "", "Hộp 100 cái", "Cái", "MPV", "Việt Nam", "", "N4", "VTYT", "0302345678", "10.000", "850", ""],
            ["", "VT-B", "Kim luồn 22G", "", "", "", "", "", "", "", "", "VTYT", "9999999999", "1.000", "14,5", ""],
            ["", "VT-A", "Bơm tiêm 5ml (trùng)", "", "", "", "Cái", "", "", "", "", "TAI_SAN", "0302345678", "abc", "850", ""],
        ]);
        using var preview = await procurement.PostAsync(new Uri($"/api/bid-decisions/{bidId}/import/preview", UriKind.Relative), Upload(bad, "import.xlsx"));
        preview.StatusCode.ShouldBe(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        var result = await preview.Content.ReadFromJsonAsync<ImportPreviewDto>(JsonDefaults.Options);
        result!.TotalRows.ShouldBe(3);
        result.ValidRows.ShouldBe(1);
        result.Rows[0].Quantity.ShouldBe(10_000);
        result.Rows[1].Errors.Select(e => e.Column).ShouldBe([ItemImportColumns.Unit, ItemImportColumns.SupplierTaxCode], ignoreOrder: true);
        result.Rows[1].UnitPrice.ShouldBe(14.5m);
        result.Rows[2].Errors.Select(e => e.Column).ShouldBe([ItemImportColumns.Quantity, ItemImportColumns.Category, ItemImportColumns.Code], ignoreOrder: true);

        using (var refused = await procurement.PostAsync(new Uri($"/api/bid-decisions/{bidId}/import", UriKind.Relative), Upload(bad, "import.xlsx")))
        {
            (await refused.Content.ReadAsStringAsync()).ShouldContain("IMPORT_HAS_ERRORS");
        }

        var good = FillWorkbook(template,
        [
            ["K-001", "VT-A", "Bơm tiêm 5ml", "", "", "Hộp 100 cái", "Cái", "MPV", "Việt Nam", "", "N4", "VTYT", "0302345678", "10.000", "850", ""],
            ["", "VT-B", "Kim luồn 22G", "", "", "Hộp 50 cái", "Cái", "B. Braun", "Malaysia", "", "N2", "", "0302345678", "1.000", "14.500", "5"],
        ]);
        using var imported = await procurement.PostAsync(new Uri($"/api/bid-decisions/{bidId}/import", UriKind.Relative), Upload(good, "import.xlsx"));
        (await imported.Content.ReadFromJsonAsync<ImportResultDto>(JsonDefaults.Options))!.Created.ShouldBe(2);

        var detail = await procurement.GetFromJsonAsync<BidDecisionDto>($"/api/bid-decisions/{bidId}", JsonDefaults.Options);
        var supplier = detail!.Suppliers.Single();
        supplier.TaxCode.ShouldBe("0302345678");
        supplier.WinningValue.ShouldBe(8_500_000m + 14_500_000m);
        var items = await procurement.GetFromJsonAsync<List<BidItemDto>>($"/api/bid-decisions/{bidId}/items", JsonDefaults.Options);
        items!.Single(i => i.Code == "VT-A").HisKey.ShouldBe("K-001");
        items!.ShouldAllBe(i => i.Source == ItemSource.Import);

        // Re-importing updates instead of duplicating.
        using var again = await procurement.PostAsync(new Uri($"/api/bid-decisions/{bidId}/import", UriKind.Relative), Upload(good, "import.xlsx"));
        (await again.Content.ReadFromJsonAsync<ImportResultDto>(JsonDefaults.Options))!.Updated.ShouldBe(2);
    }

    [Fact]
    public async Task Demo_scenarios_are_seeded_in_every_state()
    {
        using var procurement = await factory.LoginAsync("muasam");
        var list = await procurement.GetFromJsonAsync<PagedResult<ContractListItemDto>>("/api/contracts?search=2026/HĐ&pageSize=100", JsonDefaults.Options);
        list!.Items.Single(c => c.Number == "15/2026/HĐ-BVĐK").Status.ShouldBe(ContractStatus.SupplierReview);
        list.Items.Single(c => c.Number == "16/2026/HĐ-BVĐK").Status.ShouldBe(ContractStatus.Effective);
        var repair = await procurement.GetFromJsonAsync<PagedResult<ContractListItemDto>>("/api/contracts?search=17/2026/HĐ-SC", JsonDefaults.Options);
        repair!.Items.Single().Status.ShouldBe(ContractStatus.Draft);

        // Suppliers never see internal drafts; the contract under review is listed with the review action.
        using var supplier = await factory.LoginAsync("ncc1");
        var mine = await supplier.GetFromJsonAsync<PagedResult<ContractListItemDto>>("/api/contracts?pageSize=100", JsonDefaults.Options);
        mine!.Items.ShouldAllBe(c => c.SupplierName == "Công ty Cổ phần Dược phẩm Minh An" && c.Status != ContractStatus.Draft);
        var review = await GetContractAsync(supplier, mine.Items.Single(c => c.Number == "15/2026/HĐ-BVĐK").Id);
        review.Actions.SupplierReviewStepId.ShouldNotBeNull();
    }

    // ---------------- helpers ----------------

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}/2026";

    private static async Task<Guid> CreateBidAsync(HttpClient client, Guid categoryId)
    {
        using var created = await client.PostAsJsonAsync("/api/bid-decisions",
            new SaveBidDecisionRequest(Unique("QĐ"), new DateOnly(2026, 2, 15), "Phê duyệt kết quả lựa chọn nhà thầu", "Gói thầu số 01", "Đấu thầu rộng rãi qua mạng",
                "Nguồn thu sự nghiệp", "12 tháng", categoryId, null, null), JsonDefaults.Options);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        return await created.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task<(Guid BidId, List<BidItemDto> Items)> CreateActiveBidAsync(HttpClient client, string taxCode)
    {
        var categories = await client.GetFromJsonAsync<List<ItemCategoryDto>>("/api/items/categories", JsonDefaults.Options);
        var bidId = await CreateBidAsync(client, categories!.Single(c => c.Code == "THUOC").Id);
        var suppliers = await client.GetFromJsonAsync<List<LookupItem>>("/api/lookups/suppliers");
        var supplierId = suppliers!.Single(s => s.Code == taxCode).Id;
        (await client.PutAsJsonAsync($"/api/bid-decisions/{bidId}/suppliers", new BidSupplierRequest(supplierId, 0), JsonDefaults.Options)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var sync = await client.PostAsync(new Uri($"/api/bid-decisions/{bidId}/his-sync", UriKind.Relative), null);
        var synced = await sync.Content.ReadFromJsonAsync<HisSyncResultDto>(JsonDefaults.Options);
        synced!.Created.ShouldBeGreaterThanOrEqualTo(8);

        // Re-sync is idempotent.
        using var again = await client.PostAsync(new Uri($"/api/bid-decisions/{bidId}/his-sync", UriKind.Relative), null);
        (await again.Content.ReadFromJsonAsync<HisSyncResultDto>(JsonDefaults.Options))!.Created.ShouldBe(0);

        (await client.PostAsync(new Uri($"/api/bid-decisions/{bidId}/activate", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var items = (await client.GetFromJsonAsync<List<BidItemDto>>($"/api/bid-decisions/{bidId}/items", JsonDefaults.Options))!;
        items.ShouldAllBe(i => i.Source == ItemSource.His && i.HisKey != null);
        return (bidId, items);
    }

    private static SaveContractRequest ContractRequest((Guid Category, Guid Template) ids, Guid bidId, Guid supplierId, List<BidItemDto> lines, string number,
        decimal priceDelta = 0, decimal quantityFactor = 0.5m) =>
        new(ProcurementType.Bidding, bidId, supplierId, null, number, "Cung cấp thuốc generic năm 2026", ContractKind.UnitPriceCallOff, true,
            ids.Category, null, null, null, new DateOnly(2026, 3, 1), new DateOnly(2027, 2, 28), "12 tháng kể từ ngày ký",
            "Thanh toán trong 90 ngày kể từ ngày nghiệm thu", "Hạn dùng còn lại tối thiểu 12 tháng", "Giao tại kho Bên A trong 05 ngày", null, null, null, null,
            ids.Template, [.. lines.Select(l => new ContractLineRequest(l.ItemId, null, l.Quantity * quantityFactor, l.UnitPrice + priceDelta, 5))]);

    private static async Task<(Guid Category, Guid Template)> DrugContractIdsAsync(HttpClient client)
    {
        var categories = await client.GetFromJsonAsync<List<ItemCategoryDto>>("/api/items/categories", JsonDefaults.Options);
        var templates = await client.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates?kind=Contract", JsonDefaults.Options);
        return (categories!.Single(c => c.Code == "THUOC").Id, templates!.Single(t => t.Code == "HD_THUOC").Id);
    }

    private static async Task<ContractDto> GetContractAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<ContractDto>($"/api/contracts/{id}", JsonDefaults.Options))!;

    private static async Task<WorkflowDto> WorkflowAsync(HttpClient client, Guid documentId) =>
        (await client.GetFromJsonAsync<WorkflowDto>($"/api/workflows/by-document/{documentId}", JsonDefaults.Options))!;

    private static async Task ApproveAsync(HttpClient client, Guid documentId, string comment, bool skipNextOptional = false)
    {
        var wf = await WorkflowAsync(client, documentId);
        wf.MyStepId.ShouldNotBeNull("the user should have the active step");
        using var response = await client.PostAsJsonAsync($"/api/workflows/steps/{wf.MyStepId}/approve", new ApproveStepRequest(comment, skipNextOptional), JsonDefaults.Options);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task ReturnAsync(HttpClient client, Guid documentId, int toOrder, string reason)
    {
        var wf = await WorkflowAsync(client, documentId);
        using var response = await client.PostAsJsonAsync($"/api/workflows/steps/{wf.MyStepId}/return", new ReturnStepRequest(toOrder, reason), JsonDefaults.Options);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task SignAsync(HttpClient client, Guid documentId)
    {
        var wf = await WorkflowAsync(client, documentId);
        wf.MyStepId.ShouldNotBeNull("the user should have the signing step");
        using var response = await client.PostAsJsonAsync($"/api/signing/steps/{wf.MyStepId}/sign", new BeginSignRequest("MOCK", null, null), JsonDefaults.Options);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<SigningRequestDto>(JsonDefaults.Options))!.Status.ShouldBe(SigningRequestStatus.Completed);
    }

    private static async Task<byte[]> DownloadCurrentDocxAsync(HttpClient client, Guid documentId)
    {
        var document = await client.GetFromJsonAsync<DocumentDto>($"/api/documents/{documentId}", JsonDefaults.Options);
        var current = document!.Versions.Single(v => v.IsCurrent);
        return await client.GetByteArrayAsync(new Uri($"/api/documents/{documentId}/versions/{current.Id}/file?format=docx", UriKind.Relative));
    }

    /// <summary>Simulates the supplier editing with Track Changes: deletes the first run and inserts a sentence.</summary>
    internal static byte[] WithTrackedChanges(byte[] docx)
    {
        using var stream = new MemoryStream();
        stream.Write(docx);
        using (var doc = WordprocessingDocument.Open(stream, true))
        {
            var part = doc.MainDocumentPart!;
            XDocument xml;
            using (var read = part.GetStream(FileMode.Open, FileAccess.Read))
            {
                xml = XDocument.Load(read);
            }

            var run = xml.Descendants(W + "r").First(r => r.Elements(W + "t").Any(t => t.Value.Trim().Length > 3));
            var deleted = new XElement(run);
            foreach (var t in deleted.Elements(W + "t").ToList())
            {
                t.Name = W + "delText";
            }

            var date = "2026-10-16T08:00:00Z";
            run.ReplaceWith(
                new XElement(W + "del", new XAttribute(W + "id", "901"), new XAttribute(W + "author", "NCC1"), new XAttribute(W + "date", date), deleted),
                new XElement(W + "ins", new XAttribute(W + "id", "902"), new XAttribute(W + "author", "NCC1"), new XAttribute(W + "date", date),
                    new XElement(W + "r", new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), "Giao hàng trong 03 ngày."))));
            using var write = part.GetStream(FileMode.Create, FileAccess.Write);
            xml.Save(write);
        }

        return stream.ToArray();
    }

    internal static int CountRevisions(byte[] docx) => MainXml(docx).Descendants().Count(e => e.Name == W + "ins" || e.Name == W + "del");

    internal static string BodyText(byte[] docx) => string.Concat(MainXml(docx).Descendants(W + "t").Select(t => t.Value));

    private static XDocument MainXml(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(stream, false);
        using var part = doc.MainDocumentPart!.GetStream(FileMode.Open, FileAccess.Read);
        return XDocument.Load(part);
    }

    private static byte[] FillWorkbook(byte[] template, string[][] rows)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(template));
        var sheet = workbook.Worksheet("Danh mục");
        for (var r = 0; r < rows.Length; r++)
        {
            sheet.Cell(4 + r, 1).Value = r + 1;
            for (var c = 0; c < rows[r].Length; c++)
            {
                sheet.Cell(4 + r, c + 2).Value = rows[r][c];
            }
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private static MultipartFormDataContent Upload(byte[] content, string fileName)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return form;
    }
}
