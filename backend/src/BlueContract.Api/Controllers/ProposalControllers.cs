using BlueContract.Api.Auth;
using BlueContract.Application.Common;
using BlueContract.Application.Proposals;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Proposals;
using Microsoft.AspNetCore.Mvc;
using P = BlueContract.Domain.Identity.Permissions;

namespace BlueContract.Api.Controllers;

/// <summary>Phiếu đề xuất của khoa/phòng (spec §6.2 B1, §9.1-7).</summary>
[Route("api/proposals")]
public sealed class ProposalsController(ProposalService service) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(P.ProposalView)]
    public Task<PagedResult<ProposalListItemDto>> List([FromQuery] PageQuery query, [FromQuery] ProposalFilter filter, CancellationToken ct) =>
        service.ListAsync(query, filter, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(P.ProposalView)]
    public async Task<ActionResult<ProposalDto>> Get(Guid id, CancellationToken ct) => FromResult(await service.GetAsync(id, ct));

    /// <summary>Mặt hàng có thể đề xuất (HĐ hiệu lực của mảng) kèm X–N–T từ HIS.</summary>
    [HttpGet("candidates")]
    [HasPermission(P.ProposalCreate)]
    public async Task<ActionResult<ProposalCandidatesDto>> Candidates(
        [FromQuery] ProcurementDomain domain, [FromQuery] Guid warehouseId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? departmentId, CancellationToken ct) =>
        FromResult(await service.CandidatesAsync(domain, warehouseId, from, to, departmentId, ct));

    [HttpPost]
    [HasPermission(P.ProposalCreate)]
    public async Task<ActionResult<Guid>> Create(SaveProposalRequest request, CancellationToken ct) => FromResult(await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(P.ProposalCreate)]
    public async Task<IActionResult> Update(Guid id, SaveProposalRequest request, CancellationToken ct) => FromResult(await service.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/submit")]
    [HasPermission(P.ProposalCreate)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct) => FromResult(await service.SubmitAsync(id, ct));

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(P.ProposalCreate)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => FromResult(await service.CancelAsync(id, ct));
}

/// <summary>Tổng hợp đề xuất (spec §6.2 B2–B3, §9.1-8).</summary>
[Route("api/proposal-summaries")]
public sealed class ProposalSummariesController(ProposalSummaryService service, IExcelExporter excel) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(P.ProposalView, P.CallOrderView)]
    public Task<PagedResult<SummaryListItemDto>> List([FromQuery] PageQuery query, [FromQuery] SummaryFilter filter, CancellationToken ct) =>
        service.ListAsync(query, filter, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(P.ProposalView, P.CallOrderView)]
    public async Task<ActionResult<SummaryDto>> Get(Guid id, CancellationToken ct) => FromResult(await service.GetAsync(id, ct));

    /// <summary>Xuất Excel bảng tổng hợp (SL đề xuất, X–N–T, SL duyệt, đã gọi, còn gọi).</summary>
    [HttpGet("{id:guid}/export")]
    [HasPermission(P.ProposalView, P.CallOrderView)]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        var summary = await service.GetAsync(id, ct);
        if (!summary.IsSuccess)
        {
            return Problem(summary.Error!);
        }

        var bytes = excel.Export($"Tổng hợp {summary.Value.Number}", [
            new ExcelColumn<SummaryLineDto>("Nhà cung cấp", x => x.SupplierName, Width: 30),
            new ExcelColumn<SummaryLineDto>("Số HĐ", x => x.ContractNumber, Width: 16),
            new ExcelColumn<SummaryLineDto>("Mã hàng", x => x.ItemCode, Width: 12),
            new ExcelColumn<SummaryLineDto>("Tên hàng", x => x.ItemName, Width: 36),
            new ExcelColumn<SummaryLineDto>("ĐVT", x => x.Unit, Width: 8),
            new ExcelColumn<SummaryLineDto>("Tổng SL đề xuất", x => x.ProposedQuantity, ExcelColumnFormat.Number, 12),
            new ExcelColumn<SummaryLineDto>("Tồn đầu", x => x.OpeningStock, ExcelColumnFormat.Number, 10),
            new ExcelColumn<SummaryLineDto>("Nhập", x => x.Received, ExcelColumnFormat.Number, 10),
            new ExcelColumn<SummaryLineDto>("Xuất", x => x.Issued, ExcelColumnFormat.Number, 10),
            new ExcelColumn<SummaryLineDto>("Tồn", x => x.CurrentStock, ExcelColumnFormat.Number, 10),
            new ExcelColumn<SummaryLineDto>("SL duyệt", x => x.ApprovedQuantity, ExcelColumnFormat.Number, 12),
            new ExcelColumn<SummaryLineDto>("Đã gọi", x => x.CalledQuantity, ExcelColumnFormat.Number, 10),
            new ExcelColumn<SummaryLineDto>("Còn được gọi", x => x.RemainingToCall, ExcelColumnFormat.Number, 12),
            new ExcelColumn<SummaryLineDto>("Đơn giá", x => x.UnitPrice, ExcelColumnFormat.Money, 14),
            new ExcelColumn<SummaryLineDto>("Thành tiền (SL duyệt)", x => x.Amount, ExcelColumnFormat.Money, 16),
        ], summary.Value.Lines);
        return Excel(bytes, $"tong-hop-{summary.Value.Number}.xlsx");
    }

    /// <summary>Phiếu đề xuất đã gửi, chưa tổng hợp.</summary>
    [HttpGet("candidates")]
    [HasPermission(P.ProposalAggregate)]
    public Task<List<ProposalListItemDto>> Candidates([FromQuery] ProcurementDomain domain, [FromQuery] Guid? facilityId, CancellationToken ct) =>
        service.CandidatesAsync(domain, facilityId, ct);

    [HttpPost]
    [HasPermission(P.ProposalAggregate)]
    public async Task<ActionResult<Guid>> Create(SaveSummaryRequest request, CancellationToken ct) => FromResult(await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(P.ProposalAggregate)]
    public async Task<IActionResult> Update(Guid id, SaveSummaryRequest request, CancellationToken ct) => FromResult(await service.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/submit")]
    [HasPermission(P.ProposalAggregate)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct) => FromResult(await service.SubmitAsync(id, ct));

    /// <summary>Tổ trưởng sửa SL duyệt.</summary>
    [HttpPut("{id:guid}/approved-quantities")]
    [HasPermission(P.ProposalApprove)]
    public async Task<IActionResult> SetApproved(Guid id, List<ApprovedQuantityRequest> lines, CancellationToken ct) =>
        FromResult(await service.SetApprovedAsync(id, lines, ct));

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(P.ProposalAggregate)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => FromResult(await service.CancelAsync(id, ct));
}

