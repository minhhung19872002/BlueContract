using BlueContract.Api.Auth;
using BlueContract.Application.Bids;
using BlueContract.Application.Catalog;
using BlueContract.Application.Common;
using BlueContract.Application.Contracts;
using BlueContract.Domain.Bids;
using BlueContract.Domain.Catalog;
using BlueContract.Domain.Contracts;
using Microsoft.AspNetCore.Mvc;
using P = BlueContract.Domain.Identity.Permissions;

namespace BlueContract.Api.Controllers;

/// <summary>Danh mục hàng hóa (spec §9.1-4).</summary>
[Route("api/items")]
public sealed class ItemsController(CatalogService service, IExcelExporter excel) : ApiControllerBase
{
    [HttpGet("categories")]
    [HasPermission(P.ItemView, P.ContractView, P.BidView, P.MasterDataView)]
    public Task<List<ItemCategoryDto>> Categories(CancellationToken ct) => service.CategoriesAsync(ct);

    [HttpGet]
    [HasPermission(P.ItemView)]
    public Task<PagedResult<ItemDto>> List([FromQuery] PageQuery query, [FromQuery] ItemFilter filter, CancellationToken ct) =>
        service.ListAsync(query, filter, ct);

    [HttpGet("export")]
    [HasPermission(P.ItemView)]
    public async Task<FileContentResult> Export([FromQuery] PageQuery query, [FromQuery] ItemFilter filter, CancellationToken ct)
    {
        var page = await service.ListAsync(query with { Page = 1, PageSize = PageQuery.MaxExportRows, ForExport = true }, filter, ct);
        var bytes = excel.Export("Danh mục hàng hóa", [
            new ExcelColumn<ItemDto>("Mã key HIS", x => x.HisKey, Width: 14),
            new ExcelColumn<ItemDto>("Mã hàng", x => x.Code, Width: 14),
            new ExcelColumn<ItemDto>("Tên hàng", x => x.Name, Width: 40),
            new ExcelColumn<ItemDto>("Hoạt chất / thông số", x => x.ActiveIngredient, Width: 30),
            new ExcelColumn<ItemDto>("Quy cách", x => x.Specification, Width: 20),
            new ExcelColumn<ItemDto>("ĐVT", x => x.Unit, Width: 10),
            new ExcelColumn<ItemDto>("Hãng sản xuất", x => x.Manufacturer, Width: 24),
            new ExcelColumn<ItemDto>("Nước sản xuất", x => x.Country, Width: 14),
            new ExcelColumn<ItemDto>("Hạng mục", x => x.CategoryName, Width: 16),
            new ExcelColumn<ItemDto>("Nhà cung cấp", x => x.SupplierName, Width: 30),
            new ExcelColumn<ItemDto>("Số QĐ thầu", x => x.BidDecisionNumber, Width: 16),
            new ExcelColumn<ItemDto>("Đơn giá trúng thầu", x => x.WinningUnitPrice, ExcelColumnFormat.Money, 16),
            new ExcelColumn<ItemDto>("Nguồn", x => x.Source switch { ItemSource.His => "HIS", ItemSource.Import => "Import Excel", _ => "Nhập tay" }, Width: 12),
        ], page.Items);
        return Excel(bytes, "danh-muc-hang-hoa.xlsx");
    }

    [HttpGet("{id:guid}")]
    [HasPermission(P.ItemView)]
    public async Task<ActionResult<ItemDto>> Get(Guid id, CancellationToken ct) => FromResult(await service.GetAsync(id, ct));

    [HttpPost]
    [HasPermission(P.ItemManage)]
    public async Task<ActionResult<ItemDto>> Create(SaveItemRequest request, CancellationToken ct) => FromResult(await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(P.ItemManage)]
    public async Task<ActionResult<ItemDto>> Update(Guid id, SaveItemRequest request, CancellationToken ct) =>
        FromResult(await service.UpdateAsync(id, request, ct));

