using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BlueContract.Application.Integration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlueContract.Infrastructure.Integration;

public sealed class HisOptions
{
    public const string SectionName = "His";

    /// <summary>"Mock" (deterministic demo data) or "Rest" (vendor API described below).</summary>
    public string Provider { get; set; } = "Mock";

    public string DisplayName { get; set; } = "HIS giả lập";

    /// <summary>Base URL of the vendor API (string so an empty environment variable binds cleanly).</summary>
    public string? BaseUrl { get; set; }

    public string ApiKeyHeader { get; set; } = "X-Api-Key";

    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Relative URL with placeholders {bidNumber}, {taxCode}, {facilityCode}.</summary>
    public string ItemsPath { get; set; } = "api/bid-items?bidNumber={bidNumber}&taxCode={taxCode}&facility={facilityCode}";

    /// <summary>Relative URL with placeholders {warehouseCode}, {from}, {to}, {hisKeys} (comma separated).</summary>
    public string StockPath { get; set; } = "api/stock-movements?warehouse={warehouseCode}&from={from}&to={to}&keys={hisKeys}";

    /// <summary>Dotted path to the array inside the response ("" = the response itself is the array).</summary>
    public string ItemsArrayPath { get; set; } = "data";

    public string StockArrayPath { get; set; } = "data";

    /// <summary>Relative URL with placeholders {from}, {to} (yyyy-MM-dd) returning receipts in the portal schema (HIS_INTEGRATION.md §4).</summary>
    public string ReceiptsPath { get; set; } = "api/warehouse-receipts?from={from}&to={to}";

    public string ReceiptsArrayPath { get; set; } = "data";

    /// <summary>Periodic pull of warehouse receipts (alternative/complement to the push API).</summary>
    public bool PullEnabled { get; set; }

    /// <summary>Cron of the pull job (Hangfire, UTC). Default: every 15 minutes.</summary>
    public string PullCron { get; set; } = "*/15 * * * *";

    /// <summary>The pull asks for receipts of the last N days (idempotent, so overlaps are harmless).</summary>
    public int PullLookbackDays { get; set; } = 3;

    /// <summary>Our field → the vendor's JSON field name (dotted paths allowed).</summary>
    public Dictionary<string, string> ItemFields { get; set; } = new(StringComparer.Ordinal)
    {
        ["HisKey"] = "hisKey",
        ["Code"] = "code",
        ["Name"] = "name",
        ["ActiveIngredient"] = "activeIngredient",
        ["Strength"] = "strength",
        ["Specification"] = "specification",
        ["Unit"] = "unit",
        ["Manufacturer"] = "manufacturer",
        ["Country"] = "country",
        ["RegistrationNumber"] = "registrationNumber",
        ["BidGroup"] = "bidGroup",
        ["CategoryCode"] = "categoryCode",
        ["Quantity"] = "quantity",
        ["UnitPrice"] = "unitPrice",
    };

    public Dictionary<string, string> StockFields { get; set; } = new(StringComparer.Ordinal)
    {
        ["HisKey"] = "hisKey",
        ["Opening"] = "opening",
        ["Received"] = "received",
        ["Issued"] = "issued",
        ["Closing"] = "closing",
    };
}

/// <summary>
/// Deterministic HIS (spec §7.3 MockHisClient): the same (decision number, tax code) always yields the same items, so the
/// demo, E2E tests and HIS-sync screens behave like a real hospital system without one.
/// </summary>
internal sealed class MockHisClient(IOptions<HisOptions> options, IMockHisReceiptQueue queue) : IHisClient
{
    private sealed record Template(string Code, string Name, string? Ingredient, string? Strength, string? Spec, string Unit, string Maker, string Country,
        string? Reg, string? Group, string Category, decimal Price);

