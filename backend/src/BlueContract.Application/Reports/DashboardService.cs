using BlueContract.Application.Admin;
using BlueContract.Application.Common;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Payments;
using BlueContract.Domain.Proposals;
using BlueContract.Domain.Receipts;
using BlueContract.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Reports;

public sealed record PendingTypeDto(DocumentType Type, int Count, int Overdue);

public sealed record ExpiringContractDto(Guid Id, string Number, string SupplierName, DateOnly ExpiryDate, int DaysLeft);

public sealed record LowQuantityDto(Guid ContractId, string ContractNumber, string SupplierName, string ItemName, string Unit, decimal Quantity, decimal Remaining, decimal Percent);

public sealed record MonthAmountDto(string Month, decimal Paid, decimal Unpaid);

public sealed record InternalDashboardDto(
    int PendingTotal,
    int PendingOverdue,
    IReadOnlyList<PendingTypeDto> PendingByType,
    int ExpiryWarningDays,
    IReadOnlyList<ExpiringContractDto> ExpiringContracts,
    decimal LowQuantityPercent,
    IReadOnlyList<LowQuantityDto> LowQuantityItems,
    int WarningReceipts,
    int InvalidReceipts,
    int OverdueCallOrders,
    int PendingCallOrders,
    decimal PaidTotal,
    decimal UnpaidTotal,
    IReadOnlyList<MonthAmountDto> Payments);

public sealed record SupplierDashboardDto(
    int PendingSigning,
    int CallOrdersAwaiting,
    int CallOrdersOverdue,
    int EffectiveContracts,
    int ReceiptsWithoutInvoice,
    decimal AcceptedNotValued,
    decimal ValuedUnpaid,
    decimal PaidTotal,
    IReadOnlyList<MonthAmountDto> Payments);