/// <summary>Gọi hàng (spec §6.3, §9.1-9, §9.2-3). Suppliers see and confirm only their own orders.</summary>
[Route("api/call-orders")]
public sealed class CallOrdersController(CallOrderService service, IExcelExporter excel) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(P.CallOrderView)]
    public Task<PagedResult<CallOrderListItemDto>> List([FromQuery] PageQuery query, [FromQuery] CallOrderFilter filter, CancellationToken ct) =>
        service.ListAsync(query, filter, ct);

    [HttpGet("export")]
    [HasPermission(P.CallOrderView)]
    public async Task<FileContentResult> Export([FromQuery] PageQuery query, [FromQuery] CallOrderFilter filter, CancellationToken ct)
    {
        var page = await service.ListAsync(query with { Page = 1, PageSize = PageQuery.MaxExportRows, ForExport = true }, filter, ct);
        var bytes = excel.Export("Gọi hàng", [
            new ExcelColumn<CallOrderListItemDto>("Số đơn", x => x.Number, Width: 16),
            new ExcelColumn<CallOrderListItemDto>("Nhà cung cấp", x => x.SupplierName, Width: 34),
            new ExcelColumn<CallOrderListItemDto>("Phiếu tổng hợp", x => x.SummaryNumber, Width: 16),
            new ExcelColumn<CallOrderListItemDto>("Kho nhận", x => x.WarehouseName, Width: 20),
            new ExcelColumn<CallOrderListItemDto>("Ngày gọi", x => x.CallDate.ToDateTime(TimeOnly.MinValue), ExcelColumnFormat.Date, 12),
            new ExcelColumn<CallOrderListItemDto>("Hạn giao", x => x.DeliveryDeadline.ToDateTime(TimeOnly.MinValue), ExcelColumnFormat.Date, 12),
            new ExcelColumn<CallOrderListItemDto>("Giá trị", x => x.TotalAmount, ExcelColumnFormat.Money, 16),
            new ExcelColumn<CallOrderListItemDto>("Trạng thái", x => x.Status switch
            {
                CallOrderStatus.Sent => x.IsOverdue ? "Chờ NCC xác nhận (quá hạn)" : "Chờ NCC xác nhận",
                CallOrderStatus.SupplierConfirmed => "NCC đã xác nhận",
                CallOrderStatus.PartiallyConfirmed => "NCC xác nhận một phần",
                CallOrderStatus.Rejected => "NCC từ chối",
                CallOrderStatus.Delivered => "Đã giao",
                _ => "Đã hủy",
            }, Width: 24),
        ], page.Items);
        return Excel(bytes, "goi-hang.xlsx");
    }

    [HttpGet("{id:guid}")]
    [HasPermission(P.CallOrderView)]
    public async Task<ActionResult<CallOrderDto>> Get(Guid id, CancellationToken ct) => FromResult(await service.GetAsync(id, ct));

    /// <summary>Tạo lần gọi hàng từ tổng hợp đã duyệt (tự tách theo NCC).</summary>
    [HttpPost]
    [HasPermission(P.CallOrderCreate)]
    public async Task<ActionResult<List<Guid>>> Create(CreateCallsRequest request, CancellationToken ct) => FromResult(await service.CreateAsync(request, ct));

    [HttpPost("{id:guid}/confirm")]
    [HasPermission(P.CallOrderConfirm)]
    public async Task<IActionResult> Confirm(Guid id, ConfirmCallRequest request, CancellationToken ct) => FromResult(await service.ConfirmAsync(id, request, ct));

    /// <summary>Hủy đơn gọi hàng; the optional reason goes to the supplier's email and the timeline.</summary>
    [HttpPost("{id:guid}/cancel")]
    [HasPermission(P.CallOrderCreate)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CancelCallRequest? request, CancellationToken ct) =>
        FromResult(await service.CancelAsync(id, request?.Reason, ct));

    /// <summary>Diễn biến đơn gọi hàng: lập, gửi NCC, xác nhận, nhập kho, quá hạn, hủy.</summary>
    [HttpGet("{id:guid}/timeline")]
    [HasPermission(P.CallOrderView)]
    public async Task<ActionResult<List<TimelineEventDto>>> Timeline(Guid id, CancellationToken ct) => FromResult(await service.TimelineAsync(id, ct));
}