    private static readonly Template[] Drugs =
    [
        new("TH001", "Paracetamol 500mg", "Paracetamol", "500mg", "Hộp 10 vỉ x 10 viên", "Viên", "Công ty CP Dược Hậu Giang", "Việt Nam", "VD-12345-20", "N4", "THUOC", 450),
        new("TH002", "Amoxicillin 500mg", "Amoxicillin", "500mg", "Hộp 10 vỉ x 10 viên", "Viên", "Công ty CP Pymepharco", "Việt Nam", "VD-23456-21", "N4", "THUOC", 1_150),
        new("TH003", "Cefuroxim 500mg", "Cefuroxim", "500mg", "Hộp 2 vỉ x 5 viên", "Viên", "Công ty CP Dược phẩm Imexpharm", "Việt Nam", "VD-34567-22", "N3", "THUOC", 6_800),
        new("TH004", "Omeprazol 20mg", "Omeprazol", "20mg", "Hộp 3 vỉ x 10 viên", "Viên", "Công ty CP Dược phẩm OPV", "Việt Nam", "VD-45678-21", "N4", "THUOC", 520),
        new("TH005", "Metformin 850mg", "Metformin hydroclorid", "850mg", "Hộp 10 vỉ x 10 viên", "Viên", "Công ty CP Dược phẩm Agimexpharm", "Việt Nam", "VD-56789-22", "N4", "THUOC", 380),
        new("TH006", "Amlodipin 5mg", "Amlodipin", "5mg", "Hộp 3 vỉ x 10 viên", "Viên", "Công ty CP Dược phẩm Domesco", "Việt Nam", "VD-67890-20", "N4", "THUOC", 290),
        new("TH007", "Natri clorid 0,9% 500ml", "Natri clorid", "0,9%", "Chai 500ml", "Chai", "Công ty CP Fresenius Kabi Bidiphar", "Việt Nam", "VD-78901-21", "N2", "THUOC", 9_800),
        new("TH008", "Glucose 5% 500ml", "Glucose", "5%", "Chai 500ml", "Chai", "Công ty CP Fresenius Kabi Bidiphar", "Việt Nam", "VD-89012-21", "N2", "THUOC", 10_500),
        new("TH009", "Ceftriaxon 1g", "Ceftriaxon", "1g", "Hộp 10 lọ", "Lọ", "Công ty CP Dược phẩm Tenamyd", "Việt Nam", "VD-90123-22", "N2", "THUOC", 18_900),
        new("TH010", "Insulin Mixtard 30 100IU/ml", "Insulin người", "100IU/ml", "Hộp 1 lọ 10ml", "Lọ", "Novo Nordisk A/S", "Đan Mạch", "QLSP-0912-20", "N1", "THUOC", 98_000),
        new("TH011", "Atorvastatin 20mg", "Atorvastatin", "20mg", "Hộp 3 vỉ x 10 viên", "Viên", "Công ty TNHH Stellapharm", "Việt Nam", "VD-11223-22", "N3", "THUOC", 1_250),
        new("TH012", "Losartan 50mg", "Losartan kali", "50mg", "Hộp 3 vỉ x 10 viên", "Viên", "Công ty CP SPM", "Việt Nam", "VD-22334-21", "N4", "THUOC", 640),
        new("TH013", "Vitamin B1 100mg/ml", "Thiamin hydroclorid", "100mg/ml", "Hộp 100 ống 1ml", "Ống", "Công ty CP Dược phẩm Vĩnh Phúc", "Việt Nam", "VD-33445-20", "N4", "THUOC", 1_050),
        new("TH014", "Diclofenac 75mg/3ml", "Diclofenac natri", "75mg/3ml", "Hộp 10 ống", "Ống", "Công ty CP Dược phẩm Trung ương 1", "Việt Nam", "VD-44556-21", "N4", "THUOC", 2_300),
        new("TH015", "Salbutamol 2,5mg/2,5ml", "Salbutamol", "2,5mg/2,5ml", "Hộp 20 ống", "Ống", "GlaxoSmithKline", "Úc", "VN-55667-19", "N1", "THUOC", 4_700),
    ];

