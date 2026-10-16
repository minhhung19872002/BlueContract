using BlueContract.Api.Auth;
using BlueContract.Api.Integration;
using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Integration;
using BlueContract.Application.Receipts;
using BlueContract.Domain.Proposals;
using BlueContract.Domain.Receipts;
using BlueContract.Infrastructure.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using P = BlueContract.Domain.Identity.Permissions;

namespace BlueContract.Api.Controllers;

/// <summary>Phiếu nhập kho / hoàn trả (spec §6.4, §9.1-10; cổng NCC §9.2-5).</summary>
[Route("api/warehouse-receipts")]
public sealed class WarehouseReceiptsController(ReceiptService service, IExcelExporter excel) : ApiControllerBase
{
    private const long UploadLimit = FileInspector.MaxUploadBytes + (1024 * 1024);

    [HttpGet]
    [HasPermission(P.ReceiptView)]
    public Task<PagedResult<ReceiptListItemDto>> List([FromQuery] PageQuery query, [FromQuery] ReceiptFilter filter, CancellationToken ct) =>
        service.ListAsync(query, filter, ct);

    [HttpGet("export")]
    [HasPermission(P.ReceiptView)]
    public async Task<FileContentResult> Export([FromQuery] PageQuery query, [FromQuery] ReceiptFilter filter, CancellationToken ct)
    {
        var rows = await service.ListAsync(query with { Page = 1, PageSize = PageQuery.MaxExportRows, ForExport = true }, filter, ct);
        var bytes = excel.Export("Phiếu nhập kho", [
            new ExcelColumn<ReceiptListItemDto>("Số phiếu", r => r.Number, Width: 18),
            new ExcelColumn<ReceiptListItemDto>("Loại", r => r.Type == ReceiptType.Return ? "Hoàn trả" : "Nhập kho", Width: 12),
            new ExcelColumn<ReceiptListItemDto>("Nguồn", r => r.Source switch { ReceiptSource.His => "HIS", ReceiptSource.Portal => "Cổng", _ => "Phiếu ảo" }, Width: 10),
            new ExcelColumn<ReceiptListItemDto>("Ngày", r => r.ReceiptDate, ExcelColumnFormat.Date, 12),
            new ExcelColumn<ReceiptListItemDto>("Kho", r => r.WarehouseName, Width: 24),
            new ExcelColumn<ReceiptListItemDto>("Nhà cung cấp", r => r.SupplierName, Width: 36),
            new ExcelColumn<ReceiptListItemDto>("Số HĐ", r => r.ContractNumber, Width: 20),
            new ExcelColumn<ReceiptListItemDto>("Số hóa đơn", r => r.InvoiceNumber, Width: 14),
            new ExcelColumn<ReceiptListItemDto>("Giá trị (₫)", r => r.TotalAmount, ExcelColumnFormat.Money, 18),
            new ExcelColumn<ReceiptListItemDto>("Trạng thái ký", r => StatusText(r.Status), Width: 16),
            new ExcelColumn<ReceiptListItemDto>("Kiểm tra", r => r.ValidationStatus switch { ReceiptValidationStatus.Valid => "Hợp lệ", ReceiptValidationStatus.Warning => "Cảnh báo", _ => "Không hợp lệ" }, Width: 14),
            new ExcelColumn<ReceiptListItemDto>("Số cảnh báo", r => r.IssueCount, ExcelColumnFormat.Number, 12),
        ], rows.Items);
        return Excel(bytes, "phieu-nhap-kho.xlsx");
    }

    private static string StatusText(ReceiptStatus s) => s switch
    {
        ReceiptStatus.Draft => "Chưa gửi ký",
        ReceiptStatus.Signing => "Đang ký",
        ReceiptStatus.Returned => "Bị trả lại",
        ReceiptStatus.Completed => "Đã ký đủ",
        _ => "Đã hủy",
    };

    [HttpGet("{id:guid}")]
    [HasPermission(P.ReceiptView)]
    public async Task<ActionResult<ReceiptDto>> Get(Guid id, CancellationToken ct) => FromResult(await service.GetAsync(id, ct));

    /// <summary>Lập phiếu trên cổng / phiếu ảo (xác nhận khối lượng, không qua kho).</summary>
    [HttpPost]
    [HasPermission(P.ReceiptManage)]
    public async Task<ActionResult<Guid>> Create(SaveReceiptRequest request, CancellationToken ct) => FromResult(await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(P.ReceiptManage)]
    public async Task<IActionResult> Update(Guid id, SaveReceiptRequest request, CancellationToken ct) => FromResult(await service.UpdateAsync(id, request, ct));

