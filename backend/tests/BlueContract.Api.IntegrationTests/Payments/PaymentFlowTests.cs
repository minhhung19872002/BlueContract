using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Admin;
using BlueContract.Application.Catalog;
using BlueContract.Application.Common;
using BlueContract.Application.Contracts;
using BlueContract.Application.Documents;
using BlueContract.Application.Integration;
using BlueContract.Application.Payments;
using BlueContract.Application.Receipts;
using BlueContract.Application.Signing;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Payments;
using BlueContract.Domain.Receipts;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Payments;

/// <summary>
/// Spec §13 E2E #3 at API level: HIS đẩy phiếu → 5 chữ ký (+ phiếu ảo) → nghiệm thu → 08a + đề nghị thanh toán + UNC →
/// thanh lý chốt công nợ → hợp đồng Đã thanh lý; cột theo dõi HĐ đã nhập / nghiệm thu / thanh toán.
/// </summary>
[Collection(ApiCollection.Name)]
public class PaymentFlowTests(ApiFactory factory)
{
    [Fact]
    public async Task Receipts_to_acceptance_valuation_payment_and_liquidation()
    {
        using var buyer = await factory.LoginAsync("muasam");
        var (contractId, contractNumber) = await CreateEffectiveContractAsync(buyer);
        var contract = await buyer.GetFromJsonAsync<ContractDto>($"/api/contracts/{contractId}", JsonDefaults.Options);
        var first = contract!.Items.Single(i => i.ItemCode.EndsWith("-01", StringComparison.Ordinal));
        var second = contract.Items.Single(i => i.ItemCode.EndsWith("-02", StringComparison.Ordinal));

        // 1. HIS đẩy phiếu nhập cho mặt hàng 1 → 5 chữ ký.
        var receiptId = await PushReceiptAsync(contractNumber, first);
        using (var anyone = await factory.LoginAsync("thukho"))
        {
            var receipt = await anyone.GetFromJsonAsync<ReceiptDto>($"/api/warehouse-receipts/{receiptId}", JsonDefaults.Options);
            receipt!.ValidationStatus.ShouldBe(ReceiptValidationStatus.Valid, string.Join("; ", receipt.Issues.Select(i => i.Message)));
            foreach (var signer in new[] { "thukho", "ncc3", "ketoankho", "truongkhoa", "ketoantruong" })
            {
                using var client = await factory.LoginAsync(signer);
                await ActAsync(client, receipt.DocumentId!.Value);
            }
        }

        // 2. Phiếu thứ hai (mặt hàng 2) cũng từ HIS: HĐ này bắt buộc nhập kho nên không được dùng phiếu ảo (AUDIT-1 §10.2).
        var secondReceiptId = await PushReceiptAsync(contractNumber, second);
        using (var anyone = await factory.LoginAsync("thukho"))
        {
            var receipt = await anyone.GetFromJsonAsync<ReceiptDto>($"/api/warehouse-receipts/{secondReceiptId}", JsonDefaults.Options);
            foreach (var signer in new[] { "thukho", "ncc3", "ketoankho", "truongkhoa", "ketoantruong" })
            {
                using var client = await factory.LoginAsync(signer);
                await ActAsync(client, receipt!.DocumentId!.Value);
            }
        }

        // 3. Biên bản nghiệm thu từ các phiếu đã ký → Rà soát → NCC → GĐ → Văn thư.
        var candidates = await buyer.GetFromJsonAsync<List<AcceptanceReceiptDto>>($"/api/acceptances/candidates?contractId={contractId}", JsonDefaults.Options);
        candidates!.Count.ShouldBe(2);
        using var acceptanceResponse = await buyer.PostAsJsonAsync("/api/acceptances",
            new SaveAcceptanceRequest(contractId, Today.AddDays(-30), Today, null, null, [.. candidates.Select(c => c.ReceiptId)]), JsonDefaults.Options);
        acceptanceResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await acceptanceResponse.Content.ReadAsStringAsync());
        var acceptanceId = await acceptanceResponse.Content.ReadFromJsonAsync<Guid>();
        (await buyer.GetFromJsonAsync<List<AcceptanceReceiptDto>>($"/api/acceptances/candidates?contractId={contractId}", JsonDefaults.Options))!.ShouldBeEmpty();
        var acceptance = await buyer.GetFromJsonAsync<AcceptanceDto>($"/api/acceptances/{acceptanceId}", JsonDefaults.Options);
        acceptance!.TotalAmount.ShouldBe(first.UnitPrice + second.UnitPrice);
        acceptance.Items.Count.ShouldBe(2);
        (await buyer.PostAsync(new Uri($"/api/acceptances/{acceptanceId}/submit", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await SignAllAsync(acceptance.DocumentId!.Value, "rasoat", "ncc3", "giamdoc", "vanthu");
        (await buyer.GetFromJsonAsync<AcceptanceDto>($"/api/acceptances/{acceptanceId}", JsonDefaults.Options))!.Status.ShouldBe(PaymentRecordStatus.Completed);
        (await buyer.GetFromJsonAsync<ContractDto>($"/api/contracts/{contractId}", JsonDefaults.Options))!.Items.ShouldAllBe(i => i.AcceptedQuantity == 1);

        // The monthly job does not take accepted receipts again.
        (await (await buyer.PostAsync(new Uri("/api/acceptances/generate", UriKind.Relative), null)).Content.ReadFromJsonAsync<GenerateAcceptancesResultDto>(JsonDefaults.Options))!
            .Numbers.ShouldNotContain(n => n == acceptance.Number);

        // 4. 08a + đề nghị thanh toán: giá trị kỳ này, lũy kế, % so với HĐ.
        // Sent three times at once (AUDIT-1 §10.6): one 08a, the others get a 4xx with a reason — never a 500.
        var valuationAttempts = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            buyer.PostAsJsonAsync("/api/valuations", new SaveValuationRequest(contractId, [acceptanceId], null, 0, 0), JsonDefaults.Options)));
        var valuationBodies = await Task.WhenAll(valuationAttempts.Select(a => a.Content.ReadAsStringAsync()));
        valuationAttempts.Count(a => a.IsSuccessStatusCode).ShouldBe(1, string.Join(" | ", valuationBodies));
        valuationAttempts.Where(a => !a.IsSuccessStatusCode).ShouldAllBe(a => (int)a.StatusCode >= 400 && (int)a.StatusCode < 500, string.Join(" | ", valuationBodies));
        var valuation = (await valuationAttempts.Single(a => a.IsSuccessStatusCode).Content.ReadFromJsonAsync<ValuationDto>(JsonDefaults.Options))!;
        foreach (var a in valuationAttempts)
        {
            a.Dispose();
        }

        valuation.PaymentSequence.ShouldBe(1);
        valuation.PreviousCumulative.ShouldBe(0);
        valuation.CurrentPeriodValue.ShouldBe(acceptance.TotalAmount);
        valuation.CompletionPercent.ShouldBe(decimal.Round(acceptance.TotalAmount * 100 / contract.TotalAmount, 2));
        valuation.PaymentRequestDocumentId.ShouldNotBeNull();
        using (var again = await buyer.PostAsJsonAsync("/api/valuations", new SaveValuationRequest(contractId, [acceptanceId], null, 0, 0), JsonDefaults.Options))
        {
            (await again.Content.ReadAsStringAsync()).ShouldContain("ACCEPTANCE_ALREADY_IN_VALUATION");
        }

        (await buyer.PostAsync(new Uri($"/api/valuations/{valuation.Id}/submit", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await SignAllAsync(valuation.DocumentId!.Value, "rasoat", "ncc3", "giamdoc", "vanthu");
        await SignAllAsync(valuation.PaymentRequestDocumentId!.Value, "ncc3", "giamdoc");
        valuation = (await buyer.GetFromJsonAsync<ValuationDto>($"/api/valuations/{valuation.Id}", JsonDefaults.Options))!;
        valuation.Status.ShouldBe(PaymentRecordStatus.Completed);
        valuation.PaymentRequestStatus.ShouldBe(PaymentRecordStatus.Completed);
        (await buyer.GetFromJsonAsync<AcceptanceDto>($"/api/acceptances/{acceptanceId}", JsonDefaults.Options))!.IsIncludedInValuation.ShouldBeTrue();

        // 5. Ủy nhiệm chi bổ sung sau khi ký: trả thiếu 1 triệu.
        using (var tooMuch = await buyer.PostAsync(new Uri($"/api/valuations/{valuation.Id}/payment-orders", UriKind.Relative),
                   PaymentOrderForm("UNC-X", valuation.RequestedAmount + 1)))
        {
            (await tooMuch.Content.ReadAsStringAsync()).ShouldContain("PAYMENT_ORDER_EXCEEDS_REQUESTED");
        }

        using (var paid = await buyer.PostAsync(new Uri($"/api/valuations/{valuation.Id}/payment-orders", UriKind.Relative),
                   PaymentOrderForm("UNC-0158", valuation.RequestedAmount - 1_000_000, withFile: true)))
        {
            paid.StatusCode.ShouldBe(HttpStatusCode.OK, await paid.Content.ReadAsStringAsync());
            var after = (await paid.Content.ReadFromJsonAsync<ValuationDto>(JsonDefaults.Options))!;
            after.PaymentStatus.ShouldBe(PaymentStatus.Unpaid);
            after.PaymentOrders.Single().HasFile.ShouldBeTrue();
        }

        // 6. Thanh lý: đối chiếu, chốt công nợ 1 triệu còn thiếu, ký → HĐ Đã thanh lý.
        var preview = await buyer.GetFromJsonAsync<LiquidationPreviewDto>($"/api/liquidations/preview?contractId={contractId}", JsonDefaults.Options);
        preview!.BlockingCode.ShouldBeNull();
        preview.ExecutedValue.ShouldBe(valuation.CurrentPeriodValue);
        preview.RemainingAmount.ShouldBe(1_000_000);
        preview.ReductionAmount.ShouldBe(contract.TotalAmount - valuation.CurrentPeriodValue);
        var liquidationAttempts = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            buyer.PostAsJsonAsync("/api/liquidations", new SaveLiquidationRequest(contractId, "Hai bên thống nhất thanh lý."), JsonDefaults.Options)));
        var liquidationBodies = await Task.WhenAll(liquidationAttempts.Select(a => a.Content.ReadAsStringAsync()));
        liquidationAttempts.Count(a => a.IsSuccessStatusCode).ShouldBe(1, string.Join(" | ", liquidationBodies));
        liquidationBodies.Where((_, i) => !liquidationAttempts[i].IsSuccessStatusCode).ShouldAllBe(b => b.Contains("LIQUIDATION_EXISTS"));
        var liquidationId = await liquidationAttempts.Single(a => a.IsSuccessStatusCode).Content.ReadFromJsonAsync<Guid>();
        foreach (var a in liquidationAttempts)
        {
            a.Dispose();
        }

        (await buyer.GetFromJsonAsync<LiquidationPreviewDto>($"/api/liquidations/preview?contractId={contractId}", JsonDefaults.Options))!.BlockingCode.ShouldBe("LIQUIDATION_EXISTS");

        using (var settle = await buyer.PostAsync(new Uri($"/api/liquidations/{liquidationId}/settlements", UriKind.Relative),
                   SettlementForm(valuation.Id, 1_000_000)))
        {
            settle.StatusCode.ShouldBe(HttpStatusCode.OK, await settle.Content.ReadAsStringAsync());
            var settled = (await settle.Content.ReadFromJsonAsync<LiquidationDto>(JsonDefaults.Options))!;
            settled.RemainingAmount.ShouldBe(0);
            settled.Settlements.Single().MarkedByName.ShouldNotBeNullOrWhiteSpace();
        }

        var liquidation = await buyer.GetFromJsonAsync<LiquidationDto>($"/api/liquidations/{liquidationId}", JsonDefaults.Options);
        (await buyer.PostAsync(new Uri($"/api/liquidations/{liquidationId}/submit", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await SignAllAsync(liquidation!.DocumentId!.Value, "rasoat", "ncc3", "giamdoc", "vanthu");

        var closed = await buyer.GetFromJsonAsync<ContractDto>($"/api/contracts/{contractId}", JsonDefaults.Options);
        closed!.Status.ShouldBe(ContractStatus.Liquidated);
        closed.Items.ShouldAllBe(i => i.ReceivedQuantity == 1 && i.AcceptedQuantity == 1 && i.PaidQuantity == 1);
        (await buyer.GetFromJsonAsync<ValuationDto>($"/api/valuations/{valuation.Id}", JsonDefaults.Options))!.PaymentStatus.ShouldBe(PaymentStatus.Paid);

        // Liquidated: new receipts for the contract are Invalid.
        var late = await PushReceiptAsync(contractNumber, first);
        (await buyer.GetFromJsonAsync<ReceiptDto>($"/api/warehouse-receipts/{late}", JsonDefaults.Options))!.Issues.Select(i => i.Code).ShouldContain("CONTRACT_CLOSED");

        // The supplier sees its own payment documents; another supplier does not.
        using var ncc3 = await factory.LoginAsync("ncc3");
        (await ncc3.GetAsync(new Uri($"/api/liquidations/{liquidationId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var ncc1 = await factory.LoginAsync("ncc1");
        (await ncc1.GetAsync(new Uri($"/api/valuations/{valuation.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ncc1.GetAsync(new Uri($"/api/acceptances/{acceptanceId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Liquidation_is_refused_without_signed_valuations()
    {
        using var buyer = await factory.LoginAsync("muasam");
        var (contractId, _) = await CreateEffectiveContractAsync(buyer);
        var preview = await buyer.GetFromJsonAsync<LiquidationPreviewDto>($"/api/liquidations/preview?contractId={contractId}", JsonDefaults.Options);
        preview!.BlockingCode.ShouldBe("LIQUIDATION_VALUATIONS_REQUIRED");
        using var refused = await buyer.PostAsJsonAsync("/api/liquidations", new SaveLiquidationRequest(contractId, null), JsonDefaults.Options);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Virtual_receipt_requires_an_effective_contract_without_warehouse_receipt()
    {
        using var buyer = await factory.LoginAsync("muasam");

        // HĐ bắt buộc nhập kho: phiếu ảo sẽ bỏ qua 5 chữ ký để đi thẳng tới nghiệm thu.
        var (stocked, _) = await CreateEffectiveContractAsync(buyer);
        var stockedItem = (await buyer.GetFromJsonAsync<ContractDto>($"/api/contracts/{stocked}", JsonDefaults.Options))!.Items[0];
        using (var refused = await buyer.PostAsJsonAsync("/api/warehouse-receipts", new SaveReceiptRequest(ReceiptType.Receipt, true, stocked, null, Today, null, null, null,
                   [new SaveReceiptLineRequest(stockedItem.Id, 1, null, null, null)]), JsonDefaults.Options))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await refused.Content.ReadAsStringAsync()).ShouldContain("RECEIPT_VIRTUAL_NOT_ALLOWED");
        }

        // HĐ dịch vụ không nhập kho nhưng chưa có hiệu lực (bản nháp): cũng không được xác nhận khối lượng.
        var (draft, item) = await CreateDraftServiceContractAsync(buyer);
        using var notEffective = await buyer.PostAsJsonAsync("/api/warehouse-receipts", new SaveReceiptRequest(ReceiptType.Receipt, true, draft, null, Today, null, null, null,
            [new SaveReceiptLineRequest(item, 1, null, null, null)]), JsonDefaults.Options);
        notEffective.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await notEffective.Content.ReadAsStringAsync()).ShouldContain("CONTRACT_NOT_EFFECTIVE");
    }

    private static async Task<(Guid Id, Guid ItemId)> CreateDraftServiceContractAsync(HttpClient buyer)
    {
        var categories = await buyer.GetFromJsonAsync<List<ItemCategoryDto>>("/api/items/categories", JsonDefaults.Options);
        var templates = await buyer.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates?kind=Contract", JsonDefaults.Options);
        var suppliers = await buyer.GetFromJsonAsync<List<LookupItem>>("/api/lookups/suppliers?search=Hồng Lam", JsonDefaults.Options);
        var code = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        using var created = await buyer.PostAsJsonAsync("/api/contracts", new SaveContractRequest(
            ProcurementType.NonBidding, null, suppliers!.First().Id, null, $"DV-{code}/2026/HĐ", "Bảo trì thang máy", ContractKind.LumpSum, false,
            categories!.Single(c => c.Code == "SUA_CHUA").Id, null, null, Today, Today, Today.AddMonths(3), "30 ngày", null, null, null, null, null, null, null,
            templates!.Single(t => t.Code == "HD_DICHVU_KHONGTHAU").Id,
            [new ContractLineRequest(null, new NewItemRequest($"DV-{code}-01", "Bảo trì thang máy tháng", "Lần", null, null, null, null, null), 3, 5_000_000, 8)]), JsonDefaults.Options);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var id = await created.Content.ReadFromJsonAsync<Guid>();
        return (id, (await buyer.GetFromJsonAsync<ContractDto>($"/api/contracts/{id}", JsonDefaults.Options))!.Items[0].Id);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    /// <summary>Hợp đồng sửa chữa ngoài thầu của NCC3 (2 hạng mục), ký đủ đến khi có hiệu lực.</summary>
    private async Task<(Guid Id, string Number)> CreateEffectiveContractAsync(HttpClient buyer)
    {
        var categories = await buyer.GetFromJsonAsync<List<ItemCategoryDto>>("/api/items/categories", JsonDefaults.Options);
        var templates = await buyer.GetFromJsonAsync<List<TemplateListItemDto>>("/api/templates?kind=Contract", JsonDefaults.Options);
        var suppliers = await buyer.GetFromJsonAsync<List<LookupItem>>("/api/lookups/suppliers?search=Hồng Lam", JsonDefaults.Options);
        var code = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var number = $"TT-{code}/2026/HĐ";
        using var created = await buyer.PostAsJsonAsync("/api/contracts", new SaveContractRequest(
            ProcurementType.NonBidding, null, suppliers!.First().Id, null, number, "Sửa chữa hệ thống điều hòa khu phẫu thuật", ContractKind.LumpSum, true,
            categories!.Single(c => c.Code == "SUA_CHUA").Id, null, null, Today.AddDays(-1), Today.AddDays(-1), Today.AddMonths(6), "30 ngày",
            "Thanh toán sau nghiệm thu", "Bảo hành 06 tháng", null, null, null, null, null, templates!.Single(t => t.Code == "HD_DICHVU_KHONGTHAU").Id,
            [
                new ContractLineRequest(null, new NewItemRequest($"SC-{code}-01", "Thay máy nén điều hòa 5HP", "Bộ", null, null, "Daikin", "Thái Lan", null), 1, 32_000_000, 10),
                new ContractLineRequest(null, new NewItemRequest($"SC-{code}-02", "Nhân công lắp đặt, nạp gas", "Gói", null, null, null, null, null), 1, 4_400_000, 10),
            ]), JsonDefaults.Options);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var id = await created.Content.ReadFromJsonAsync<Guid>();
        (await buyer.PostAsync(new Uri($"/api/contracts/{id}/submit", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var documentId = (await buyer.GetFromJsonAsync<ContractDto>($"/api/contracts/{id}", JsonDefaults.Options))!.DocumentId!.Value;
        await SignAllAsync(documentId, "rasoat");
        using (var supplier = await factory.LoginAsync("ncc3"))
        {
            using var agree = await supplier.PostAsJsonAsync($"/api/contracts/{id}/supplier-response", new SupplierResponseRequest(true, null), JsonDefaults.Options);
            agree.StatusCode.ShouldBe(HttpStatusCode.NoContent, await agree.Content.ReadAsStringAsync());
        }

        await SignAllAsync(documentId, "ncc3", "ketoan", "giamdoc", "vanthu");
        (await buyer.GetFromJsonAsync<ContractDto>($"/api/contracts/{id}", JsonDefaults.Options))!.Status.ShouldBe(ContractStatus.Effective);
        return (id, number);
    }

    private async Task<Guid> PushReceiptAsync(string contractNumber, ContractItemDto item)
    {
        var id = "PAY-" + Guid.NewGuid().ToString("N")[..12];
        var payload = new HisReceiptPayload(id, null, ReceiptType.Receipt, Today, "KTS01", null, "2901234567", null, contractNumber, null, "Kỹ thuật viên Hồng Lam",
            null, new HisInvoicePayload("1C26THL", Random.Shared.Next(1_000_000, 9_999_999).ToString(CultureInfo.InvariantCulture), Today, null, null, item.UnitPrice, "2901234567"),
            [new HisReceiptLinePayload(null, item.ItemCode, item.ItemName, item.Unit, null, null, 1, item.UnitPrice, item.VatRate)]);
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, JsonDefaults.Options);
        const string path = "/api/integration/his/warehouse-receipts";
        var timestamp = HisRequestSignature.Timestamp(DateTimeOffset.UtcNow);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Api-Key", ApiFactory.HisApiKey);
        request.Headers.Add("X-Timestamp", timestamp);
        request.Headers.Add("X-Signature", HisRequestSignature.Sign(ApiFactory.HisSecret, "POST", path, timestamp, body));
        using var anonymous = factory.CreateClient();
        using var response = await anonymous.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<HisIngestResultDto>(JsonDefaults.Options))!.ReceiptId;
    }

    private async Task SignAllAsync(Guid documentId, params string[] users)
    {
        foreach (var user in users)
        {
            using var client = await factory.LoginAsync(user);
            await ActAsync(client, documentId);
        }
    }

    /// <summary>Acts on the caller's active step: approve (rà soát) or sign.</summary>
    private static async Task ActAsync(HttpClient client, Guid documentId)
    {
        var wf = await client.GetFromJsonAsync<WorkflowDto>($"/api/workflows/by-document/{documentId}", JsonDefaults.Options);
        wf!.MyStepId.ShouldNotBeNull("the user should have the active step");
        var step = wf.Steps.Single(s => s.Id == wf.MyStepId);
        using var response = step.RequiresSignature
            ? await client.PostAsJsonAsync($"/api/signing/steps/{step.Id}/sign", new BeginSignRequest("MOCK", null, null), JsonDefaults.Options)
            : await client.PostAsJsonAsync($"/api/workflows/steps/{step.Id}/approve", new ApproveStepRequest("Đồng ý", false), JsonDefaults.Options);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static MultipartFormDataContent PaymentOrderForm(string number, decimal amount, bool withFile = false)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(number), "number" },
            { new StringContent(Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "date" },
            { new StringContent(amount.ToString(CultureInfo.InvariantCulture)), "amount" },
        };
        if (withFile)
        {
            var pdf = new ByteArrayContent("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray());
            pdf.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(pdf, "file", "unc.pdf");
        }

        return form;
    }

    private static MultipartFormDataContent SettlementForm(Guid valuationId, decimal amount) => new()
    {
        { new StringContent(valuationId.ToString()), "valuationId" },
        { new StringContent("Khoản chênh lệch được NCC xác nhận đã nhận đủ"), "description" },
        { new StringContent(amount.ToString(CultureInfo.InvariantCulture)), "amount" },
        { new StringContent("BBXN-01"), "voucherNumber" },
        { new StringContent(Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "voucherDate" },
    };
}
