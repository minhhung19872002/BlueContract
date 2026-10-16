using BlueContract.Api.Auth;
using BlueContract.Application.Archive;
using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using P = BlueContract.Domain.Identity.Permissions;

namespace BlueContract.Api.Controllers;

/// <summary>Hồ sơ lưu trữ (spec §9.1-12).</summary>
[Route("api/archive")]
public sealed class ArchiveController(ArchiveService service) : ApiControllerBase
{
    /// <summary>Tìm hồ sơ theo số hợp đồng, tên, số quyết định thầu hoặc nhà cung cấp.</summary>
    [HttpGet]
    [HasPermission(P.ArchiveView)]
    public Task<PagedResult<ArchiveSearchItemDto>> Search([FromQuery] PageQuery query, CancellationToken ct) => service.SearchAsync(query, ct);

    [HttpGet("{contractId:guid}")]
    [HasPermission(P.ArchiveView)]
    public async Task<ActionResult<ArchiveTreeDto>> Tree(Guid contractId, CancellationToken ct) => FromResult(await service.TreeAsync(contractId, ct));

    [HttpGet("{contractId:guid}/files/{fileId}")]
    [HasPermission(P.ArchiveView)]
    public async Task<ActionResult<FileLinkDto>> File(Guid contractId, string fileId, CancellationToken ct) =>
        FromResult(await service.FileLinkAsync(contractId, fileId, ct));

    /// <summary>Tải toàn bộ hồ sơ (ZIP) kèm muc_luc.pdf liệt kê tài liệu + SHA-256.</summary>
    [HttpGet("{contractId:guid}/zip")]
    [HasPermission(P.ArchiveDownload)]
    public async Task<IActionResult> Zip(Guid contractId, CancellationToken ct)
    {
        var result = await service.ZipAsync(contractId, ct);
        return result.IsSuccess ? File(result.Value.Content, "application/zip", result.Value.FileName) : Problem(result.Error!);
    }

    [HttpPut("{contractId:guid}/complete")]
    [HasPermission(P.ArchiveDownload)]
    public async Task<ActionResult<ArchiveTreeDto>> Complete(Guid contractId, ArchiveCompleteRequest request, CancellationToken ct) =>
        FromResult(await service.SetCompleteAsync(contractId, request, ct));
}

/// <summary>Tổng quan (spec §9.1-1, §9.2-1).</summary>
[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController(DashboardService service) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<InternalDashboardDto>> Internal(CancellationToken ct) =>
        service.IsSupplier ? Problem(Error.Forbidden()) : await service.InternalAsync(ct);

    [HttpGet("supplier")]
    public async Task<ActionResult<SupplierDashboardDto>> Supplier(CancellationToken ct) =>
        service.IsSupplier ? await service.SupplierAsync(ct) : Problem(Error.Forbidden());
}

/// <summary>Báo cáo + xuất Excel (spec §9.1-13).</summary>
[Route("api/reports")]
[HasPermission(P.ReportView)]
public sealed class ReportsController(ReportService service, IExcelExporter excel) : ApiControllerBase
{
    [HttpGet("contract-execution")]
    public Task<List<ContractExecutionRowDto>> ContractExecution([FromQuery] ContractExecutionFilter filter, CancellationToken ct) =>
        service.ContractExecutionAsync(filter, ct);

    [HttpGet("contract-execution/export")]
    public async Task<FileContentResult> ContractExecutionExport([FromQuery] ContractExecutionFilter filter, CancellationToken ct)
    {
        var rows = await service.ContractExecutionAsync(filter, ct);
        return Excel(excel.Export("Thực hiện hợp đồng", [
            new ExcelColumn<ContractExecutionRowDto>("Số hợp đồng", r => r.ContractNumber, Width: 20),
            new ExcelColumn<ContractExecutionRowDto>("Tên hợp đồng", r => r.ContractName, Width: 40),
            new ExcelColumn<ContractExecutionRowDto>("Nhà cung cấp", r => r.SupplierName, Width: 36),
            new ExcelColumn<ContractExecutionRowDto>("Hạng mục", r => r.CategoryName, Width: 18),
            new ExcelColumn<ContractExecutionRowDto>("Hết hạn", r => r.ExpiryDate, ExcelColumnFormat.Date, 12),
            new ExcelColumn<ContractExecutionRowDto>("Giá trị HĐ", r => r.ContractValue, ExcelColumnFormat.Money, 18),
            new ExcelColumn<ContractExecutionRowDto>("Đã gọi hàng", r => r.CalledValue, ExcelColumnFormat.Money, 18),
            new ExcelColumn<ContractExecutionRowDto>("Đã nhập kho", r => r.ReceivedValue, ExcelColumnFormat.Money, 18),
            new ExcelColumn<ContractExecutionRowDto>("Đã nghiệm thu", r => r.AcceptedValue, ExcelColumnFormat.Money, 18),
            new ExcelColumn<ContractExecutionRowDto>("Đã thanh toán", r => r.PaidValue, ExcelColumnFormat.Money, 18),
            new ExcelColumn<ContractExecutionRowDto>("Còn lại", r => r.RemainingValue, ExcelColumnFormat.Money, 18),
            new ExcelColumn<ContractExecutionRowDto>("% thực hiện", r => r.ExecutionPercent, ExcelColumnFormat.Number, 12),
        ], rows), "thuc-hien-hop-dong.xlsx");
    }