    /// <summary>Biểu mẫu import .xlsx (1: thuốc/VTYT/hóa chất/sinh phẩm, 2: hàng hóa/tài sản/dịch vụ).</summary>
    [HttpGet("import-template")]
    [HasPermission(P.ItemView, P.BidManage, P.ContractCreate)]
    public async Task<FileContentResult> ImportTemplate([FromQuery] ItemImportKind kind, CancellationToken ct) =>
        Excel(await service.ImportTemplateAsync(kind, ct), kind == ItemImportKind.Medical
            ? "bieu-mau-import-thuoc-vtyt-hoachat-sinhpham.xlsx"
            : "bieu-mau-import-hanghoa-taisan-dichvu.xlsx");
}

/// <summary>Quyết định thầu (spec §6.1 B1, §9.1-3).</summary>
[Route("api/bid-decisions")]
public sealed class BidDecisionsController(BidDecisionService service, IExcelExporter excel) : ApiControllerBase
{
    private const long UploadLimit = FileInspector.MaxUploadBytes + (1024 * 1024);

    [HttpGet]
    [HasPermission(P.BidView)]
    public Task<PagedResult<BidDecisionListItemDto>> List([FromQuery] PageQuery query, [FromQuery] BidFilter filter, CancellationToken ct) =>
        service.ListAsync(query, filter, ct);

    [HttpGet("export")]
    [HasPermission(P.BidView)]
    public async Task<FileContentResult> Export([FromQuery] PageQuery query, [FromQuery] BidFilter filter, CancellationToken ct)
    {
        var page = await service.ListAsync(query with { Page = 1, PageSize = PageQuery.MaxExportRows, ForExport = true }, filter, ct);
        var bytes = excel.Export("Quyết định thầu", [
            new ExcelColumn<BidDecisionListItemDto>("Số quyết định", x => x.Number, Width: 18),
            new ExcelColumn<BidDecisionListItemDto>("Ngày ký", x => x.SignedDate.ToDateTime(TimeOnly.MinValue), ExcelColumnFormat.Date, 12),
            new ExcelColumn<BidDecisionListItemDto>("Tên gói thầu", x => x.PackageName, Width: 40),
            new ExcelColumn<BidDecisionListItemDto>("Hạng mục", x => x.CategoryName, Width: 16),
            new ExcelColumn<BidDecisionListItemDto>("Số NCC trúng thầu", x => x.SupplierCount, ExcelColumnFormat.Number, 10),
            new ExcelColumn<BidDecisionListItemDto>("Số mặt hàng", x => x.ItemCount, ExcelColumnFormat.Number, 10),
            new ExcelColumn<BidDecisionListItemDto>("Giá trị trúng thầu", x => x.TotalValue, ExcelColumnFormat.Money, 18),
            new ExcelColumn<BidDecisionListItemDto>("Số hợp đồng", x => x.ContractCount, ExcelColumnFormat.Number, 10),
            new ExcelColumn<BidDecisionListItemDto>("Trạng thái", x => x.Status switch
            {
                BidDecisionStatus.Draft => "Đang nhập liệu",
                BidDecisionStatus.Active => "Đã ban hành",
                _ => "Đã đóng",
            }, Width: 14),
        ], page.Items);
        return Excel(bytes, "quyet-dinh-thau.xlsx");
    }

    [HttpGet("{id:guid}")]
    [HasPermission(P.BidView)]
    public async Task<ActionResult<BidDecisionDto>> Get(Guid id, CancellationToken ct) => FromResult(await service.GetAsync(id, ct));

    [HttpGet("{id:guid}/items")]
    [HasPermission(P.BidView, P.ContractCreate)]
    public async Task<ActionResult<List<BidItemDto>>> Items(Guid id, [FromQuery] Guid? supplierId, CancellationToken ct) =>
        FromResult(await service.ItemsAsync(id, supplierId, ct));