    /// <summary>Gửi ký (phiếu cổng) / Gửi ký lại (sau khi bị trả lại) / Xác nhận phiếu ảo.</summary>
    [HttpPost("{id:guid}/submit")]
    [HasPermission(P.ReceiptManage)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct) => FromResult(await service.SubmitAsync(id, ct));

    [HttpPost("{id:guid}/revalidate")]
    [HasPermission(P.ReceiptManage)]
    public async Task<ActionResult<ReceiptDto>> Revalidate(Guid id, CancellationToken ct) => FromResult(await service.RevalidateAsync(id, ct));

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(P.ReceiptManage)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelReceiptRequest? request, CancellationToken ct) =>
        FromResult(await service.CancelAsync(id, request?.Reason, ct));

    /// <summary>Bổ sung thông tin hóa đơn (NCC ở bước người giao hàng hoặc bệnh viện).</summary>
    [HttpPut("{id:guid}/invoice")]
    [HasPermission(P.ReceiptInvoice, P.ReceiptManage)]
    public async Task<ActionResult<ReceiptDto>> SetInvoice(Guid id, InvoiceRequest request, CancellationToken ct) =>
        FromResult(await service.SetInvoiceAsync(id, request, ct));

    /// <summary>Tải XML hóa đơn điện tử (tự đọc ký hiệu, số, ngày, tổng tiền) hoặc PDF bản thể hiện.</summary>
    [HttpPost("{id:guid}/invoice-file")]
    [HasPermission(P.ReceiptInvoice, P.ReceiptManage)]
    [RequestSizeLimit(UploadLimit)]
    public async Task<ActionResult<ReceiptDto>> UploadInvoice(Guid id, IFormFile? file, CancellationToken ct)
    {
        var upload = await TemplatesController.ReadUploadAsync(file, ct);
        return upload.IsSuccess ? FromResult(await service.UploadInvoiceFileAsync(id, upload.Value, ct)) : Problem(upload.Error!);
    }

    /// <summary>Đọc thử XML hóa đơn (không lưu) để điền sẵn form.</summary>
    [HttpPost("invoice/parse")]
    [HasPermission(P.ReceiptInvoice, P.ReceiptManage)]
    [RequestSizeLimit(UploadLimit)]
    public async Task<ActionResult<ParsedInvoiceDto>> ParseInvoice(IFormFile? file, CancellationToken ct)
    {
        var upload = await TemplatesController.ReadUploadAsync(file, ct);
        return upload.IsSuccess ? FromResult(ReceiptService.ParseInvoice(upload.Value)) : Problem(upload.Error!);
    }

    [HttpGet("{id:guid}/invoice-file/{format}")]
    [HasPermission(P.ReceiptView)]
    public async Task<ActionResult<FileLinkDto>> InvoiceLink(Guid id, string format, CancellationToken ct) =>
        format is "xml" or "pdf" ? FromResult(await service.InvoiceFileLinkAsync(id, format, ct)) : Problem(Error.NotFound());
}

public sealed record CancelReceiptRequest(string? Reason);

/// <summary>
/// API cho HIS (spec §7.2): API key + HMAC-SHA256, chống replay ±5 phút, whitelist IP, rate limit, nhật ký đầy đủ.
/// Tài liệu: docs/HIS_INTEGRATION.md.
/// </summary>
[Route("api/integration/his")]
[AllowAnonymous]
[EnableRateLimiting(AuthSetup.HisRateLimit)]
[ServiceFilter(typeof(HisInboundFilter))]
public sealed class HisIntegrationController(HisReceiptIngestion ingestion, HisInboundService inbound) : ApiControllerBase
{
    /// <summary>Đẩy phiếu nhập kho / hoàn trả. Idempotent theo hisReceiptId.</summary>
    [HttpPost("warehouse-receipts")]
    public async Task<ActionResult<HisIngestResultDto>> PushReceipt(HisReceiptPayload payload, CancellationToken ct)
    {
        var result = await ingestion.IngestAsync(payload, ct);
        if (result.IsSuccess)
        {
            HttpContext.Items[HisInboundFilter.ReceiptItem] = result.Value.ReceiptId;
            HttpContext.Items[HisInboundFilter.OutcomeItem] = result.Value.Outcome;
        }

        return FromResult(result);
    }