    private static readonly Template[] Supplies =
    [
        new("VT001", "Bơm tiêm 5ml", "Bơm tiêm nhựa vô trùng, kim 25G", null, "Hộp 100 cái", "Cái", "Công ty CP Nhựa y tế MPV", "Việt Nam", null, "N4", "VTYT", 850),
        new("VT002", "Bơm tiêm 10ml", "Bơm tiêm nhựa vô trùng, kim 23G", null, "Hộp 100 cái", "Cái", "Công ty CP Nhựa y tế MPV", "Việt Nam", null, "N4", "VTYT", 1_150),
        new("VT003", "Kim luồn tĩnh mạch 22G", "Kim luồn có cánh, có cổng tiêm thuốc", null, "Hộp 50 cái", "Cái", "B. Braun", "Malaysia", null, "N2", "VTYT", 14_500),
        new("VT004", "Dây truyền dịch", "Dây truyền dịch vô trùng có bầu đếm giọt", null, "Gói 1 bộ", "Bộ", "Công ty TNHH Tanaphar", "Việt Nam", null, "N4", "VTYT", 3_900),
        new("VT005", "Găng tay khám không bột size M", "Latex, không bột", null, "Hộp 100 chiếc", "Đôi", "Công ty TNHH Merufa", "Việt Nam", null, "N4", "VTYT", 1_350),
        new("VT006", "Găng tay phẫu thuật tiệt trùng size 7", "Latex, có bột, tiệt trùng", null, "Hộp 50 đôi", "Đôi", "Công ty TNHH Merufa", "Việt Nam", null, "N4", "VTYT", 6_200),
        new("VT007", "Bông y tế thấm nước 1kg", "Bông xơ 100% cotton", null, "Gói 1kg", "Gói", "Công ty CP Bông Bạch Tuyết", "Việt Nam", null, "N4", "VTYT", 165_000),
        new("VT008", "Gạc phẫu thuật 10x10cm", "Gạc 12 lớp, tiệt trùng", null, "Gói 10 miếng", "Gói", "Công ty CP Danameco", "Việt Nam", null, "N4", "VTYT", 7_800),
        new("VT009", "Chỉ khâu Nylon 3/0", "Chỉ không tiêu, kim tam giác 3/8", null, "Hộp 12 tép", "Tép", "Công ty CP CPT", "Việt Nam", null, "N3", "VTYT", 18_500),
        new("VT010", "Ống nội khí quản số 7.5", "PVC, có bóng chèn", null, "Hộp 10 cái", "Cái", "Medtronic", "Mexico", null, "N1", "VTYT", 42_000),
        new("HC001", "Hóa chất xét nghiệm Glucose", "Thuốc thử định lượng Glucose", null, "Hộp 4 x 50ml", "Hộp", "Roche Diagnostics", "Đức", "TBYT-2101-21", "N1", "HOA_CHAT", 2_650_000),
        new("HC002", "Hóa chất xét nghiệm Creatinine", "Thuốc thử định lượng Creatinine", null, "Hộp 4 x 50ml", "Hộp", "Roche Diagnostics", "Đức", "TBYT-2102-21", "N1", "HOA_CHAT", 2_980_000),
        new("SP001", "Test nhanh HBsAg", "Que thử phát hiện kháng nguyên HBsAg", null, "Hộp 50 test", "Test", "Abbott", "Hàn Quốc", "TBYT-3301-22", "N2", "SINH_PHAM", 18_000),
        new("SP002", "Test nhanh HIV 1/2", "Que thử phát hiện kháng thể HIV 1/2", null, "Hộp 100 test", "Test", "Abbott", "Hàn Quốc", "TBYT-3302-22", "N2", "SINH_PHAM", 25_000),
        new("SP003", "Ống nghiệm EDTA K2 2ml", "Ống chân không chống đông EDTA", null, "Khay 100 ống", "Ống", "Công ty CP Hồng Thiện Mỹ", "Việt Nam", null, "N4", "SINH_PHAM", 1_200),
    ];

    private static readonly Template[] General =
    [
        new("TS001", "Máy đo huyết áp điện tử", "Đo bắp tay, bộ nhớ 60 lần đo", null, "Hộp 1 máy", "Cái", "Omron", "Nhật Bản", "HEM-7121", null, "TAI_SAN", 1_150_000),
        new("TS002", "Giường bệnh nhân 2 tay quay", "Khung thép sơn tĩnh điện, có song chắn", null, "Bộ", "Cái", "Công ty TNHH Hồng Hà", "Việt Nam", "GB-02", null, "TAI_SAN", 9_800_000),
        new("TS003", "Máy tính để bàn", "Core i5, RAM 16GB, SSD 512GB, màn hình 23,8 inch", null, "Bộ", "Bộ", "Dell", "Trung Quốc", "OptiPlex 7010", null, "TAI_SAN", 17_500_000),
        new("HH001", "Giấy A4 Double A 80gsm", "Giấy in 80gsm", null, "Ram 500 tờ", "Ram", "Double A", "Thái Lan", null, null, "HANG_HOA", 92_000),
        new("HH002", "Mực in HP 107A", "Hộp mực laser", null, "Hộp", "Hộp", "HP", "Việt Nam", "W1107A", null, "HANG_HOA", 1_250_000),
        new("DV001", "Bảo trì hệ thống điều hòa trung tâm", "Vệ sinh, nạp gas, kiểm tra định kỳ hằng quý", null, null, "Lần", "—", "Việt Nam", null, null, "DICH_VU", 18_000_000),
        new("DV002", "Dịch vụ giặt là đồ vải y tế", "Giặt, sấy, khử khuẩn theo quy trình", null, null, "Kg", "—", "Việt Nam", null, null, "DICH_VU", 18_500),
        new("SC001", "Sửa chữa máy X-quang kỹ thuật số", "Thay bóng phát tia, hiệu chỉnh", null, null, "Gói", "—", "Việt Nam", null, null, "SUA_CHUA", 145_000_000),
        new("SC002", "Sửa chữa hệ thống khí y tế khoa HSTC", "Thay van, đồng hồ, kiểm tra rò rỉ", null, null, "Gói", "—", "Việt Nam", null, null, "SUA_CHUA", 68_000_000),
        new("SC003", "Sửa chữa thang máy bệnh viện số 2", "Thay cáp tải, bảo dưỡng tủ điều khiển", null, null, "Gói", "—", "Việt Nam", null, null, "SUA_CHUA", 92_000_000),
    ];

