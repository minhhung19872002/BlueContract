using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Notifications;
using BlueContract.Application.Proposals;
using BlueContract.Application.Receipts.Validation;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Receipts;
using BlueContract.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Receipts;

/// <summary>
/// Phiếu nhập kho (spec §6.4, §9.1-10, §9.2-5): list / detail with warnings, invoice (NCC uploads the e-invoice XML),
/// portal receipts and virtual receipts (phiếu xác nhận khối lượng), re-validation, re-submission after a return.
/// </summary>
public sealed class ReceiptService(
    IAppDbContext db,
    DocumentNumbers numbers,
    ReceiptValidationLoader validation,
    ReceiptProcessing processing,
    IFileStorage storage,
    NotificationService notifications,
    IAuditLogger audit,
    ICurrentUser current,
    IClock clock,
    DataScope dataScope,
    Workflows.WorkflowAccess workflowAccess)
{
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(5);

    public async Task<PagedResult<ReceiptListItemDto>> ListAsync(PageQuery q, ReceiptFilter f, CancellationToken ct)
    {
        var query = await dataScope.ReceiptsAsync(db.WarehouseReceipts.AsNoTracking(), ct);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            query = query.Where(r => EF.Functions.ILike(r.Number, p) || (r.ContractNumber != null && EF.Functions.ILike(r.ContractNumber, p))
                || (r.InvoiceNumber != null && EF.Functions.ILike(r.InvoiceNumber, p)) || r.Lines.Any(l => EF.Functions.ILike(l.ItemName, p)));
        }

        query = f.Status is { } status ? query.Where(r => r.Status == status) : query;
        query = f.Validation is { } validationStatus ? query.Where(r => r.ValidationStatus == validationStatus) : query;
        query = f.Type is { } type ? query.Where(r => r.Type == type) : query;
        query = f.Source is { } source ? query.Where(r => r.Source == source) : query;
        query = f.SupplierId is { } supplier ? query.Where(r => r.SupplierId == supplier) : query;
        query = f.WarehouseId is { } warehouse ? query.Where(r => r.WarehouseId == warehouse) : query;
        query = f.ContractId is { } contract ? query.Where(r => r.ContractId == contract) : query;
        query = f.From is { } from ? query.Where(r => r.ReceiptDate >= from) : query;
        query = f.To is { } to ? query.Where(r => r.ReceiptDate <= to) : query;
        query = f.HasInvoice is { } hasInvoice ? query.Where(r => (r.InvoiceNumber != null) == hasInvoice) : query;
        query = f.Accepted is { } accepted ? query.Where(r => r.IsIncludedInAcceptance == accepted) : query;

        return await query.OrderByDescending(r => r.ReceiptDate).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Select(r => new ReceiptListItemDto(
                r.Id, r.Number, r.Type, r.Source, r.ReceiptDate,
                db.Warehouses.Where(w => w.Id == r.WarehouseId).Select(w => w.Name).FirstOrDefault() ?? r.WarehouseCode,
                r.SupplierId,
                db.Suppliers.Where(s => s.Id == r.SupplierId).Select(s => s.Name).FirstOrDefault() ?? ("MST " + r.SupplierTaxCode),
                r.ContractId, r.ContractNumber, r.InvoiceNumber,
                r.Lines.Sum(l => l.Amount), r.Status, r.ValidationStatus, r.IssueCount, r.InvoiceNumber != null, r.IsIncludedInAcceptance, r.CreatedAt))
            .ToPagedAsync(q, ct);
    }

    public async Task<Result<ReceiptDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var r = await (await dataScope.ReceiptsAsync(db.WarehouseReceipts.AsNoTracking(), ct)).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? await ParticipantReceiptAsync(id, ct);
        return r is null ? Error.NotFound() : await ToDtoAsync(r, ct);
    }

    /// <summary>A signer assigned to the receipt's workflow may open it even outside their data scope.</summary>
    private async Task<WarehouseReceipt?> ParticipantReceiptAsync(Guid id, CancellationToken ct)
    {
        var r = await db.WarehouseReceipts.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        return r?.DocumentId is { } docId && await workflowAccess.IsParticipantAsync(docId, ct) ? r : null;
    }

    /// <summary>Lập phiếu nhập kho / hoàn trả trên cổng, hoặc phiếu ảo cho hợp đồng không nhập kho.</summary>
    public async Task<Result<Guid>> CreateAsync(SaveReceiptRequest r, CancellationToken ct)
    {
        var contract = await db.Contracts.AsNoTracking().Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == r.ContractId, ct);
        if (contract is null)
        {
            return Error.Validation("CONTRACT_NOT_FOUND");
        }

        var built = await BuildAsync(r, contract, ct);
        if (!built.IsSuccess)
        {
            return built.Error!;
        }

        var (warehouseCode, warehouseId, lines) = built.Value;
        var bid = contract.BidDecisionId is { } bidId ? await db.BidDecisions.Where(b => b.Id == bidId).Select(b => b.Number).FirstOrDefaultAsync(ct) : null;
        var taxCode = await db.Suppliers.Where(s => s.Id == contract.SupplierId).Select(s => s.TaxCode).FirstAsync(ct);
        var receipt = new WarehouseReceipt(
            await numbers.NextAsync(r.Type == ReceiptType.Return ? DocumentNumbers.Return : DocumentNumbers.Receipt, ct),
            r.Type, r.Virtual ? ReceiptSource.VirtualPortal : ReceiptSource.Portal, null, r.ReceiptDate, warehouseCode, warehouseId, taxCode, bid);
        receipt.UpdateHeader(receipt.Number, r.ReceiptDate, warehouseCode, warehouseId, taxCode, bid, r.DelivererName, r.Note);
        receipt.SetLines(lines);
        db.WarehouseReceipts.Add(receipt);
        await ApplyMappingAsync(receipt, contract, r.CallOrderId, ct);

        var generated = await processing.GenerateDocumentAsync(receipt, receipt.IsVirtual ? "Lập phiếu xác nhận khối lượng" : "Lập phiếu trên cổng", ct);
        if (!generated.IsSuccess)
        {
            return generated.Error!;
        }

        audit.Log("ReceiptCreate", nameof(WarehouseReceipt), receipt.Id.ToString(), new { receipt.Number, receipt.Source, receipt.Type, ContractNumber = contract.Number });
        await db.SaveChangesAsync(ct);
        return receipt.Id;
    }

    public async Task<Result> UpdateAsync(Guid id, SaveReceiptRequest r, CancellationToken ct)
    {
        var receipt = await db.WarehouseReceipts.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (receipt is null || receipt.Source == ReceiptSource.His)
        {
            return Result.Failure(Error.NotFound());
        }

        if (receipt.Status is not (ReceiptStatus.Draft or ReceiptStatus.Returned) || receipt.Type != r.Type || receipt.IsVirtual != r.Virtual || receipt.ContractId != r.ContractId)
        {
            return Result.Failure(Error.Conflict("RECEIPT_LOCKED"));
        }

        var contract = await db.Contracts.AsNoTracking().Include(c => c.Items).FirstAsync(c => c.Id == r.ContractId, ct);
        var built = await BuildAsync(r, contract, ct);
        if (!built.IsSuccess)
        {
            return Result.Failure(built.Error!);
        }

        var previousItems = receipt.Lines.Where(l => l.ContractItemId is not null).Select(l => l.ContractItemId!.Value).ToList();
        var previousOrder = receipt.CallOrderId;
        var (warehouseCode, warehouseId, lines) = built.Value;
        receipt.UpdateHeader(receipt.Number, r.ReceiptDate, warehouseCode, warehouseId, receipt.SupplierTaxCode, receipt.BidDecisionNumber, r.DelivererName, r.Note);
        receipt.SetLines(lines);
        await ApplyMappingAsync(receipt, contract, r.CallOrderId, ct);
        var generated = await processing.GenerateDocumentAsync(receipt, "Cập nhật phiếu", ct);
        if (!generated.IsSuccess)
        {
            return generated;
        }

        await db.SaveChangesAsync(ct);
        await processing.RecalculateAsync(previousItems.Concat(ItemsOf(receipt)), [previousOrder, receipt.CallOrderId], ct);
        return Result.Success();
    }

    /// <summary>
    /// "Gửi ký" (portal receipt) / "Gửi ký lại" (after a return, re-validated) / "Xác nhận" (virtual receipt: completed
    /// without signatures).
    /// </summary>
    public async Task<Result> SubmitAsync(Guid id, CancellationToken ct)
    {
        var receipt = await db.WarehouseReceipts.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (receipt is null)
        {
            return Result.Failure(Error.NotFound());
        }

        if (receipt.Status is not (ReceiptStatus.Draft or ReceiptStatus.Returned))
        {
            return Result.Failure(Error.Conflict("RECEIPT_NOT_SUBMITTABLE"));
        }

        await validation.ValidateAsync(receipt, ct);
        if (receipt.IsVirtual)
        {
            receipt.CompleteVirtual(clock.UtcNow);
            if (receipt.DocumentId is { } docId)
            {
                (await db.Documents.FirstAsync(d => d.Id == docId, ct)).Complete();
            }

            audit.Log("ReceiptVirtualConfirm", nameof(WarehouseReceipt), receipt.Id.ToString(), new { receipt.Number });
            await db.SaveChangesAsync(ct);
            await processing.RecalculateAsync(ItemsOf(receipt), [receipt.CallOrderId], ct);
            return Result.Success();
        }

        if (receipt.ValidationStatus == ReceiptValidationStatus.Invalid && receipt.Source == ReceiptSource.Portal)
        {
            await db.SaveChangesAsync(ct);
            return Result.Failure(Error.Validation("RECEIPT_INVALID_BLOCKS_SIGNING"));
        }

        var regenerated = await processing.GenerateDocumentAsync(receipt, "Gửi ký lại sau khi bổ sung", ct);
        if (!regenerated.IsSuccess)
        {
            return regenerated;
        }

        var (startedBy, startedByName) = receipt.Source == ReceiptSource.His
            ? await processing.HisServiceUserAsync(ct)
            : (current.UserId!.Value, current.FullName ?? string.Empty);
        await db.SaveChangesAsync(ct);
        var started = await processing.StartSigningAsync(receipt, startedBy, startedByName, ct);
        if (!started.IsSuccess)
        {
            return started;
        }

        await db.SaveChangesAsync(ct);
        await processing.RecalculateAsync(ItemsOf(receipt), [receipt.CallOrderId], ct);
        return Result.Success();
    }

    /// <summary>Bổ sung / sửa thông tin hóa đơn (NCC ở bước người giao hàng, hoặc bệnh viện) — re-runs the validation engine.</summary>
    public Task<Result<ReceiptDto>> SetInvoiceAsync(Guid id, InvoiceRequest r, CancellationToken ct) => InvoiceLockedAsync(id, () => SetInvoiceLockedAsync(id, r, ct), ct);

    private async Task<Result<ReceiptDto>> SetInvoiceLockedAsync(Guid id, InvoiceRequest r, CancellationToken ct)
    {
        var receipt = await db.WarehouseReceipts.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (receipt is null)
        {
            return Error.NotFound();
        }

        if (!CanEditInvoice(receipt))
        {
            return Error.Conflict("RECEIPT_LOCKED");
        }

        receipt.SetInvoice(new InvoiceInfo(r.Symbol, r.Number, r.Date, r.AmountBeforeVat, r.VatAmount, r.TotalAmount, r.SellerTaxCode), current.FullName, clock.UtcNow);
        return await AfterInvoiceAsync(receipt, ct);
    }

    /// <summary>Tải file hóa đơn: XML hóa đơn điện tử (tự đọc số/ký hiệu/ngày/tổng tiền) hoặc PDF bản thể hiện.</summary>
    public Task<Result<ReceiptDto>> UploadInvoiceFileAsync(Guid id, UploadedFile file, CancellationToken ct) => InvoiceLockedAsync(id, () => UploadInvoiceFileLockedAsync(id, file, ct), ct);

    private async Task<Result<ReceiptDto>> UploadInvoiceFileLockedAsync(Guid id, UploadedFile file, CancellationToken ct)
    {
        var receipt = await db.WarehouseReceipts.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (receipt is null)
        {
            return Error.NotFound();
        }

        if (!CanEditInvoice(receipt))
        {
            return Error.Conflict("RECEIPT_LOCKED");
        }

        var kind = FileInspector.Detect(file.Content);
        if (kind is not (FileKind.Xml or FileKind.Pdf))
        {
            return Error.Validation("INVOICE_FILE_TYPE_INVALID");
        }

        if (kind == FileKind.Xml)
        {
            var parsed = InvoiceXmlParser.TryParse(file.Content);
            if (parsed is null)
            {
                return Error.Validation("INVOICE_XML_INVALID");
            }

            using var stream = new MemoryStream(file.Content);
            var stored = await storage.UploadAsync($"receipts/{receipt.Id:N}/invoice-{clock.UtcNow:yyyyMMddHHmmss}.xml", stream, "application/xml", ct);
            receipt.SetInvoice(parsed.Invoice, current.FullName, clock.UtcNow);
            receipt.AttachInvoiceFiles(stored.ObjectKey, null);
        }
        else
        {
            using var stream = new MemoryStream(file.Content);
            var stored = await storage.UploadAsync($"receipts/{receipt.Id:N}/invoice-{clock.UtcNow:yyyyMMddHHmmss}.pdf", stream, "application/pdf", ct);
            receipt.AttachInvoiceFiles(null, stored.ObjectKey);
        }

        return await AfterInvoiceAsync(receipt, ct);
    }

    /// <summary>Invoice changes and re-validation of a supplier's receipts run one at a time (see <see cref="ReceiptLocks"/>).</summary>
    private async Task<Result<ReceiptDto>> InvoiceLockedAsync(Guid id, Func<Task<Result<ReceiptDto>>> work, CancellationToken ct)
    {
        var taxCode = await db.WarehouseReceipts.AsNoTracking().Where(x => x.Id == id).Select(x => x.SupplierTaxCode).FirstOrDefaultAsync(ct);
        return await db.SerializedAsync([ReceiptLocks.Invoices(taxCode)], work, ct);
    }

    /// <summary>Đọc thử XML hóa đơn (không lưu) để điền sẵn form.</summary>
    public static Result<ParsedInvoiceDto> ParseInvoice(UploadedFile file)
    {
        var parsed = FileInspector.Detect(file.Content) == FileKind.Xml ? InvoiceXmlParser.TryParse(file.Content) : null;
        if (parsed is null)
        {
            return Error.Validation("INVOICE_XML_INVALID");
        }

        var i = parsed.Invoice;
        return new ParsedInvoiceDto(new InvoiceRequest(i.Symbol, i.Number, i.Date, i.AmountBeforeVat, i.VatAmount, i.TotalAmount, i.SellerTaxCode),
            parsed.SellerName, parsed.BuyerTaxCode, parsed.BuyerName);
    }

    public async Task<Result<FileLinkDto>> InvoiceFileLinkAsync(Guid id, string format, CancellationToken ct)
    {
        var receipt = await db.WarehouseReceipts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        var key = format == "xml" ? receipt?.InvoiceXmlFileKey : receipt?.InvoicePdfFileKey;
        if (receipt is null || key is null)
        {
            return Error.NotFound();
        }

        audit.Log("ReceiptInvoiceDownload", nameof(WarehouseReceipt), receipt.Id.ToString(), new { format });
        await db.SaveChangesAsync(ct);
        var expires = clock.UtcNow.Add(LinkLifetime);
        return new FileLinkDto(await storage.GetPresignedDownloadUrlAsync(key, LinkLifetime, ct), $"hoa-don-{receipt.InvoiceNumber ?? receipt.Number}.{format}", expires);
    }

    /// <summary>"Kiểm tra lại": re-maps (HĐ/mặt hàng may have changed, e.g. after an appendix) and re-runs every rule.</summary>
    public Task<Result<ReceiptDto>> RevalidateAsync(Guid id, CancellationToken ct) => InvoiceLockedAsync(id, () => RevalidateLockedAsync(id, ct), ct);

    private async Task<Result<ReceiptDto>> RevalidateLockedAsync(Guid id, CancellationToken ct)
    {
        var receipt = await db.WarehouseReceipts.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (receipt is null)
        {
            return Error.NotFound();
        }

        if (receipt.Status is ReceiptStatus.Completed or ReceiptStatus.Cancelled)
        {
            return Error.Conflict("RECEIPT_LOCKED");
        }

        var previousItems = ItemsOf(receipt).ToList();
        var previousOrder = receipt.CallOrderId;
        if (receipt.Source == ReceiptSource.His && !await processing.HasSignaturesAsync(receipt, ct))
        {
            await validation.MapAsync(receipt, receipt.ContractNumber, null, ct);
        }

        await validation.ValidateAsync(receipt, ct);
        await db.SaveChangesAsync(ct);
        await processing.RecalculateAsync(previousItems.Concat(ItemsOf(receipt)), [previousOrder, receipt.CallOrderId], ct);
        return await ToDtoAsync(receipt, ct);
    }

    public async Task<Result> CancelAsync(Guid id, string? reason, CancellationToken ct)
    {
        var receipt = await db.WarehouseReceipts.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (receipt is null)
        {
            return Result.Failure(Error.NotFound());
        }

        if (receipt.Status == ReceiptStatus.Signing || await processing.HasSignaturesAsync(receipt, ct) && receipt.Status != ReceiptStatus.Returned)
        {
            return Result.Failure(Error.Conflict("RECEIPT_NOT_CANCELLABLE"));
        }

        receipt.Cancel();
        if (receipt.DocumentId is { } docId)
        {
            var wf = await db.WorkflowInstances.Include("_steps").Include("_events")
                .FirstOrDefaultAsync(w => w.DocumentId == docId && w.Status == WorkflowStatus.Returned, ct);
            wf?.Cancel(current.UserId!.Value, current.FullName ?? string.Empty, reason ?? "Hủy phiếu", clock.UtcNow);
            (await db.Documents.FirstAsync(d => d.Id == docId, ct)).Cancel();
        }

        audit.Log("ReceiptCancel", nameof(WarehouseReceipt), receipt.Id.ToString(), new { receipt.Number }, reason);
        await db.SaveChangesAsync(ct);
        await processing.RecalculateAsync(ItemsOf(receipt), [receipt.CallOrderId], ct);
        return Result.Success();
    }

    private async Task<Result<ReceiptDto>> AfterInvoiceAsync(WarehouseReceipt receipt, CancellationToken ct)
    {
        await validation.ValidateAsync(receipt, ct);
        var generated = await processing.GenerateDocumentAsync(receipt, "Bổ sung thông tin hóa đơn", ct);
        if (!generated.IsSuccess)
        {
            return generated.Error!;
        }

        audit.Log("ReceiptInvoice", nameof(WarehouseReceipt), receipt.Id.ToString(), new { receipt.InvoiceSymbol, receipt.InvoiceNumber, receipt.InvoiceTotalAmount });
        if (current.IsSupplier)
        {
            var managers = await db.Users
                .Where(u => u.Status == UserStatus.Active && u.UserType == UserType.Internal
                    && u.Roles.Any(x => x.Role!.Permissions.Any(p => p.PermissionCode == Permissions.ReceiptManage)))
                .Select(u => u.Id).ToListAsync(ct);
            await notifications.NotifyAsync(managers, NotificationTypes.ReceiptInvoice, $"NCC bổ sung hóa đơn cho {ReceiptProcessing.Title(receipt)}",
                $"Hóa đơn {receipt.InvoiceSymbol} số {receipt.InvoiceNumber}", $"/nhap-kho/{receipt.Id}", email: false, ct);
        }

        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(receipt, ct);
    }

    private async Task<Result<(string WarehouseCode, Guid? WarehouseId, List<ReceiptLineDraft> Lines)>> BuildAsync(SaveReceiptRequest r, Contract contract, CancellationToken ct)
    {
        if (contract.Status != ContractStatus.Effective)
        {
            return Error.Validation("CONTRACT_NOT_EFFECTIVE");
        }

        // A virtual receipt skips the five warehouse signatures, so it is only for contracts that never go through a warehouse.
        if (r.Virtual && contract.RequiresWarehouseReceipt)
        {
            return Error.Validation("RECEIPT_VIRTUAL_NOT_ALLOWED");
        }

        if (r.Virtual && r.Type == ReceiptType.Return)
        {
            return Error.Validation("RECEIPT_VIRTUAL_RETURN");
        }

        string warehouseCode;
        Guid? warehouseId = null;
        if (r.WarehouseId is { } wid)
        {
            var warehouse = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == wid, ct);
            if (warehouse is null)
            {
                return Error.Validation("WAREHOUSE_NOT_FOUND");
            }

            warehouseCode = warehouse.HisWarehouseCode ?? warehouse.Code;
            warehouseId = warehouse.Id;
        }
        else
        {
            warehouseCode = "KHONG_QUA_KHO";
        }

        var itemIds = contract.Items.Select(i => i.ItemId).ToList();
        var keys = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.HisKey, ct);
        var lines = new List<ReceiptLineDraft>();
        foreach (var line in r.Lines)
        {
            var item = contract.Items.FirstOrDefault(i => i.Id == line.ContractItemId);
            if (item is null)
            {
                return Error.Validation("RECEIPT_ITEM_NOT_IN_CONTRACT");
            }

            lines.Add(new ReceiptLineDraft(keys.GetValueOrDefault(item.ItemId), item.ItemCode, item.ItemName, item.Unit, line.LotNumber, line.ExpiryDate,
                line.Quantity, line.UnitPrice ?? item.UnitPrice, item.VatRate));
        }

        return (warehouseCode, warehouseId, lines);
    }

    /// <summary>Portal receipts are created from a chosen contract: the mapping is exact, then the engine runs.</summary>
    private async Task ApplyMappingAsync(WarehouseReceipt receipt, Contract contract, Guid? callOrderId, CancellationToken ct)
    {
        foreach (var line in receipt.Lines)
        {
            var item = contract.Items.First(i => i.ItemCode == line.ItemCode && i.ItemName == line.ItemName);
            receipt.MapLine(line.LineNo, item.ItemId, item.Id);
        }

        var orderId = callOrderId is { } oid && await db.CallOrders.AnyAsync(o => o.Id == oid && o.SupplierId == contract.SupplierId, ct) ? oid : (Guid?)null;
        receipt.MapParties(contract.SupplierId, contract.Id, contract.Number, receipt.Type == ReceiptType.Return ? null : orderId);
        await validation.ValidateAsync(receipt, ct);
    }

    private bool CanEditInvoice(WarehouseReceipt r) =>
        r.Status is not (ReceiptStatus.Completed or ReceiptStatus.Cancelled)
        && (current.IsSupplier ? current.HasPermission(Permissions.ReceiptInvoice) && r.SupplierId == current.SupplierId : current.HasPermission(Permissions.ReceiptManage));

    private static IEnumerable<Guid> ItemsOf(WarehouseReceipt r) =>
        r.Lines.Where(l => l.ContractItemId is not null).Select(l => l.ContractItemId!.Value);

    private async Task<ReceiptDto> ToDtoAsync(WarehouseReceipt r, CancellationToken ct)
    {
        var warehouse = r.WarehouseId is { } wid ? await db.Warehouses.AsNoTracking().Where(w => w.Id == wid).Select(w => w.Name).FirstOrDefaultAsync(ct) : null;
        var supplier = r.SupplierId is { } sid ? await db.Suppliers.AsNoTracking().Where(s => s.Id == sid).Select(s => s.Name).FirstOrDefaultAsync(ct) : null;
        var callOrder = r.CallOrderId is { } oid ? await db.CallOrders.AsNoTracking().Where(o => o.Id == oid).Select(o => o.Number).FirstOrDefaultAsync(ct) : null;
        var contractItems = r.ContractId is { } cid
            ? await db.Contracts.AsNoTracking().Where(c => c.Id == cid).SelectMany(c => c.Items)
                .Select(i => new { i.Id, i.UnitPrice, Remaining = i.Quantity - i.ReceivedQuantity }).ToDictionaryAsync(i => i.Id, ct)
            : [];
        var manage = !current.IsSupplier && current.HasPermission(Permissions.ReceiptManage);
        var open = r.Status is ReceiptStatus.Draft or ReceiptStatus.Returned;

        return new ReceiptDto(
            r.Id, r.Number, r.Type, r.Source, r.HisReceiptId, r.ReceiptDate, r.WarehouseCode, r.WarehouseId, warehouse ?? r.WarehouseCode,
            r.SupplierId, supplier ?? $"MST {r.SupplierTaxCode}", r.SupplierTaxCode, r.BidDecisionNumber, r.ContractId, r.ContractNumber, r.CallOrderId, callOrder,
            r.DelivererName, r.Note,
            new ReceiptInvoiceDto(r.InvoiceSymbol, r.InvoiceNumber, r.InvoiceDate, r.InvoiceAmountBeforeVat, r.InvoiceVatAmount, r.InvoiceTotalAmount,
                r.InvoiceSellerTaxCode, r.InvoiceXmlFileKey is not null, r.InvoicePdfFileKey is not null, r.InvoiceUpdatedAt, r.InvoiceUpdatedByName),
            r.Status, r.ValidationStatus, r.ValidatedAt,
            [.. r.Issues.Select(i => new ReceiptIssueDto(i.Code, i.Severity, i.Message, i.LineNo))],
            r.DocumentId, r.CompletedAt, r.LastReturnReason, r.IsIncludedInAcceptance, r.TotalAmount, r.VatAmount,
            [.. r.Lines.OrderBy(l => l.LineNo).Select(l =>
            {
                var ci = l.ContractItemId is { } id ? contractItems.GetValueOrDefault(id) : null;
                return new ReceiptLineDto(l.Id, l.LineNo, l.HisKey, l.ItemCode, l.ItemName, l.Unit, l.LotNumber, l.ExpiryDate, l.Quantity, l.UnitPrice, l.VatRate,
                    l.Amount, l.VatAmount, l.ContractItemId, ci?.UnitPrice, ci?.Remaining);
            })],
            CanEdit: manage && r.Source != ReceiptSource.His && open,
            CanEditInvoice: CanEditInvoice(r),
            CanSubmit: manage && open && (r.Source != ReceiptSource.His || r.Status == ReceiptStatus.Returned) && r.SupplierId is not null,
            CanCancel: manage && r.Status is ReceiptStatus.Draft or ReceiptStatus.Returned && !r.IsIncludedInAcceptance,
            CanRevalidate: manage && r.Status is not (ReceiptStatus.Completed or ReceiptStatus.Cancelled));
    }
}