    [HttpGet("supplier-debt")]
    public Task<List<SupplierDebtRowDto>> SupplierDebt(CancellationToken ct) => service.SupplierDebtAsync(ct);

    [HttpGet("supplier-debt/export")]
    public async Task<FileContentResult> SupplierDebtExport(CancellationToken ct)
    {
        var rows = await service.SupplierDebtAsync(ct);
        return Excel(excel.Export("Công nợ NCC", [
            new ExcelColumn<SupplierDebtRowDto>("Nhà cung cấp", r => r.SupplierName, Width: 40),
            new ExcelColumn<SupplierDebtRowDto>("MST", r => r.TaxCode, Width: 14),
            new ExcelColumn<SupplierDebtRowDto>("Số HĐ", r => r.ContractCount, ExcelColumnFormat.Number, 8),
            new ExcelColumn<SupplierDebtRowDto>("Giá trị HĐ", r => r.ContractValue, ExcelColumnFormat.Money, 18),
            new ExcelColumn<SupplierDebtRowDto>("Đã nghiệm thu", r => r.AcceptedValue, ExcelColumnFormat.Money, 18),
            new ExcelColumn<SupplierDebtRowDto>("Đã lập 08a", r => r.ValuedValue, ExcelColumnFormat.Money, 18),
            new ExcelColumn<SupplierDebtRowDto>("Đã thanh toán", r => r.PaidValue, ExcelColumnFormat.Money, 18),
            new ExcelColumn<SupplierDebtRowDto>("Còn phải trả", r => r.DebtValue, ExcelColumnFormat.Money, 18),
        ], rows), "cong-no-nha-cung-cap.xlsx");
    }

    [HttpGet("payments")]
    public Task<List<PaymentReportRowDto>> Payments([FromQuery] PaymentReportFilter filter, CancellationToken ct) => service.PaymentsAsync(filter, ct);

    [HttpGet("payments/export")]
    public async Task<FileContentResult> PaymentsExport([FromQuery] PaymentReportFilter filter, CancellationToken ct)
    {
        var rows = await service.PaymentsAsync(filter, ct);
        return Excel(excel.Export("Thanh toán", [
            new ExcelColumn<PaymentReportRowDto>("Kỳ", r => r.Period, Width: 14),
            new ExcelColumn<PaymentReportRowDto>("Nhóm", r => r.Group, Width: 36),
            new ExcelColumn<PaymentReportRowDto>("Số UNC", r => r.PaymentCount, ExcelColumnFormat.Number, 10),
            new ExcelColumn<PaymentReportRowDto>("Giá trị đã thanh toán", r => r.PaidAmount, ExcelColumnFormat.Money, 20),
        ], rows), "gia-tri-thanh-toan.xlsx");
    }

    [HttpGet("signing")]
    public Task<SigningReportDto> Signing([FromQuery] DateTimeOffset? from, CancellationToken ct) => service.SigningAsync(from, ct);

    [HttpGet("signing/export")]
    public async Task<FileContentResult> SigningExport([FromQuery] DateTimeOffset? from, CancellationToken ct)
    {
        var report = await service.SigningAsync(from, ct);
        return Excel(excel.Export("Tiến độ ký", [
            new ExcelColumn<SigningStepRowDto>("Luồng ký", r => r.WorkflowCode, Width: 22),
            new ExcelColumn<SigningStepRowDto>("Bước", r => r.StepName, Width: 36),
            new ExcelColumn<SigningStepRowDto>("Số lần xử lý", r => r.Completed, ExcelColumnFormat.Number, 12),
            new ExcelColumn<SigningStepRowDto>("Trung bình (giờ)", r => r.AverageHours, ExcelColumnFormat.Number, 14),
            new ExcelColumn<SigningStepRowDto>("Lâu nhất (giờ)", r => r.MaxHours, ExcelColumnFormat.Number, 14),
            new ExcelColumn<SigningStepRowDto>("Quá hạn SLA", r => r.Overdue, ExcelColumnFormat.Number, 12),
        ], report.Steps), "tien-do-ky.xlsx");
    }
}

/// <summary>Kiểm tra toàn vẹn tài liệu (spec §11): lịch sử các lần kiểm tra và chạy ngay.</summary>
[Route("api/admin/integrity")]
[HasPermission(P.SettingManage, P.AuditView)]
public sealed class IntegrityController(BlueContract.Application.Admin.IntegrityService service, ICurrentUser current) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<BlueContract.Application.Admin.IntegrityRunListItemDto>> List([FromQuery] PageQuery query, CancellationToken ct) =>
        service.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BlueContract.Application.Admin.IntegrityRunDto>> Get(Guid id, CancellationToken ct) => FromResult(await service.GetAsync(id, ct));

    [HttpPost("run")]
    public Task<BlueContract.Application.Admin.IntegrityRunDto> Run(CancellationToken ct) => service.RunAsync(current.FullName ?? "Quản trị", ct);
}