    [HttpPost]
    [HasPermission(P.BidManage)]
    public async Task<ActionResult<Guid>> Create(SaveBidDecisionRequest request, CancellationToken ct) => FromResult(await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(P.BidManage)]
    public async Task<IActionResult> Update(Guid id, SaveBidDecisionRequest request, CancellationToken ct) => FromResult(await service.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/attachment")]
    [HasPermission(P.BidManage)]
    [RequestSizeLimit(UploadLimit)]
    public async Task<IActionResult> Attach(Guid id, IFormFile? file, CancellationToken ct)
    {
        var upload = await TemplatesController.ReadUploadAsync(file, ct);
        return upload.IsSuccess ? FromResult(await service.AttachAsync(id, upload.Value, ct)) : Problem(upload.Error!);
    }

    [HttpGet("{id:guid}/attachment")]
    [HasPermission(P.BidView)]
    public async Task<IActionResult> Attachment(Guid id, CancellationToken ct)
    {
        var file = await service.AttachmentAsync(id, ct);
        return file.IsSuccess ? File(file.Value.Content, "application/pdf", file.Value.FileName) : Problem(file.Error!);
    }

    [HttpPut("{id:guid}/suppliers")]
    [HasPermission(P.BidManage)]
    public async Task<IActionResult> UpsertSupplier(Guid id, BidSupplierRequest request, CancellationToken ct) =>
        FromResult(await service.UpsertSupplierAsync(id, request, ct));

    [HttpDelete("{id:guid}/suppliers/{supplierId:guid}")]
    [HasPermission(P.BidManage)]
    public async Task<IActionResult> RemoveSupplier(Guid id, Guid supplierId, CancellationToken ct) =>
        FromResult(await service.RemoveSupplierAsync(id, supplierId, ct));

    [HttpPost("{id:guid}/items")]
    [HasPermission(P.BidManage)]
    public async Task<ActionResult<Guid>> AddItem(Guid id, BidItemRequest request, CancellationToken ct) =>
        FromResult(await service.AddItemAsync(id, request, ct));

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    [HasPermission(P.BidManage)]
    public async Task<IActionResult> RemoveItem(Guid id, Guid itemId, CancellationToken ct) => FromResult(await service.RemoveItemAsync(id, itemId, ct));

    /// <summary>Đồng bộ danh mục từ HIS theo (Số QĐ + MST) — tất cả NCC hoặc một NCC.</summary>
    [HttpPost("{id:guid}/his-sync")]
    [HasPermission(P.BidManage)]
    public async Task<ActionResult<HisSyncResultDto>> SyncFromHis(Guid id, [FromQuery] Guid? supplierId, CancellationToken ct) =>
        FromResult(await service.SyncFromHisAsync(id, supplierId, ct));

    /// <summary>Kiểm tra tệp import (xem trước, đánh dấu dòng lỗi) — không ghi dữ liệu.</summary>
    [HttpPost("{id:guid}/import/preview")]
    [HasPermission(P.BidManage)]
    [RequestSizeLimit(UploadLimit)]
    public async Task<ActionResult<ImportPreviewDto>> PreviewImport(Guid id, IFormFile? file, CancellationToken ct)
    {
        var upload = await TemplatesController.ReadUploadAsync(file, ct);
        return upload.IsSuccess ? FromResult(await service.PreviewImportAsync(id, upload.Value.Content, ct)) : Problem(upload.Error!);
    }

    [HttpPost("{id:guid}/import")]
    [HasPermission(P.BidManage)]
    [RequestSizeLimit(UploadLimit)]
    public async Task<ActionResult<ImportResultDto>> Import(Guid id, IFormFile? file, CancellationToken ct)
    {
        var upload = await TemplatesController.ReadUploadAsync(file, ct);
        return upload.IsSuccess ? FromResult(await service.CommitImportAsync(id, upload.Value.Content, ct)) : Problem(upload.Error!);
    }

    [HttpPost("{id:guid}/activate")]
    [HasPermission(P.BidManage)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct) => FromResult(await service.ActivateAsync(id, ct));

    [HttpPost("{id:guid}/close")]
    [HasPermission(P.BidManage)]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) => FromResult(await service.CloseAsync(id, ct));
}

public sealed record CancelContractRequest(string? Reason);