/// <summary>Tổng quan (spec §9.1-1, §9.2-1): việc chờ xử lý, cảnh báo hợp đồng / phiếu / gọi hàng, thanh toán theo tháng.</summary>
public sealed class DashboardService(IAppDbContext db, WorkflowService workflows, SettingService settings, ICurrentUser current, IClock clock, DataScope dataScope)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);

    public async Task<InternalDashboardDto> InternalAsync(CancellationToken ct)
    {
        var pending = await workflows.PendingByTypeAsync(ct);
        var today = Today;
        var expiryDays = await settings.GetIntAsync(SystemSettingKeys.ContractExpiryWarningDays, 30, ct);
        var lowPercent = await settings.GetDecimalAsync(SystemSettingKeys.ContractLowQuantityPercent, 10, ct);
        var horizon = today.AddDays(expiryDays);

        // Every figure below is limited to the user's data scope (spec §4.2, AUDIT-1 §10.7).
        var contracts = await dataScope.ContractsAsync(db.Contracts.AsNoTracking(), ct);
        var scoped = await dataScope.ContractIdsAsync(ct);
        var callOrders = db.CallOrders.AsNoTracking();
        var valuations = db.ValuationRecords.AsNoTracking();
        var liquidations = db.LiquidationRecords.AsNoTracking();
        if (scoped is not null)
        {
            callOrders = callOrders.Where(o => o.Lines.Any(l => scoped.Contains(l.ContractId)));
            valuations = valuations.Where(v => scoped.Contains(v.ContractId));
            liquidations = liquidations.Where(l => scoped.Contains(l.ContractId));
        }

        var expiring = await contracts
            .Where(c => c.Status == ContractStatus.Effective && c.ExpiryDate != null && c.ExpiryDate <= horizon)
            .OrderBy(c => c.ExpiryDate).Take(20)
            .Select(c => new { c.Id, c.Number, c.ExpiryDate, Supplier = db.Suppliers.Where(s => s.Id == c.SupplierId).Select(s => s.Name).FirstOrDefault() })
            .ToListAsync(ct);

        var low = await contracts.Where(c => c.Status == ContractStatus.Effective)
            .SelectMany(c => c.Items.Select(i => new { Contract = c, Item = i }))
            .Where(x => x.Item.Quantity > 0 && (x.Item.Quantity - x.Item.ReceivedQuantity) * 100 <= x.Item.Quantity * lowPercent)
            .OrderBy(x => (x.Item.Quantity - x.Item.ReceivedQuantity) / x.Item.Quantity).Take(20)
            .Select(x => new LowQuantityDto(x.Contract.Id, x.Contract.Number,
                db.Suppliers.Where(s => s.Id == x.Contract.SupplierId).Select(s => s.Name).FirstOrDefault() ?? string.Empty,
                x.Item.ItemName, x.Item.Unit, x.Item.Quantity, x.Item.Quantity - x.Item.ReceivedQuantity,
                Math.Round((x.Item.Quantity - x.Item.ReceivedQuantity) * 100 / x.Item.Quantity, 1)))
            .ToListAsync(ct);

        var openReceipts = (await dataScope.ReceiptsAsync(db.WarehouseReceipts.AsNoTracking(), ct))
            .Where(r => r.Status != ReceiptStatus.Completed && r.Status != ReceiptStatus.Cancelled);
        var (paid, unpaid, months) = await PaymentsAsync(valuations, liquidations, ct);
        return new InternalDashboardDto(
            pending.Sum(p => p.Count), pending.Sum(p => p.Overdue),
            [.. pending.OrderByDescending(p => p.Count).Select(p => new PendingTypeDto(p.Type, p.Count, p.Overdue))],
            expiryDays,
            [.. expiring.Select(c => new ExpiringContractDto(c.Id, c.Number, c.Supplier ?? string.Empty, c.ExpiryDate!.Value, c.ExpiryDate!.Value.DayNumber - today.DayNumber))],
            lowPercent, low,
            await openReceipts.CountAsync(r => r.ValidationStatus == ReceiptValidationStatus.Warning, ct),
            await openReceipts.CountAsync(r => r.ValidationStatus == ReceiptValidationStatus.Invalid, ct),
            await callOrders.CountAsync(o => o.Status == CallOrderStatus.Sent && o.DeliveryDeadline < today, ct),
            await callOrders.CountAsync(o => o.Status == CallOrderStatus.Sent, ct),
            paid, unpaid, months);
    }

    /// <summary>Cổng NCC: queries run under the supplier isolation filter, so every figure is the caller's company only.</summary>
    public async Task<SupplierDashboardDto> SupplierAsync(CancellationToken ct)
    {
        var today = Today;
        var pending = await workflows.InboxCountAsync(ct);
        var acceptedNotValued = await db.AcceptanceRecords.AsNoTracking()
            .Where(a => a.Status == PaymentRecordStatus.Completed && a.ValuationId == null).SumAsync(a => (decimal?)a.TotalAmount, ct) ?? 0;
        var (paid, unpaid, months) = await PaymentsAsync(db.ValuationRecords.AsNoTracking(), db.LiquidationRecords.AsNoTracking(), ct);
        return new SupplierDashboardDto(
            pending.Pending,
            await db.CallOrders.CountAsync(o => o.Status == CallOrderStatus.Sent, ct),
            await db.CallOrders.CountAsync(o => o.Status == CallOrderStatus.Sent && o.DeliveryDeadline < today, ct),
            await db.Contracts.CountAsync(c => c.Status == ContractStatus.Effective, ct),
            await db.WarehouseReceipts.CountAsync(r => r.InvoiceNumber == null && (r.Status == ReceiptStatus.Signing || r.Status == ReceiptStatus.Returned), ct),
            acceptedNotValued, unpaid, paid, months);
    }

    /// <summary>
    /// Đã thanh toán = UNC (theo ngày UNC) + khoản chốt công nợ khi thanh lý; chưa thanh toán = đề nghị thanh toán của các
    /// 08a đã ký còn thiếu (theo tháng ký). Twelve months ending this month.
    /// </summary>
    private async Task<(decimal Paid, decimal Unpaid, List<MonthAmountDto> Months)> PaymentsAsync(
        IQueryable<ValuationRecord> valuations, IQueryable<LiquidationRecord> liquidations, CancellationToken ct)
    {
        var first = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-11);
        var orders = await valuations.Where(v => v.Status != PaymentRecordStatus.Cancelled).SelectMany(v => v.PaymentOrders)
            .Select(p => new { p.Date, p.Amount }).ToListAsync(ct);
        var settlements = await liquidations.Where(l => l.Status == PaymentRecordStatus.Completed).SelectMany(l => l.Settlements)
            .Select(s => new { s.MarkedAt, s.Amount }).ToListAsync(ct);
        var completed = await valuations.Where(v => v.Status == PaymentRecordStatus.Completed && v.PaymentStatus == PaymentStatus.Unpaid)
            .Select(v => new { v.CompletedAt, Open = v.CurrentPeriodValue - v.AdvanceRecovered - v.PaymentOrders.Sum(p => p.Amount) })
            .ToListAsync(ct);

        static string Key(DateOnly d) => d.ToString("MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
        DateOnly Local(DateTimeOffset at) => DateOnly.FromDateTime(at.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var paidByMonth = orders.Select(o => (Month: Key(o.Date), o.Amount)).Concat(settlements.Select(s => (Month: Key(Local(s.MarkedAt)), s.Amount)))
            .GroupBy(x => x.Month).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var unpaidByMonth = completed.Where(v => v.CompletedAt is not null && v.Open > 0)
            .GroupBy(v => Key(Local(v.CompletedAt!.Value))).ToDictionary(g => g.Key, g => g.Sum(v => v.Open));
        var months = Enumerable.Range(0, 12).Select(i => Key(first.AddMonths(i)))
            .Select(m => new MonthAmountDto(m, paidByMonth.GetValueOrDefault(m), unpaidByMonth.GetValueOrDefault(m))).ToList();
        return (orders.Sum(o => o.Amount) + settlements.Sum(s => s.Amount), completed.Where(v => v.Open > 0).Sum(v => v.Open), months);
    }

    public bool IsSupplier => current.IsSupplier;
}
