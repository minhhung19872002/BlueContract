using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Notifications;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Proposals;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Proposals;

public sealed record CallOrderListItemDto(
    Guid Id,
    string Number,
    Guid SupplierId,
    string SupplierName,
    string SummaryNumber,
    string WarehouseName,
    DateOnly CallDate,
    DateOnly DeliveryDeadline,
    CallOrderStatus Status,
    decimal TotalAmount,
    int LineCount,
    bool IsOverdue,
    DateTimeOffset? ConfirmedAt);

public sealed record CallOrderFilter(CallOrderStatus? Status, Guid? SupplierId, Guid? SummaryId, bool? Overdue, Guid? ContractId = null);

public sealed record CallOrderLineDto(
    Guid Id,
    string ContractNumber,
    string ItemCode,
    string ItemName,
    string Unit,
    decimal UnitPrice,
    decimal Quantity,
    decimal? ConfirmedQuantity,
    string? ShortageReason,
    decimal DeliveredQuantity,
    decimal Amount);

public sealed record CallOrderDto(
    Guid Id,
    string Number,
    Guid SummaryId,
    string SummaryNumber,
    Guid SupplierId,
    string SupplierName,
    Guid WarehouseId,
    string WarehouseName,
    DateOnly CallDate,
    DateOnly DeliveryDeadline,
    string? ContactName,
    string? Note,
    CallOrderStatus Status,
    Guid? DocumentId,
    DateTimeOffset? ConfirmedAt,
    string? ConfirmedByName,
    string? SupplierNote,
    bool IsOverdue,
    decimal TotalAmount,
    IReadOnlyList<CallOrderLineDto> Lines,
    bool CanConfirm,
    bool CanCancel);

public sealed record CallLineRequest(Guid SummaryLineId, decimal Quantity);

public sealed record CreateCallsRequest(Guid SummaryId, Guid WarehouseId, DateOnly DeliveryDeadline, string? ContactName, string? Note, IReadOnlyList<CallLineRequest> Lines);

public sealed record ConfirmLineRequest(Guid LineId, decimal ConfirmedQuantity, string? ShortageReason);

public sealed record ConfirmCallRequest(IReadOnlyList<ConfirmLineRequest> Lines, string? Note);

internal sealed class CreateCallsRequestValidator : AbstractValidator<CreateCallsRequest>
{
    public CreateCallsRequestValidator()
    {
        RuleFor(x => x.SummaryId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty().WithName("Kho nhận");
        RuleFor(x => x.DeliveryDeadline).NotEmpty().WithName("Hạn giao");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Nhập SL gọi cho ít nhất một mặt hàng.");
        RuleForEach(x => x.Lines).ChildRules(l => l.RuleFor(x => x.Quantity).GreaterThan(0).WithName("SL gọi"));
    }
}

/// <summary>Gọi hàng (spec §6.3): từ tổng hợp đã duyệt, tự tách theo NCC, email NCC, NCC xác nhận SL, theo dõi giao hàng.</summary>
public sealed class CallOrderService(
    IAppDbContext db,
    DocumentNumbers numbers,
    ContractTracking tracking,
    ProposalDocumentData documentData,
    TemplateService templates,
    DocumentService documents,
    NotificationService notifications,
    IAuditLogger audit,
    ICurrentUser current,
    IClock clock,
    DataScope dataScope)
{
    public const string OwnerType = "CallOrder";

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);

    public async Task<PagedResult<CallOrderListItemDto>> ListAsync(PageQuery q, CallOrderFilter f, CancellationToken ct)
    {
        var query = db.CallOrders.AsNoTracking();
        if (await dataScope.ContractIdsAsync(ct) is { } scoped)
        {
            // An order is shown when one of its contracts is in the user's scope.
            query = query.Where(o => o.Lines.Any(l => scoped.Contains(l.ContractId)));
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            query = query.Where(o => EF.Functions.ILike(o.Number, p) || o.Lines.Any(l => EF.Functions.ILike(l.ItemName, p)));
        }

        if (f.Status is not null)
        {
            query = query.Where(o => o.Status == f.Status);
        }

        if (f.SupplierId is not null)
        {
            query = query.Where(o => o.SupplierId == f.SupplierId);
        }

        if (f.SummaryId is not null)
        {
            query = query.Where(o => o.SummaryId == f.SummaryId);
        }

        if (f.ContractId is not null)
        {
            query = query.Where(o => o.Lines.Any(l => l.ContractId == f.ContractId));
        }

        var today = Today;
        if (f.Overdue == true)
        {
            query = query.Where(o => o.Status == CallOrderStatus.Sent && o.DeliveryDeadline < today);
        }

        return await query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Select(o => new CallOrderListItemDto(
                o.Id, o.Number, o.SupplierId,
                db.Suppliers.Where(s => s.Id == o.SupplierId).Select(s => s.Name).FirstOrDefault() ?? string.Empty,
                db.ProposalSummaries.Where(s => s.Id == o.SummaryId).Select(s => s.Number).FirstOrDefault() ?? string.Empty,
                db.Warehouses.Where(w => w.Id == o.WarehouseId).Select(w => w.Name).FirstOrDefault() ?? string.Empty,
                o.CallDate, o.DeliveryDeadline, o.Status,
                o.Lines.Sum(l => (l.ConfirmedQuantity ?? l.Quantity) * l.UnitPrice), o.Lines.Count,
                o.Status == CallOrderStatus.Sent && o.DeliveryDeadline < today, o.ConfirmedAt))
            .ToPagedAsync(q, ct);
    }