/// <summary>Hợp đồng (spec §6.1 B2–B3, §6.6, §9.1-5, §9.2-2). Supplier users see only their own contracts.</summary>
[Route("api/contracts")]
public sealed class ContractsController(ContractService service, IExcelExporter excel) : ApiControllerBase
{
    private const long UploadLimit = FileInspector.MaxUploadBytes + (1024 * 1024);

    [HttpGet]
    [HasPermission(P.ContractView)]
    public Task<PagedResult<ContractListItemDto>> List([FromQuery] PageQuery query, [FromQuery] ContractFilter filter, CancellationToken ct) =>
        service.ListAsync(query, filter, ct);

    [HttpGet("export")]
    [HasPermission(P.ContractView)]
    public async Task<FileContentResult> Export([FromQuery] PageQuery query, [FromQuery] ContractFilter filter, CancellationToken ct)
    {
        var page = await service.ListAsync(query with { Page = 1, PageSize = PageQuery.MaxExportRows, ForExport = true }, filter, ct);
        var bytes = excel.Export("Hợp đồng", [
            new ExcelColumn<ContractListItemDto>("Số hợp đồng", x => x.Number, Width: 18),
            new ExcelColumn<ContractListItemDto>("Tên hợp đồng", x => x.Name, Width: 40),
            new ExcelColumn<ContractListItemDto>("Nhà cung cấp", x => x.SupplierName, Width: 34),
            new ExcelColumn<ContractListItemDto>("Hạng mục", x => x.CategoryName, Width: 16),
            new ExcelColumn<ContractListItemDto>("Hình thức", x => x.ProcurementType == ProcurementType.Bidding ? "Trong thầu" : "Ngoài thầu", Width: 12),
            new ExcelColumn<ContractListItemDto>("Số QĐ thầu", x => x.BidDecisionNumber, Width: 16),
            new ExcelColumn<ContractListItemDto>("Giá trị", x => x.TotalAmount, ExcelColumnFormat.Money, 18),
            new ExcelColumn<ContractListItemDto>("Ngày hiệu lực", x => x.EffectiveDate?.ToDateTime(TimeOnly.MinValue), ExcelColumnFormat.Date, 12),
            new ExcelColumn<ContractListItemDto>("Ngày hết hạn", x => x.ExpiryDate?.ToDateTime(TimeOnly.MinValue), ExcelColumnFormat.Date, 12),
            new ExcelColumn<ContractListItemDto>("Đã nhập kho (%)", x => x.ReceivedPercent, ExcelColumnFormat.Number, 12),
            new ExcelColumn<ContractListItemDto>("Trạng thái", x => ContractStatusText(x.Status), Width: 20),
        ], page.Items);
        return Excel(bytes, "hop-dong.xlsx");
    }

    [HttpGet("{id:guid}")]
    [HasPermission(P.ContractView)]
    public async Task<ActionResult<ContractDto>> Get(Guid id, CancellationToken ct) => FromResult(await service.GetAsync(id, ct));

    [HttpGet("{id:guid}/history")]
    [HasPermission(P.ContractView)]
    public async Task<ActionResult<List<ContractHistoryEntryDto>>> History(Guid id, CancellationToken ct) =>
        FromResult(await service.HistoryAsync(id, ct));

    [HttpPost]
    [HasPermission(P.ContractCreate)]
    public async Task<ActionResult<Guid>> Create(SaveContractRequest request, CancellationToken ct) => FromResult(await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(P.ContractCreate)]
    public async Task<IActionResult> Update(Guid id, SaveContractRequest request, CancellationToken ct) => FromResult(await service.UpdateAsync(id, request, ct));

    /// <summary>Xem trước PDF của hợp đồng đang soạn (wizard bước 4).</summary>
    [HttpPost("preview")]
    [HasPermission(P.ContractCreate)]
    [Produces("application/pdf", "application/json")]
    public async Task<IActionResult> Preview(SaveContractRequest request, CancellationToken ct)
    {
        var pdf = await service.PreviewAsync(request, ct);
        return pdf.IsSuccess ? File(pdf.Value, "application/pdf", "xem-truoc-hop-dong.pdf") : Problem(pdf.Error!);
    }