    /// <summary>Hợp đồng hiệu lực + số lượng còn lại theo Số QĐ / MST.</summary>
    [HttpGet("contracts")]
    public Task<List<HisContractDto>> Contracts([FromQuery] string? bidDecisionNumber, [FromQuery] string? supplierTaxCode, CancellationToken ct) =>
        inbound.ContractsAsync(bidDecisionNumber, supplierTaxCode, ct);

    /// <summary>Tổng hợp đề xuất đã duyệt (mặc định status=Approved).</summary>
    [HttpGet("proposal-summaries")]
    public Task<List<HisSummaryDto>> Summaries([FromQuery] ProposalSummaryStatus? status, [FromQuery] DateTimeOffset? approvedSince, CancellationToken ct) =>
        inbound.SummariesAsync(status, approvedSince, ct);

    /// <summary>Danh mục hàng hóa (thay đổi từ updatedSince).</summary>
    [HttpGet("items")]
    public Task<List<HisItemDto>> Items([FromQuery] DateTimeOffset? updatedSince, CancellationToken ct) => inbound.ItemsAsync(updatedSince, ct);
}

public sealed record HisIntegrationConfigDto(
    string Provider,
    string DisplayName,
    bool InboundEnabled,
    IReadOnlyList<string> Clients,
    IReadOnlyList<string> AllowedIps,
    int RateLimitPerMinute,
    bool PullEnabled,
    string PullCron,
    int PullLookbackDays,
    bool DevEndpoints);

/// <summary>Cấu hình tích hợp HIS + nhật ký (spec §9.1-14): xem log, gửi lại, kéo phiếu ngay.</summary>
[Route("api/admin/integration")]
[HasPermission(P.IntegrationManage)]
public sealed class IntegrationAdminController(IntegrationLogService service, IOptions<HisInboundOptions> inbound, IOptions<HisOptions> his) : ApiControllerBase
{
    [HttpGet("config")]
    public HisIntegrationConfigDto Config()
    {
        var i = inbound.Value;
        var h = his.Value;
        return new HisIntegrationConfigDto(h.Provider, h.DisplayName, i.Enabled, [.. i.Clients.Select(c => c.Name)], i.AllowedIps, i.RateLimitPerMinute,
            h.PullEnabled, h.PullCron, h.PullLookbackDays, i.DevEndpoints);
    }

    [HttpGet("logs")]
    public Task<PagedResult<IntegrationLogListItemDto>> Logs([FromQuery] PageQuery query, [FromQuery] IntegrationLogFilter filter, CancellationToken ct) =>
        service.ListAsync(query, filter, ct);

    [HttpGet("logs/{id:guid}")]
    public async Task<ActionResult<IntegrationLogDto>> Log(Guid id, CancellationToken ct) => FromResult(await service.GetAsync(id, ct));

    /// <summary>Gửi lại: xử lý lại nội dung phiếu đã lưu của một lần nhận lỗi.</summary>
    [HttpPost("logs/{id:guid}/retry")]
    public async Task<ActionResult<HisIngestResultDto>> Retry(Guid id, CancellationToken ct) => FromResult(await service.RetryAsync(id, ct));

    /// <summary>Kéo phiếu từ HIS ngay (không chờ lịch).</summary>
    [HttpPost("his/pull")]
    public Task<PullResultDto> Pull(CancellationToken ct) => service.PullAsync(his.Value.PullLookbackDays, ct);
}

/// <summary>Giả lập HIS cho môi trường demo/kiểm thử (spec §7.3) — bật bằng HisInbound:DevEndpoints.</summary>
[Route("api/dev/his")]
public sealed class DevHisController(HisSimulator simulator, IOptions<HisInboundOptions> options) : ApiControllerBase
{
    [HttpGet("status")]
    [HasPermission(P.ReceiptView)]
    public DevHisStatusDto Status() => new(options.Value.DevEndpoints);

    /// <summary>Giả lập HIS đẩy (push) hoặc lập phiếu chờ kéo (pull) cho một hợp đồng hiệu lực.</summary>
    [HttpPost("simulate-receipt")]
    [HasPermission(P.ReceiptManage, P.IntegrationManage)]
    public async Task<ActionResult<SimulateReceiptResultDto>> Simulate(SimulateReceiptRequest request, CancellationToken ct) =>
        options.Value.DevEndpoints ? FromResult(await simulator.SimulateAsync(request, ct)) : Problem(Error.NotFound());
}

public sealed record DevHisStatusDto(bool Enabled);
