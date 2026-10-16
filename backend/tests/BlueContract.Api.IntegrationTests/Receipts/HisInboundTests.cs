using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Common;
using BlueContract.Application.Integration;
using BlueContract.Application.Receipts;
using BlueContract.Application.Signing;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Integration;
using BlueContract.Domain.Receipts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Receipts;

/// <summary>
/// Spec §13: API HIS inbound (HMAC, idempotent), phiếu nhập kho 5 chữ ký nối tiếp, validation engine chặn ký,
/// NCC bổ sung hóa đơn XML, nhật ký tích hợp + gửi lại, giả lập push/pull.
/// </summary>
[Collection(ApiCollection.Name)]
public class HisInboundTests(ApiFactory factory)
{
    private const string ReceiptsPath = "/api/integration/his/warehouse-receipts";
    private static readonly string[] Signers = ["thukho", "ncc2", "ketoankho", "truongkhoa", "ketoantruong"];

    [Fact]
    public async Task Push_requires_api_key_hmac_fresh_timestamp_and_is_idempotent()
    {
        var payload = await ValidPayloadAsync(1);
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, JsonDefaults.Options);
        using var anonymous = factory.CreateClient();

        (await CodeAsync(await anonymous.PostAsync(ReceiptsPath, Json(body)))).ShouldBe("HIS_AUTH_API_KEY_INVALID");
        (await CodeAsync(await SendAsync(anonymous, HttpMethod.Post, ReceiptsPath, body, signature: new string('0', 64)))).ShouldBe("HIS_AUTH_SIGNATURE_INVALID");
        (await CodeAsync(await SendAsync(anonymous, HttpMethod.Post, ReceiptsPath, body, at: DateTimeOffset.UtcNow.AddMinutes(-6)))).ShouldBe("HIS_AUTH_TIMESTAMP_INVALID");
        (await CodeAsync(await SendAsync(anonymous, HttpMethod.Post, ReceiptsPath, body, secret: "wrong-secret"))).ShouldBe("HIS_AUTH_SIGNATURE_INVALID");

        var at = DateTimeOffset.UtcNow;
        using var created = await SendAsync(anonymous, HttpMethod.Post, ReceiptsPath, body, at: at);
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var first = (await created.Content.ReadFromJsonAsync<HisIngestResultDto>(JsonDefaults.Options))!;
        first.Outcome.ShouldBe(HisIngestOutcome.Created);
        first.ValidationStatus.ShouldBe(ReceiptValidationStatus.Valid, string.Join("; ", first.Issues.Select(i => i.Message)));
        first.Status.ShouldBe(ReceiptStatus.Signing);
        first.ContractNumber.ShouldNotBeNull();

        // The very same signed request again is a replay; a fresh signature of the same content is a duplicate (no-op).
        (await CodeAsync(await SendAsync(anonymous, HttpMethod.Post, ReceiptsPath, body, at: at))).ShouldBe("HIS_AUTH_REPLAY");
        using var again = await SendAsync(anonymous, HttpMethod.Post, ReceiptsPath, body, at: at.AddSeconds(1));
        var duplicate = (await again.Content.ReadFromJsonAsync<HisIngestResultDto>(JsonDefaults.Options))!;
        duplicate.Outcome.ShouldBe(HisIngestOutcome.Duplicate);
        duplicate.ReceiptId.ShouldBe(first.ReceiptId);