    public string DisplayName => options.Value.DisplayName;

    public Task<IReadOnlyList<HisItem>> GetItemsByBidDecisionAsync(string bidDecisionNumber, string supplierTaxCode, string? facilityCode, CancellationToken ct)
    {
        var pool = PoolFor(supplierTaxCode);
        var seed = Hash(bidDecisionNumber + "|" + supplierTaxCode);
        var count = Math.Min(pool.Length, 8 + (int)(seed % 8));
        var start = (int)(seed % (ulong)pool.Length);
        var items = Enumerable.Range(0, count).Select(i => pool[(start + i) % pool.Length]).Select(t =>
        {
            var h = Hash(bidDecisionNumber + "|" + supplierTaxCode + "|" + t.Code);
            var quantity = t.Price >= 1_000_000 ? 2 + (h % 10) : t.Price >= 50_000 ? 20 + (h % 200) : 1_000 * (5 + (h % 200));
            var priceFactor = 0.9m + (h % 21) / 100m; // ±10 % around the reference price
            return new HisItem($"K{h % 1_000_000:000000}", t.Code, t.Name, t.Ingredient, t.Strength, t.Spec, t.Unit, t.Maker, t.Country, t.Reg, t.Group,
                t.Category, quantity, decimal.Round(t.Price * priceFactor, 0, MidpointRounding.AwayFromZero));
        }).OrderBy(i => i.Code).ToList();
        return Task.FromResult<IReadOnlyList<HisItem>>(items);
    }

    public Task<IReadOnlyList<HisStockMovement>> GetStockMovementAsync(string warehouseCode, IReadOnlyCollection<string> hisKeys, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var period = $"{warehouseCode}|{from:yyyyMMdd}|{to:yyyyMMdd}";
        var rows = hisKeys.Distinct().Select(key =>
        {
            var h = Hash(period + "|" + key);
            decimal opening = h % 5_000;
            decimal received = (h >> 16) % 3_000;
            var issued = Math.Min(opening + received, (h >> 32) % 4_000);
            return new HisStockMovement(key, opening, received, issued, opening + received - issued);
        }).ToList();
        return Task.FromResult<IReadOnlyList<HisStockMovement>>(rows);
    }

    /// <summary>Receipts "lập trên HIS giả lập" (queued by the dev simulator in pull mode).</summary>
    public Task<IReadOnlyList<HisReceiptPayload>> GetWarehouseReceiptsAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<HisReceiptPayload>>([.. queue.Snapshot().Where(r => r.ReceiptDate >= from && r.ReceiptDate <= to)]);

    private static Template[] PoolFor(string taxCode) => taxCode switch
    {
        "0101234567" => Drugs,
        "0302345678" => Supplies,
        _ => (Hash(taxCode) % 3) switch { 0 => Drugs, 1 => Supplies, _ => General },
    };

    private static ulong Hash(string value) => BitConverter.ToUInt64(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0);
}

/// <summary>
/// Generic REST adapter (spec §7.3 RestHisClient): URLs and field names come from configuration so one implementation
/// fits several HIS vendors; the HTTP client has retry, circuit breaker and timeouts (Microsoft.Extensions.Http.Resilience).
/// </summary>
internal sealed class RestHisClient(IHttpClientFactory factory, IOptions<HisOptions> options, ILogger<RestHisClient> logger) : IHisClient
{
    public const string ClientName = "his";

    public string DisplayName => options.Value.DisplayName;