    public async Task<Result<CallOrderDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var o = await db.CallOrders.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null || (await dataScope.ContractIdsAsync(ct) is { } scoped && !await scoped.AnyAsync(c => o.Lines.Select(l => l.ContractId).Contains(c), ct)))
        {
            return Error.NotFound();
        }

        var supplier = await db.Suppliers.AsNoTracking().Where(s => s.Id == o.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        var summary = await db.ProposalSummaries.AsNoTracking().Where(s => s.Id == o.SummaryId).Select(s => s.Number).FirstOrDefaultAsync(ct);
        var warehouse = await db.Warehouses.AsNoTracking().Where(w => w.Id == o.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct);
        return new CallOrderDto(o.Id, o.Number, o.SummaryId, summary ?? string.Empty, o.SupplierId, supplier ?? string.Empty, o.WarehouseId, warehouse ?? string.Empty,
            o.CallDate, o.DeliveryDeadline, o.ContactName, o.Note, o.Status, o.DocumentId, o.ConfirmedAt, o.ConfirmedByName, o.SupplierNote, o.IsOverdue(Today),
            o.TotalAmount,
            [.. o.Lines.OrderBy(l => l.ItemName).Select(l => new CallOrderLineDto(l.Id, l.ContractNumber, l.ItemCode, l.ItemName, l.Unit, l.UnitPrice, l.Quantity,
                l.ConfirmedQuantity, l.ShortageReason, l.DeliveredQuantity, l.Amount))],
            CanConfirm: current.IsSupplier && current.HasPermission(Permissions.CallOrderConfirm) && o.Status == CallOrderStatus.Sent,
            CanCancel: !current.IsSupplier && current.HasPermission(Permissions.CallOrderCreate) && o.Status == CallOrderStatus.Sent);
    }

    /// <summary>Tạo lần gọi hàng: SL gọi ≤ SL duyệt còn lại; một đơn cho mỗi NCC.</summary>
    public async Task<Result<List<Guid>>> CreateAsync(CreateCallsRequest r, CancellationToken ct)
    {
        // Σ SL gọi ≤ SL duyệt (and ≤ SL hợp đồng) must hold when several calls are sent at once: serialize on the summary,
        // the contract items involved and the document counter, then read the quantities again under the lock.
        var lineIds = r.Lines.Select(l => l.SummaryLineId).Distinct().ToList();
        var itemIds = await db.ProposalSummaries.AsNoTracking().Where(s => s.Id == r.SummaryId)
            .SelectMany(s => s.Lines).Where(l => lineIds.Contains(l.Id)).Select(l => l.ContractItemId).ToListAsync(ct);
        string[] keys = [$"proposal-summary:{r.SummaryId}", $"number:{DocumentNumbers.CallOrder}", .. itemIds.Select(i => $"contract-item:{i}")];
        return await db.SerializedAsync(keys, () => CreateLockedAsync(r, ct), ct);
    }

    private async Task<Result<List<Guid>>> CreateLockedAsync(CreateCallsRequest r, CancellationToken ct)
    {
        var summary = await db.ProposalSummaries.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == r.SummaryId, ct);
        if (summary is null)
        {
            return Error.NotFound();
        }

        if (r.DeliveryDeadline < Today)
        {
            return Error.Validation("CALL_DEADLINE_INVALID");
        }

        if (!await db.Warehouses.AnyAsync(w => w.Id == r.WarehouseId, ct))
        {
            return Error.Validation("WAREHOUSE_NOT_FOUND");
        }

        var requested = r.Lines.Where(l => l.Quantity > 0).GroupBy(l => l.SummaryLineId).Select(g => (LineId: g.Key, Quantity: g.Sum(x => x.Quantity))).ToList();
        foreach (var (lineId, quantity) in requested)
        {
            summary.RegisterCall(lineId, quantity);
        }

        // Only contracts in force today can be called (not liquidated, expired or not yet effective).
        var contractIds = requested.Select(x => summary.Lines.First(l => l.Id == x.LineId).ContractId).Distinct().ToList();
        var today = Today;
        var notInForce = await db.Contracts.AsNoTracking().Where(c => contractIds.Contains(c.Id))
            .Where(c => c.Status != ContractStatus.Effective || (c.EffectiveDate != null && c.EffectiveDate > today) || (c.ExpiryDate != null && c.ExpiryDate < today))
            .Select(c => c.Number).ToListAsync(ct);
        if (notInForce.Count > 0)
        {
            return Error.Validation("CALL_CONTRACT_NOT_IN_FORCE");
        }

        // Several summaries can cover the same contract item: the calls of all of them together stay within the contract.
        var callItems = requested.Select(x => (summary.Lines.First(l => l.Id == x.LineId).ContractItemId, x.Quantity))
            .GroupBy(x => x.ContractItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var itemIds = callItems.Keys.ToList();
        var calledSoFar = await db.CallOrders.AsNoTracking()
            .Where(o => o.Status != CallOrderStatus.Cancelled && o.Status != CallOrderStatus.Rejected)
            .SelectMany(o => o.Lines).Where(l => itemIds.Contains(l.ContractItemId))
            .GroupBy(l => l.ContractItemId).Select(g => new { g.Key, Qty = g.Sum(l => l.ConfirmedQuantity ?? l.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var contractQuantities = await db.Contracts.AsNoTracking().SelectMany(c => c.Items).Where(i => itemIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Quantity }).ToDictionaryAsync(x => x.Id, x => x.Quantity, ct);
        if (callItems.Any(x => calledSoFar.GetValueOrDefault(x.Key) + x.Value > contractQuantities.GetValueOrDefault(x.Key)))
        {
            return Error.Validation("CALL_QTY_EXCEEDS_CONTRACT");
        }

        var template = await db.DocumentTemplates.AsNoTracking().Where(t => t.Kind == TemplateKind.CallOrder && t.IsActive).OrderBy(t => t.Code).FirstOrDefaultAsync(ct);
        if (template is null)
        {
            return Error.Validation("CALL_TEMPLATE_MISSING");
        }

        var orders = new List<CallOrder>();
        foreach (var group in requested.Select(x => (Line: summary.Lines.First(l => l.Id == x.LineId), x.Quantity)).GroupBy(x => x.Line.SupplierId))
        {
            var order = new CallOrder(await numbers.NextAsync(DocumentNumbers.CallOrder, ct), summary.Id, group.Key, r.WarehouseId, Today, r.DeliveryDeadline,
                r.ContactName ?? current.FullName, r.Note);
            order.AddLines([.. group.Select(x => new CallOrderLineDraft(x.Line.Id, x.Line.ContractId, x.Line.ContractNumber, x.Line.ContractItemId,
                x.Line.ItemCode, x.Line.ItemName, x.Line.Unit, x.Line.UnitPrice, x.Quantity))]);
            db.CallOrders.Add(order);

            var rendered = await templates.RenderAsync(template.Id, await documentData.CallOrderAsync(order, summary.Number, ct), ct);
            if (!rendered.IsSuccess)
            {
                return rendered.Error!;
            }

            var document = await documents.CreateFromDocxAsync(DocumentType.CallOrder, $"Đơn gọi hàng {order.Number}", OwnerType, order.Id, order.SupplierId,
                order.Lines.First().ContractId, template.Id, rendered.Value, "Lập đơn gọi hàng", ct);
            order.AttachDocument(document.Id);
            await documents.PrepareForSigningAsync(document, finalize: false, ct); // PDF for the supplier (no signing workflow)
            orders.Add(order);
            audit.Log("CallOrderCreate", nameof(CallOrder), order.Id.ToString(), new { order.Number, summary = summary.Number, lines = order.Lines.Count });

            // Spec §6.3: email (deep link) + in-app notification to every active user of the supplier.
            var supplierUsers = await db.Users.Where(u => u.SupplierId == order.SupplierId && u.Status == UserStatus.Active).Select(u => u.Id).ToListAsync(ct);
            await notifications.NotifyAsync(supplierUsers, NotificationTypes.CallOrderSent, $"Đơn gọi hàng mới {order.Number}",
                $"Hạn giao {order.DeliveryDeadline:dd/MM/yyyy}. Vui lòng xác nhận số lượng.", $"/ncc/goi-hang/{order.Id}", email: true, ct);
        }

        await db.SaveChangesAsync(ct);
        await tracking.RecalculateAsync([.. orders.SelectMany(o => o.Lines).Select(l => l.ContractItemId)], ct);
        await db.SaveChangesAsync(ct);
        return orders.Select(o => o.Id).ToList();
    }

    /// <summary>NCC xác nhận SL (≤ SL gọi; thiếu phải nêu lý do); phần không giao được trả lại SL duyệt còn gọi.</summary>
    public async Task<Result> ConfirmAsync(Guid id, ConfirmCallRequest r, CancellationToken ct)
    {
        if (!current.IsSupplier)
        {
            return Result.Failure(Error.Forbidden());
        }

        return await WithSummaryLockAsync(id, () => ConfirmLockedAsync(id, r, ct), ct);
    }

    private async Task<Result> ConfirmLockedAsync(Guid id, ConfirmCallRequest r, CancellationToken ct)
    {
        var order = await db.CallOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return Result.Failure(Error.NotFound());
        }

        var released = order.Confirm([.. r.Lines.Select(l => new CallConfirmation(l.LineId, l.ConfirmedQuantity, l.ShortageReason))],
            current.UserId!.Value, current.FullName, r.Note, clock.UtcNow);
        await ReleaseAsync(order, released, ct);
        audit.Log("CallOrderConfirm", nameof(CallOrder), order.Id.ToString(), new { order.Status, released = released.Sum(x => x.Released) }, r.Note);
        var summaryAggregator = await db.ProposalSummaries.Where(s => s.Id == order.SummaryId).Select(s => s.AggregatorId).FirstAsync(ct);
        var recipients = new[] { summaryAggregator }.Concat(order.CreatedBy is { } creator ? [creator] : Array.Empty<Guid>());
        var statusText = order.Status switch
        {
            CallOrderStatus.SupplierConfirmed => "xác nhận đủ số lượng",
            CallOrderStatus.PartiallyConfirmed => "xác nhận một phần",
            _ => "từ chối",
        };
        await notifications.NotifyAsync(recipients, NotificationTypes.CallOrderConfirmed, $"NCC {statusText} đơn gọi hàng {order.Number}",
            r.Note, $"/goi-hang/{order.Id}", email: true, ct);
        await db.SaveChangesAsync(ct);
        await tracking.RecalculateAsync([.. order.Lines.Select(l => l.ContractItemId)], ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public Task<Result> CancelAsync(Guid id, CancellationToken ct) => WithSummaryLockAsync(id, () => CancelLockedAsync(id, ct), ct);

    /// <summary>Released quantities go back to the summary line: same lock as <see cref="CreateAsync"/> so no update is lost.</summary>
    private async Task<Result> WithSummaryLockAsync(Guid orderId, Func<Task<Result>> work, CancellationToken ct)
    {
        var summaryId = await db.CallOrders.AsNoTracking().Where(o => o.Id == orderId).Select(o => (Guid?)o.SummaryId).FirstOrDefaultAsync(ct);
        return summaryId is null ? Result.Failure(Error.NotFound()) : await db.SerializedAsync([$"proposal-summary:{summaryId}"], work, ct);
    }

    private async Task<Result> CancelLockedAsync(Guid id, CancellationToken ct)
    {
        var order = await db.CallOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return Result.Failure(Error.NotFound());
        }

        var released = order.Cancel();
        await ReleaseAsync(order, released, ct);
        if (order.DocumentId is { } docId)
        {
            (await db.Documents.FirstAsync(d => d.Id == docId, ct)).Cancel();
        }

        var supplierUsers = await db.Users.Where(u => u.SupplierId == order.SupplierId && u.Status == UserStatus.Active).Select(u => u.Id).ToListAsync(ct);
        await notifications.NotifyAsync(supplierUsers, NotificationTypes.CallOrderSent, $"Bệnh viện hủy đơn gọi hàng {order.Number}", null,
            $"/ncc/goi-hang/{order.Id}", email: true, ct);
        await db.SaveChangesAsync(ct);
        await tracking.RecalculateAsync([.. order.Lines.Select(l => l.ContractItemId)], ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task ReleaseAsync(CallOrder order, IReadOnlyList<(Guid SummaryLineId, decimal Released)> released, CancellationToken ct)
    {
        if (released.Count == 0)
        {
            return;
        }

        var summary = await db.ProposalSummaries.Include(s => s.Lines).FirstAsync(s => s.Id == order.SummaryId, ct);
        foreach (var (lineId, quantity) in released)
        {
            summary.ReleaseCall(lineId, quantity);
        }
    }
}
