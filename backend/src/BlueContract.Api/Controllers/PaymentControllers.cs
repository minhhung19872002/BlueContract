using BlueContract.Api.Auth;
using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Payments;
using Microsoft.AspNetCore.Mvc;
using P = BlueContract.Domain.Identity.Permissions;

namespace BlueContract.Api.Controllers;

/// <summary>Biên bản nghiệm thu (spec §6.5, §9.1-11 tab Nghiệm thu).</summary>
[Route("api/acceptances")]
public sealed class AcceptancesController(AcceptanceService service, IClock clock) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(P.PaymentView)]
    public Task<PagedResult<AcceptanceListItemDto>> List([FromQuery] PageQuery query, [FromQuery] AcceptanceFilter filter, CancellationToken ct) =>
        service.ListAsync(query, filter, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(P.PaymentView)]
    public async Task<ActionResult<AcceptanceDto>> Get(Guid id, CancellationToken ct) => FromResult(await service.GetAsync(id, ct));

    /// <summary>Phiếu nhập kho đã ký đủ, chưa nghiệm thu của hợp đồng.</summary>
    [HttpGet("candidates")]
    [HasPermission(P.PaymentManage)]
    public Task<List<AcceptanceReceiptDto>> Candidates([FromQuery] Guid contractId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? acceptanceId,
        CancellationToken ct) => service.CandidatesAsync(contractId, from, to, acceptanceId, ct);

    [HttpPost]
    [HasPermission(P.PaymentManage)]
    public async Task<ActionResult<Guid>> Create(SaveAcceptanceRequest request, CancellationToken ct) => FromResult(await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(P.PaymentManage)]
    public async Task<IActionResult> Update(Guid id, SaveAcceptanceRequest request, CancellationToken ct) => FromResult(await service.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/submit")]
    [HasPermission(P.PaymentManage)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct) => FromResult(await service.SubmitAsync(id, ct));

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(P.PaymentManage)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => FromResult(await service.CancelAsync(id, ct));

    /// <summary>Chạy ngay việc lập nghiệm thu tháng trước (bình thường do job ngày 1 hằng tháng thực hiện).</summary>
    [HttpPost("generate")]
    [HasPermission(P.PaymentManage)]
    public Task<GenerateAcceptancesResultDto> Generate(CancellationToken ct) =>
        service.GenerateMonthlyAsync(DateOnly.FromDateTime(clock.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime).AddMonths(1), ct);
}

/// <summary>Biên bản xác định giá trị (08a) + Đề nghị thanh toán + Ủy nhiệm chi.</summary>
[Route("api/valuations")]
public sealed class ValuationsController(ValuationService service) : ApiControllerBase
{
    private const long UploadLimit = FileInspector.MaxUploadBytes + (1024 * 1024);

    [HttpGet]
    [HasPermission(P.PaymentView)]
    public Task<PagedResult<ValuationListItemDto>> List([FromQuery] PageQuery query, [FromQuery] ValuationFilter filter, CancellationToken ct) =>
        service.ListAsync(query, filter, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(P.PaymentView)]
    public async Task<ActionResult<ValuationDto>> Get(Guid id, CancellationToken ct) => FromResult(await service.GetAsync(id, ct));