    [HttpPost("{id:guid}/submit")]
    [HasPermission(P.ContractCreate)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct) => FromResult(await service.SubmitAsync(id, ct));

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(P.ContractCreate)]
    public async Task<IActionResult> Cancel(Guid id, CancelContractRequest request, CancellationToken ct) =>
        FromResult(await service.CancelAsync(id, request.Reason, ct));

    /// <summary>NCC: "Đồng ý ký" hoặc "Gửi lại" (có sửa đổi).</summary>
    [HttpPost("{id:guid}/supplier-response")]
    [HasPermission(P.ContractSupplierReview)]
    public async Task<IActionResult> SupplierResponse(Guid id, SupplierResponseRequest request, CancellationToken ct) =>
        FromResult(await service.SupplierRespondAsync(id, request, ct));

    /// <summary>NCC tải lên bản DOCX đã sửa (bắt buộc Track Changes).</summary>
    [HttpPost("{id:guid}/supplier-revision")]
    [HasPermission(P.ContractSupplierReview)]
    [RequestSizeLimit(UploadLimit)]
    public async Task<ActionResult<Guid>> SupplierRevision(Guid id, IFormFile? file, CancellationToken ct)
    {
        var upload = await TemplatesController.ReadUploadAsync(file, ct);
        return upload.IsSuccess ? FromResult(await service.UploadSupplierRevisionAsync(id, upload.Value, ct)) : Problem(upload.Error!);
    }

    [HttpGet("{id:guid}/revisions")]
    [HasPermission(P.ContractView)]
    public async Task<ActionResult<RevisionListDto>> Revisions(Guid id, CancellationToken ct) => FromResult(await service.RevisionsAsync(id, ct));

    /// <summary>Kế toán chấp nhận / từ chối từng thay đổi.</summary>
    [HttpPost("{id:guid}/revisions")]
    [HasPermission(P.ContractAccountantReview)]
    public async Task<ActionResult<Guid>> ResolveRevisions(Guid id, ResolveRevisionsRequest request, CancellationToken ct) =>
        FromResult(await service.ResolveRevisionsAsync(id, request, ct));

    [HttpPost("{id:guid}/appendices")]
    [HasPermission(P.ContractCreate)]
    public async Task<ActionResult<Guid>> CreateAppendix(Guid id, SaveAppendixRequest request, CancellationToken ct) =>
        FromResult(await service.CreateAppendixAsync(id, request, ct));

    [HttpPut("appendices/{appendixId:guid}")]
    [HasPermission(P.ContractCreate)]
    public async Task<IActionResult> UpdateAppendix(Guid appendixId, SaveAppendixRequest request, CancellationToken ct) =>
        FromResult(await service.UpdateAppendixAsync(appendixId, request, ct));

    [HttpPost("appendices/{appendixId:guid}/submit")]
    [HasPermission(P.ContractCreate)]
    public async Task<IActionResult> SubmitAppendix(Guid appendixId, CancellationToken ct) => FromResult(await service.SubmitAppendixAsync(appendixId, ct));

    [HttpPost("appendices/{appendixId:guid}/cancel")]
    [HasPermission(P.ContractCreate)]
    public async Task<IActionResult> CancelAppendix(Guid appendixId, CancellationToken ct) => FromResult(await service.CancelAppendixAsync(appendixId, ct));

    private static string ContractStatusText(ContractStatus status) => status switch
    {
        ContractStatus.Draft => "Bản nháp",
        ContractStatus.InReview => "Tổ rà soát kiểm tra",
        ContractStatus.SupplierReview => "NCC rà soát",
        ContractStatus.AccountantReview => "Kế toán xem thay đổi",
        ContractStatus.SupplierSigning => "NCC ký số",
        ContractStatus.AccountantInitialing => "Kế toán ký nháy",
        ContractStatus.DirectorSigning => "Giám đốc ký",
        ContractStatus.Stamping => "Văn thư đóng dấu",
        ContractStatus.Effective => "Đang hiệu lực",
        ContractStatus.Liquidated => "Đã thanh lý",
        ContractStatus.Cancelled => "Đã hủy",
        _ => "Bị trả lại",
    };
}