        // Every call is in the integration log, rejected ones included.
        using var admin = await factory.LoginAsync("admin");
        var logs = await admin.GetFromJsonAsync<PagedResult<IntegrationLogListItemDto>>($"/api/admin/integration/logs?search={payload.HisReceiptId}&pageSize=50", JsonDefaults.Options);
        logs!.Items.Select(l => l.Status).ShouldContain(IntegrationLogStatus.Rejected);
        logs.Items.Select(l => l.Status).ShouldContain(IntegrationLogStatus.Duplicate);
        var success = logs.Items.Single(l => l.Status == IntegrationLogStatus.Success);
        success.ReceiptId.ShouldBe(first.ReceiptId);
        success.ClientName.ShouldBe("HIS-test");
        var detail = await admin.GetFromJsonAsync<IntegrationLogDto>($"/api/admin/integration/logs/{success.Id}", JsonDefaults.Options);
        detail!.RequestBody!.ShouldContain(payload.HisReceiptId);
        detail.ResponseBody!.ShouldContain(first.ReceiptId.ToString());
    }

    [Fact]
    public async Task Five_signatures_complete_the_receipt_and_update_the_contract()
    {
        var payload = await ValidPayloadAsync(2);
        var line = payload.Lines[0];
        var before = await ContractItemAsync(payload.ContractNumber!, line.ItemCode);
        var result = await PushAsync(payload);
        result.ValidationStatus.ShouldBe(ReceiptValidationStatus.Valid, string.Join("; ", result.Issues.Select(i => i.Message)));
        (await ContractItemAsync(payload.ContractNumber!, line.ItemCode)).ReceivedQuantity.ShouldBe(before.ReceivedQuantity + line.Quantity);

        using var buyer = await factory.LoginAsync("muasam");
        var receipt = await GetReceiptAsync(buyer, result.ReceiptId);
        foreach (var signer in Signers)
        {
            using var client = await factory.LoginAsync(signer);
            await SignAsync(client, receipt.DocumentId!.Value);
        }

        receipt = await GetReceiptAsync(buyer, result.ReceiptId);
        receipt.Status.ShouldBe(ReceiptStatus.Completed);
        var wf = await buyer.GetFromJsonAsync<WorkflowDto>($"/api/workflows/by-document/{receipt.DocumentId}", JsonDefaults.Options);
        wf!.Signatures.Count.ShouldBe(5);
        wf.StartedByName.ShouldBe("Hệ thống HIS (tích hợp)");

        // A signed receipt can no longer be changed from the HIS (→ conflict, logged as failed, retry keeps failing).
        using var anonymous = factory.CreateClient();
        var changed = payload with { Note = "Sửa sau khi ký" };
        using var conflict = await SendAsync(anonymous, HttpMethod.Post, ReceiptsPath, JsonSerializer.SerializeToUtf8Bytes(changed, JsonDefaults.Options));
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(conflict)).ShouldBe("RECEIPT_LOCKED_SIGNED");

        using var admin = await factory.LoginAsync("admin");
        var logs = await admin.GetFromJsonAsync<PagedResult<IntegrationLogListItemDto>>($"/api/admin/integration/logs?search={payload.HisReceiptId}&status=Failed", JsonDefaults.Options);
        var failed = logs!.Items.Single();
        failed.CanRetry.ShouldBeTrue();
        using var retry = await admin.PostAsync(new Uri($"/api/admin/integration/logs/{failed.Id}/retry", UriKind.Relative), null);
        (await CodeAsync(retry)).ShouldBe("RECEIPT_LOCKED_SIGNED");
        (await admin.GetFromJsonAsync<IntegrationLogDto>($"/api/admin/integration/logs/{failed.Id}", JsonDefaults.Options))!.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task Invalid_receipts_cannot_be_signed_and_warnings_need_a_reason_and_an_invoice()
    {
        // Invalid: price above the contract.
        var valid = await ValidPayloadAsync(1);
        var invalid = await PushAsync(valid with
        {
            HisReceiptId = valid.HisReceiptId + "-INV",
            Lines = [valid.Lines[0] with { UnitPrice = valid.Lines[0].UnitPrice + 1 }],
            Invoice = null,
        });
        invalid.ValidationStatus.ShouldBe(ReceiptValidationStatus.Invalid);
        invalid.Issues.Select(i => i.Code).ShouldContain("UNIT_PRICE_ABOVE_CONTRACT");
        using var storekeeper = await factory.LoginAsync("thukho");
        var invalidReceipt = await GetReceiptAsync(storekeeper, invalid.ReceiptId);
        var gate = (await storekeeper.GetFromJsonAsync<WorkflowDto>($"/api/workflows/by-document/{invalidReceipt.DocumentId}", JsonDefaults.Options))!.Gate;
        gate.ShouldNotBeNull();
        gate.Blocked.ShouldBeTrue();
        (await CodeAsync(await BeginSignAsync(storekeeper, invalidReceipt.DocumentId!.Value, null))).ShouldBe("RECEIPT_INVALID_BLOCKS_SIGNING");

        // Warning: price below the contract, no invoice yet.
        var warning = await PushAsync(valid with
        {
            HisReceiptId = valid.HisReceiptId + "-WARN",
            Lines = [valid.Lines[0] with { UnitPrice = valid.Lines[0].UnitPrice - 1 }],
            Invoice = null,
        });
        warning.ValidationStatus.ShouldBe(ReceiptValidationStatus.Warning);
        var receipt = await GetReceiptAsync(storekeeper, warning.ReceiptId);
        (await CodeAsync(await BeginSignAsync(storekeeper, receipt.DocumentId!.Value, null))).ShouldBe("RECEIPT_WARNING_REASON_REQUIRED");
        (await BeginSignAsync(storekeeper, receipt.DocumentId!.Value, "Đơn giá thấp hơn theo thông báo giảm giá của NCC")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Deliverer (NCC) step: the invoice is mandatory; the supplier uploads the e-invoice XML.
        using var supplier = await factory.LoginAsync("ncc2");
        (await CodeAsync(await BeginSignAsync(supplier, receipt.DocumentId!.Value, "Đồng ý"))).ShouldBe("RECEIPT_INVOICE_REQUIRED");
        var atSupplier = await GetReceiptAsync(supplier, warning.ReceiptId);
        atSupplier.CanEditInvoice.ShouldBeTrue();
        using var form = new MultipartFormDataContent();
        var xml = new ByteArrayContent(Encoding.UTF8.GetBytes(InvoiceXml(atSupplier.TotalAmount, "0302345678", "C26TBB", RandomNumber())));
        xml.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        form.Add(xml, "file", "hoa-don.xml");
        using var uploaded = await supplier.PostAsync(new Uri($"/api/warehouse-receipts/{warning.ReceiptId}/invoice-file", UriKind.Relative), form);
        uploaded.StatusCode.ShouldBe(HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync());
        var withInvoice = (await uploaded.Content.ReadFromJsonAsync<ReceiptDto>(JsonDefaults.Options))!;
        withInvoice.Invoice.Symbol.ShouldBe("1C26TBB");
        withInvoice.Invoice.HasXml.ShouldBeTrue();
        withInvoice.Issues.Select(i => i.Code).ShouldNotContain("INVOICE_MISSING");
        (await BeginSignAsync(supplier, receipt.DocumentId!.Value, "Giá theo thông báo giảm giá")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Supplier isolation: another supplier cannot read it, even with the id.
        using var other = await factory.LoginAsync("ncc1");
        (await other.GetAsync(new Uri($"/api/warehouse-receipts/{warning.ReceiptId}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var otherList = await other.GetFromJsonAsync<PagedResult<ReceiptListItemDto>>("/api/warehouse-receipts?pageSize=200", JsonDefaults.Options);
        otherList!.Items.ShouldAllBe(r => r.SupplierId != atSupplier.SupplierId);
    }

    [Fact]
    public async Task Changed_payload_updates_an_unsigned_receipt()
    {
        var payload = await ValidPayloadAsync(1);
        var created = await PushAsync(payload);
        var updated = await PushAsync(payload with { DelivererName = "Người giao mới", Lines = [payload.Lines[0] with { LotNumber = "L-NEW" }] });
        updated.Outcome.ShouldBe(HisIngestOutcome.Updated);
        updated.ReceiptId.ShouldBe(created.ReceiptId);
        using var buyer = await factory.LoginAsync("muasam");
        var receipt = await GetReceiptAsync(buyer, created.ReceiptId);
        receipt.DelivererName.ShouldBe("Người giao mới");
        receipt.Lines.Single().LotNumber.ShouldBe("L-NEW");
        receipt.Status.ShouldBe(ReceiptStatus.Signing);

        using var anonymous = factory.CreateClient();
        using var typeChange = await SendAsync(anonymous, HttpMethod.Post, ReceiptsPath,
            JsonSerializer.SerializeToUtf8Bytes(payload with { Type = ReceiptType.Return }, JsonDefaults.Options));
        (await CodeAsync(typeChange)).ShouldBe("RECEIPT_TYPE_LOCKED");
    }

    [Fact]
    public async Task Read_endpoints_serve_contracts_summaries_and_items_to_the_his()
    {
        using var anonymous = factory.CreateClient();
        using var contracts = await SendAsync(anonymous, HttpMethod.Get, "/api/integration/his/contracts?supplierTaxCode=0302345678", []);
        contracts.StatusCode.ShouldBe(HttpStatusCode.OK, await contracts.Content.ReadAsStringAsync());
        var list = (await contracts.Content.ReadFromJsonAsync<List<HisContractDto>>(JsonDefaults.Options))!;
        list.ShouldNotBeEmpty();
        list.ShouldAllBe(c => c.SupplierTaxCode == "0302345678");
        list[0].Items.ShouldAllBe(i => i.RemainingQuantity == i.Quantity - i.ReceivedQuantity);

        using var summaries = await SendAsync(anonymous, HttpMethod.Get, "/api/integration/his/proposal-summaries?status=Approved", []);
        (await summaries.Content.ReadFromJsonAsync<List<HisSummaryDto>>(JsonDefaults.Options))!.ShouldAllBe(s => s.Status == Domain.Proposals.ProposalSummaryStatus.Approved);

        using var items = await SendAsync(anonymous, HttpMethod.Get, "/api/integration/his/items?updatedSince=2020-01-01T00:00:00Z", []);
        (await items.Content.ReadFromJsonAsync<List<HisItemDto>>(JsonDefaults.Options))!.ShouldContain(i => i.HisKey != null);
    }

    [Fact]
    public async Task Simulator_pushes_and_queues_receipts_for_the_pull_job()
    {
        using var buyer = await factory.LoginAsync("muasam");
        using var pushed = await buyer.PostAsJsonAsync("/api/dev/his/simulate-receipt",
            new SimulateReceiptRequest(null, ReceiptType.Receipt, SimulationScenario.Invalid, SimulationMode.Push, true, 1), JsonDefaults.Options);
        pushed.StatusCode.ShouldBe(HttpStatusCode.OK, await pushed.Content.ReadAsStringAsync());
        var push = (await pushed.Content.ReadFromJsonAsync<SimulateReceiptResultDto>(JsonDefaults.Options))!;
        push.Result!.ValidationStatus.ShouldBe(ReceiptValidationStatus.Invalid);
        push.Result.Issues.Select(i => i.Code).ShouldContain("ITEM_NOT_IN_CONTRACT");

        using var queued = await buyer.PostAsJsonAsync("/api/dev/his/simulate-receipt",
            new SimulateReceiptRequest(null, ReceiptType.Receipt, SimulationScenario.Valid, SimulationMode.Pull, true, 1), JsonDefaults.Options);
        var queue = (await queued.Content.ReadFromJsonAsync<SimulateReceiptResultDto>(JsonDefaults.Options))!;
        queue.Queued.ShouldBeTrue();

        using var admin = await factory.LoginAsync("admin");
        using var pull = await admin.PostAsync(new Uri("/api/admin/integration/his/pull", UriKind.Relative), null);
        var pulled = (await pull.Content.ReadFromJsonAsync<PullResultDto>(JsonDefaults.Options))!;
        pulled.Received.ShouldBeGreaterThanOrEqualTo(1);
        pulled.Failed.ShouldBe(0, string.Join("; ", pulled.Errors));
        var list = await admin.GetFromJsonAsync<PagedResult<ReceiptListItemDto>>($"/api/warehouse-receipts?search={queue.Payload.ReceiptNumber}", JsonDefaults.Options);
        list!.Items.ShouldContain(r => r.Number == queue.Payload.ReceiptNumber && r.Source == ReceiptSource.His);

        // Pulling again only finds duplicates.
        using var again = await admin.PostAsync(new Uri("/api/admin/integration/his/pull", UriKind.Relative), null);
        (await again.Content.ReadFromJsonAsync<PullResultDto>(JsonDefaults.Options))!.Created.ShouldBe(0);
    }

    // ---------------- helpers ----------------

    /// <summary>A valid receipt for the effective VTYT contract: an item still called but not yet received, invoice matching.</summary>
    /// <summary>
    /// AUDIT-1 §10.10: X-Forwarded-For is believed only from a configured proxy — a caller cannot spoof the HIS's IP to pass
    /// the whitelist (nor dodge the per-IP login rate limit).
    /// </summary>
    [Fact]
    public async Task Forwarded_for_from_an_untrusted_caller_does_not_pass_the_ip_whitelist()
    {
        // The caller connects from 203.0.113.5 (not a proxy) and claims to be the whitelisted HIS at 10.9.9.9.
        await using var direct = factory.WithWebHostBuilder(b => b
            .UseSetting("HisInbound:AllowedIps:0", "10.9.9.9")
            .ConfigureTestServices(s => s.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter("203.0.113.5"))));
        using (var client = direct.CreateClient())
        {
            (await CodeAsync(await client.SendAsync(SignedContractsRequest("10.9.9.9")))).ShouldBe("HIS_AUTH_IP_NOT_ALLOWED");
        }

        // Through a configured proxy at that address the forwarded client IP is used.
        await using var proxied = direct.WithWebHostBuilder(b => b.UseSetting("ForwardedHeaders:KnownProxies:0", "203.0.113.5"));
        using var viaProxy = proxied.CreateClient();
        using var allowed = await viaProxy.SendAsync(SignedContractsRequest("10.9.9.9"));
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
    }

    private static HttpRequestMessage SignedContractsRequest(string forwardedFor)
    {
        const string path = "/api/integration/his/contracts?supplierTaxCode=0302345678";
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        var timestamp = HisRequestSignature.Timestamp(DateTimeOffset.UtcNow);
        request.Headers.Add("X-Api-Key", ApiFactory.HisApiKey);
        request.Headers.Add("X-Timestamp", timestamp);
        request.Headers.Add("X-Signature", HisRequestSignature.Sign(ApiFactory.HisSecret, "GET", path, timestamp, []));
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return request;
    }

    /// <summary>Gives test requests a real remote address (the test server has none of its own).</summary>
    private sealed class RemoteIpStartupFilter(string ip) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    /// <summary>
    /// AUDIT-1 §10.9: a signed request is accepted once — not once per API instance, and not once per thread when the same
    /// request arrives several times at the same moment.
    /// </summary>
    [Fact]
    public async Task Replay_is_refused_across_instances_and_under_parallel_arrival()
    {
        // Second API instance: its own process memory, the same database.
        await using var instanceB = factory.WithWebHostBuilder(_ => { });
        using var clientA = factory.CreateClient();
        using var clientB = instanceB.CreateClient();

        var body = JsonSerializer.SerializeToUtf8Bytes(await ValidPayloadAsync(1), JsonDefaults.Options);
        var at = DateTimeOffset.UtcNow;
        using (var first = await SendAsync(clientA, HttpMethod.Post, ReceiptsPath, body, at: at))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        }

        (await CodeAsync(await SendAsync(clientB, HttpMethod.Post, ReceiptsPath, body, at: at))).ShouldBe("HIS_AUTH_REPLAY");

        var parallelBody = JsonSerializer.SerializeToUtf8Bytes(await ValidPayloadAsync(1), JsonDefaults.Options);
        var parallelAt = DateTimeOffset.UtcNow;
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
            SendAsync(i % 2 == 0 ? clientA : clientB, HttpMethod.Post, ReceiptsPath, parallelBody, at: parallelAt)));
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
        responses.Count(r => r.IsSuccessStatusCode).ShouldBe(1, string.Join(" | ", bodies));
        bodies.Where((_, i) => !responses[i].IsSuccessStatusCode).ShouldAllBe(b => b.Contains("HIS_AUTH_REPLAY"));
        foreach (var r in responses)
        {
            r.Dispose();
        }
    }

    /// <summary>AUDIT-1 §10.6: HIS retries racing each other create the receipt once and never answer 500.</summary>
    [Fact]
    public async Task Parallel_pushes_of_one_receipt_create_it_once()
    {
        var payload = await ValidPayloadAsync(1);
        using var anonymous = factory.CreateClient();
        // Different notes: identical bodies in the same second would be refused as replays before reaching the service.
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(i =>
            SendAsync(anonymous, HttpMethod.Post, ReceiptsPath, JsonSerializer.SerializeToUtf8Bytes(payload with { Note = $"Lượt {i}" }, JsonDefaults.Options))));
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK, string.Join(" | ", bodies));
        var results = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<HisIngestResultDto>(JsonDefaults.Options)));
        results.Count(r => r!.Outcome == HisIngestOutcome.Created).ShouldBe(1);
        results.Select(r => r!.ReceiptId).Distinct().ShouldHaveSingleItem();
        foreach (var r in responses)
        {
            r.Dispose();
        }
    }

    /// <summary>AUDIT-1 §10.6: three receipts carrying the same invoice at the same time — exactly one keeps it.</summary>
    [Fact]
    public async Task Parallel_receipts_sharing_an_invoice_accept_only_one()
    {
        var first = await ValidPayloadAsync(1);
        var payloads = Enumerable.Range(0, 3).Select(i =>
        {
            var id = "IT-" + Guid.NewGuid().ToString("N")[..12];
            return first with { HisReceiptId = id, ReceiptNumber = "PN-" + id[3..] };
        }).ToList();
        using var anonymous = factory.CreateClient();
        var responses = await Task.WhenAll(payloads.Select(p =>
            SendAsync(anonymous, HttpMethod.Post, ReceiptsPath, JsonSerializer.SerializeToUtf8Bytes(p, JsonDefaults.Options))));
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK, string.Join(" | ", bodies));
        var results = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<HisIngestResultDto>(JsonDefaults.Options)));
        results.Count(r => r!.ValidationStatus != ReceiptValidationStatus.Invalid).ShouldBe(1, string.Join(" | ", bodies));

        using var storekeeper = await factory.LoginAsync("thukho");
        foreach (var rejected in results.Where(r => r!.ValidationStatus == ReceiptValidationStatus.Invalid))
        {
            (await GetReceiptAsync(storekeeper, rejected!.ReceiptId)).Issues.ShouldContain(i => i.Code == "INVOICE_DUPLICATE");
        }

        foreach (var r in responses)
        {
            r.Dispose();
        }
    }

    private async Task<HisReceiptPayload> ValidPayloadAsync(decimal quantity)
    {
        using var anonymous = factory.CreateClient();
        using var response = await SendAsync(anonymous, HttpMethod.Get, "/api/integration/his/contracts?supplierTaxCode=0302345678", []);
        var contract = (await response.Content.ReadFromJsonAsync<List<HisContractDto>>(JsonDefaults.Options))!.First();
        var item = contract.Items.Where(i => i.HisKey != null && i.CalledQuantity - i.ReceivedQuantity >= quantity).OrderByDescending(i => i.CalledQuantity - i.ReceivedQuantity).First();
        var total = decimal.Round(quantity * item.UnitPrice, 0, MidpointRounding.AwayFromZero);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var id = "IT-" + Guid.NewGuid().ToString("N")[..12];
        return new HisReceiptPayload(id, "PN-" + id[3..], ReceiptType.Receipt, today, "KV01", contract.BidDecisionNumber, contract.SupplierTaxCode, contract.SupplierName,
            contract.ContractNumber, null, "Người giao hàng thử", null, new HisInvoicePayload("1C26TAA", RandomNumber(), today, null, null, total, contract.SupplierTaxCode),
            [new HisReceiptLinePayload(item.HisKey, item.ItemCode, item.ItemName, item.Unit, "L-IT", today.AddYears(2), quantity, item.UnitPrice, item.VatRate)]);
    }

    private async Task<HisIngestResultDto> PushAsync(HisReceiptPayload payload)
    {
        using var anonymous = factory.CreateClient();
        using var response = await SendAsync(anonymous, HttpMethod.Post, ReceiptsPath, JsonSerializer.SerializeToUtf8Bytes(payload, JsonDefaults.Options));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<HisIngestResultDto>(JsonDefaults.Options))!;
    }

    private async Task<HisContractItemDto> ContractItemAsync(string contractNumber, string itemCode)
    {
        using var anonymous = factory.CreateClient();
        using var response = await SendAsync(anonymous, HttpMethod.Get, "/api/integration/his/contracts?supplierTaxCode=0302345678", []);
        return (await response.Content.ReadFromJsonAsync<List<HisContractDto>>(JsonDefaults.Options))!.Single(c => c.ContractNumber == contractNumber).Items.Single(i => i.ItemCode == itemCode);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string pathAndQuery, byte[] body, DateTimeOffset? at = null,
        string? secret = null, string? signature = null)
    {
        var timestamp = HisRequestSignature.Timestamp(at ?? DateTimeOffset.UtcNow);
        var request = new HttpRequestMessage(method, new Uri(pathAndQuery, UriKind.Relative));
        if (method != HttpMethod.Get)
        {
            request.Content = Json(body);
        }

        request.Headers.Add("X-Api-Key", ApiFactory.HisApiKey);
        request.Headers.Add("X-Timestamp", timestamp);
        request.Headers.Add("X-Signature", signature ?? HisRequestSignature.Sign(secret ?? ApiFactory.HisSecret, method.Method, pathAndQuery, timestamp, body));
        return client.SendAsync(request);
    }

    private static ByteArrayContent Json(byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.ShouldBeFalse(text);
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : text;
        }
    }

    private static async Task<ReceiptDto> GetReceiptAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<ReceiptDto>($"/api/warehouse-receipts/{id}", JsonDefaults.Options))!;

    private static async Task<HttpResponseMessage> BeginSignAsync(HttpClient client, Guid documentId, string? comment)
    {
        var wf = await client.GetFromJsonAsync<WorkflowDto>($"/api/workflows/by-document/{documentId}", JsonDefaults.Options);
        wf!.MyStepId.ShouldNotBeNull("the user should have the active step");
        return await client.PostAsJsonAsync($"/api/signing/steps/{wf.MyStepId}/sign", new BeginSignRequest("MOCK", null, comment), JsonDefaults.Options);
    }

    private static async Task SignAsync(HttpClient client, Guid documentId)
    {
        using var response = await BeginSignAsync(client, documentId, null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static string RandomNumber() => Random.Shared.Next(1_000_000, 9_999_999).ToString(CultureInfo.InvariantCulture);

    private static string InvoiceXml(decimal total, string sellerTaxCode, string symbol, string number) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <HDon><DLHDon><TTChung><KHMSHDon>1</KHMSHDon><KHHDon>{symbol}</KHHDon><SHDon>{number}</SHDon><NLap>{DateTime.UtcNow:yyyy-MM-dd}</NLap></TTChung>
        <NDHDon><NBan><Ten>NCC</Ten><MST>{sellerTaxCode}</MST></NBan><NMua><Ten>BV</Ten></NMua>
        <TToan><TgTCThue>{total.ToString(CultureInfo.InvariantCulture)}</TgTCThue><TgTThue>0</TgTThue><TgTTTBSo>{total.ToString(CultureInfo.InvariantCulture)}</TgTTTBSo></TToan></NDHDon></DLHDon></HDon>
        """;
}