    [HttpPost]
    [HasPermission(P.PaymentManage)]
    public async Task<ActionResult<ValuationDto>> Create(SaveValuationRequest request, CancellationToken ct) => FromResult(await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(P.PaymentManage)]
    public async Task<IActionResult> Update(Guid id, SaveValuationRequest request, CancellationToken ct) => FromResult(await service.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/submit")]
    [HasPermission(P.PaymentManage)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct) => FromResult(await service.SubmitAsync(id, ct));

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(P.PaymentManage)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => FromResult(await service.CancelAsync(id, ct));

    /// <summary>Đính kèm Ủy nhiệm chi (form: number, date, amount, file tùy chọn).</summary>
    [HttpPost("{id:guid}/payment-orders")]
    [HasPermission(P.PaymentManage)]
    [RequestSizeLimit(UploadLimit)]
    public async Task<ActionResult<ValuationDto>> AddPaymentOrder(Guid id, [FromForm] PaymentOrderRequest request, IFormFile? file, CancellationToken ct)
    {
        UploadedFile? upload = null;
        if (file is not null)
        {
            var read = await TemplatesController.ReadUploadAsync(file, ct);
            if (!read.IsSuccess)
            {
                return Problem(read.Error!);
            }

            upload = read.Value;
        }

        return FromResult(await service.AddPaymentOrderAsync(id, request, upload, ct));
    }

    [HttpDelete("{id:guid}/payment-orders/{orderId:guid}")]
    [HasPermission(P.PaymentManage)]
    public async Task<ActionResult<ValuationDto>> RemovePaymentOrder(Guid id, Guid orderId, CancellationToken ct) =>
        FromResult(await service.RemovePaymentOrderAsync(id, orderId, ct));

    [HttpGet("{id:guid}/payment-orders/{orderId:guid}/file")]
    [HasPermission(P.PaymentView)]
    public async Task<ActionResult<FileLinkDto>> PaymentOrderFile(Guid id, Guid orderId, CancellationToken ct) =>
        FromResult(await service.PaymentOrderFileAsync(id, orderId, ct));
}

/// <summary>Biên bản thanh lý + chốt công nợ.</summary>
[Route("api/liquidations")]
public sealed class LiquidationsController(LiquidationService service) : ApiControllerBase
{
    private const long UploadLimit = FileInspector.MaxUploadBytes + (1024 * 1024);

    [HttpGet]
    [HasPermission(P.PaymentView)]
    public Task<PagedResult<LiquidationListItemDto>> List([FromQuery] PageQuery query, [FromQuery] LiquidationFilter filter, CancellationToken ct) =>
        service.ListAsync(query, filter, ct);

    /// <summary>Bảng đối chiếu giá trị HĐ / thực hiện / đã thanh toán / còn lại trước khi lập biên bản.</summary>
    [HttpGet("preview")]
    [HasPermission(P.PaymentView)]
    public async Task<ActionResult<LiquidationPreviewDto>> Preview([FromQuery] Guid contractId, CancellationToken ct) =>
        FromResult(await service.PreviewAsync(contractId, ct));

    [HttpGet("{id:guid}")]
    [HasPermission(P.PaymentView)]
    public async Task<ActionResult<LiquidationDto>> Get(Guid id, CancellationToken ct) => FromResult(await service.GetAsync(id, ct));

    [HttpPost]
    [HasPermission(P.PaymentManage)]
    public async Task<ActionResult<Guid>> Create(SaveLiquidationRequest request, CancellationToken ct) => FromResult(await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(P.PaymentManage)]
    public async Task<IActionResult> Update(Guid id, SaveLiquidationRequest request, CancellationToken ct) => FromResult(await service.UpdateAsync(id, request, ct));

    /// <summary>Đánh dấu khoản chưa thanh toán là "đã thanh toán" (form: valuationId, description, amount, voucherNumber, voucherDate, file).</summary>
    [HttpPost("{id:guid}/settlements")]
    [HasPermission(P.PaymentManage)]
    [RequestSizeLimit(UploadLimit)]
    public async Task<ActionResult<LiquidationDto>> AddSettlement(Guid id, [FromForm] SettlementRequest request, IFormFile? file, CancellationToken ct)
    {
        UploadedFile? upload = null;
        if (file is not null)
        {
            var read = await TemplatesController.ReadUploadAsync(file, ct);
            if (!read.IsSuccess)
            {
                return Problem(read.Error!);
            }

            upload = read.Value;
        }

        return FromResult(await service.AddSettlementAsync(id, request, upload, ct));
    }

    [HttpDelete("{id:guid}/settlements/{settlementId:guid}")]
    [HasPermission(P.PaymentManage)]
    public async Task<ActionResult<LiquidationDto>> RemoveSettlement(Guid id, Guid settlementId, CancellationToken ct) =>
        FromResult(await service.RemoveSettlementAsync(id, settlementId, ct));

    [HttpPost("{id:guid}/submit")]
    [HasPermission(P.PaymentManage)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct) => FromResult(await service.SubmitAsync(id, ct));

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(P.PaymentManage)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => FromResult(await service.CancelAsync(id, ct));
}