    public async Task<IReadOnlyList<HisItem>> GetItemsByBidDecisionAsync(string bidDecisionNumber, string supplierTaxCode, string? facilityCode, CancellationToken ct)
    {
        var o = options.Value;
        var url = Fill(o.ItemsPath, ("bidNumber", bidDecisionNumber), ("taxCode", supplierTaxCode), ("facilityCode", facilityCode ?? string.Empty));
        var rows = await GetArrayAsync(url, o.ItemsArrayPath, ct);
        var f = o.ItemFields;
        return [.. rows.Select(r => new HisItem(
            Text(r, f, "HisKey") ?? throw new HisUnavailableException("HIS trả về mặt hàng thiếu mã key."),
            Text(r, f, "Code") ?? Text(r, f, "HisKey")!,
            Text(r, f, "Name") ?? string.Empty,
            Text(r, f, "ActiveIngredient"),
            Text(r, f, "Strength"),
            Text(r, f, "Specification"),
            Text(r, f, "Unit") ?? string.Empty,
            Text(r, f, "Manufacturer"),
            Text(r, f, "Country"),
            Text(r, f, "RegistrationNumber"),
            Text(r, f, "BidGroup"),
            Text(r, f, "CategoryCode"),
            Number(r, f, "Quantity"),
            Number(r, f, "UnitPrice")))];
    }

    public async Task<IReadOnlyList<HisStockMovement>> GetStockMovementAsync(string warehouseCode, IReadOnlyCollection<string> hisKeys, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var o = options.Value;
        var url = Fill(o.StockPath, ("warehouseCode", warehouseCode), ("from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("hisKeys", string.Join(',', hisKeys)));
        var rows = await GetArrayAsync(url, o.StockArrayPath, ct);
        var f = o.StockFields;
        return [.. rows.Select(r => new HisStockMovement(Text(r, f, "HisKey") ?? string.Empty, Number(r, f, "Opening"), Number(r, f, "Received"),
            Number(r, f, "Issued"), Number(r, f, "Closing")))];
    }

    /// <summary>The vendor returns receipts in the portal's JSON schema (camelCase, enums as strings) — see HIS_INTEGRATION.md.</summary>
    public async Task<IReadOnlyList<HisReceiptPayload>> GetWarehouseReceiptsAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var o = options.Value;
        var url = Fill(o.ReceiptsPath, ("from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        var rows = await GetArrayAsync(url, o.ReceiptsArrayPath, ct);
        var result = new List<HisReceiptPayload>();
        foreach (var row in rows)
        {
            try
            {
                if (System.Text.Json.JsonSerializer.Deserialize<HisReceiptPayload>(row, PayloadJson) is { } payload)
                {
                    result.Add(payload);
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                logger.LogWarning(ex, "HIS returned a receipt that does not match the schema");
            }
        }

        return result;
    }

    private static readonly System.Text.Json.JsonSerializerOptions PayloadJson = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private async Task<IReadOnlyList<JsonNode>> GetArrayAsync(string url, string arrayPath, CancellationToken ct)
    {
        try
        {
            var client = factory.CreateClient(ClientName);
            using var response = await client.GetAsync(new Uri(url, UriKind.Relative), ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new HisUnavailableException($"HIS trả về mã lỗi {(int)response.StatusCode}.");
            }

            var root = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
            var node = string.IsNullOrEmpty(arrayPath) ? root : Navigate(root, arrayPath);
            return node is JsonArray array ? [.. array.Where(x => x is not null).Select(x => x!)] : [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
            or Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException)
        {
            logger.LogWarning(ex, "HIS call {Url} failed", url);
            throw new HisUnavailableException("Không kết nối được HIS.", ex);
        }
    }

    private static string Fill(string template, params (string Name, string Value)[] values) =>
        values.Aggregate(template.TrimStart('/'), (s, v) => s.Replace("{" + v.Name + "}", Uri.EscapeDataString(v.Value), StringComparison.Ordinal));

    private static JsonNode? Navigate(JsonNode? node, string path)
    {
        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            node = node is JsonObject o && o.TryGetPropertyValue(part, out var next) ? next : null;
        }

        return node;
    }

    private static string? Text(JsonNode row, Dictionary<string, string> fields, string key)
    {
        var node = fields.TryGetValue(key, out var path) ? Navigate(row, path) : null;
        var text = node?.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static decimal Number(JsonNode row, Dictionary<string, string> fields, string key)
    {
        var node = fields.TryGetValue(key, out var path) ? Navigate(row, path) : null;
        return node is JsonValue v && v.TryGetValue<decimal>(out var d) ? d
            : decimal.TryParse(node?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out d) ? d : 0;
    }
}
