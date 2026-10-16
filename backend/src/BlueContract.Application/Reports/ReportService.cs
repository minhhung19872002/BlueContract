using BlueContract.Application.Common;
using BlueContract.Application.Proposals;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Payments;
using BlueContract.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Reports;

public sealed record ContractExecutionFilter(Guid? SupplierId, Guid? CategoryId, ContractStatus? Status, ProcurementDomain? Domain);

public sealed record ContractExecutionRowDto(
    Guid ContractId,
    string ContractNumber,
    string ContractName,
    string SupplierName,
    string CategoryName,
    ContractStatus Status,
    DateOnly? ExpiryDate,
    decimal ContractValue,
    decimal CalledValue,
    decimal ReceivedValue,
    decimal AcceptedValue,
    decimal PaidValue,
    decimal RemainingValue,
    decimal ExecutionPercent);

public sealed record SupplierDebtRowDto(
    Guid SupplierId,
    string SupplierName,
    string TaxCode,
    int ContractCount,
    decimal ContractValue,
    decimal AcceptedValue,
    decimal ValuedValue,
    decimal PaidValue,
    decimal DebtValue);

public enum PaymentPeriod
{
    Month = 0,
    Quarter = 1,
    Year = 2,
}

public enum PaymentDimension
{
    None = 0,
    Domain = 1,
    Facility = 2,
    Supplier = 3,
}

public sealed record PaymentReportFilter(int? Year, PaymentPeriod Period = PaymentPeriod.Month, PaymentDimension Dimension = PaymentDimension.None);

public sealed record PaymentReportRowDto(string Period, string Group, int PaymentCount, decimal PaidAmount);

public sealed record SigningStepRowDto(string WorkflowCode, string StepCode, string StepName, int Completed, decimal AverageHours, decimal MaxHours, int Overdue);

public sealed record SigningBacklogRowDto(string Assignee, string AssigneeType, int Pending, int Overdue, DateTimeOffset? OldestSince);

public sealed record SigningReportDto(IReadOnlyList<SigningStepRowDto> Steps, IReadOnlyList<SigningBacklogRowDto> Backlog);

/// <summary>Báo cáo (spec §9.1-13): tình hình thực hiện HĐ, công nợ theo NCC, giá trị thanh toán theo kỳ / mảng / cơ sở, tiến độ ký.</summary>
public sealed class ReportService(IAppDbContext db, IClock clock)
{
    public async Task<List<ContractExecutionRowDto>> ContractExecutionAsync(ContractExecutionFilter f, CancellationToken ct)
    {
        var query = db.Contracts.AsNoTracking().Where(c => c.Status != ContractStatus.Draft && c.Status != ContractStatus.Cancelled);
        query = f.SupplierId is { } supplier ? query.Where(c => c.SupplierId == supplier) : query;
        query = f.CategoryId is { } category ? query.Where(c => c.CategoryId == category) : query;
        query = f.Status is { } status ? query.Where(c => c.Status == status) : query;
        query = f.Domain is { } domain ? query.Where(c => db.ItemCategories.Any(x => x.Id == c.CategoryId && x.Domain == domain)) : query;

        var rows = await query.OrderBy(c => c.Number)
            .Select(c => new
            {
                c.Id,
                c.Number,
                c.Name,
                c.Status,
                c.ExpiryDate,
                c.TotalAmount,
                Supplier = db.Suppliers.Where(s => s.Id == c.SupplierId).Select(s => s.Name).FirstOrDefault(),
                Category = db.ItemCategories.Where(x => x.Id == c.CategoryId).Select(x => x.Name).FirstOrDefault(),
                Called = c.Items.Sum(i => i.CalledQuantity * i.UnitPrice),
                Received = c.Items.Sum(i => i.ReceivedQuantity * i.UnitPrice),
                Accepted = c.Items.Sum(i => i.AcceptedQuantity * i.UnitPrice),
                Paid = c.Items.Sum(i => i.PaidQuantity * i.UnitPrice),
            })
            .Take(PageQuery.MaxExportRows).ToListAsync(ct);
        return [.. rows.Select(r => new ContractExecutionRowDto(r.Id, r.Number, r.Name, r.Supplier ?? string.Empty, r.Category ?? string.Empty, r.Status, r.ExpiryDate,
            r.TotalAmount, Round(r.Called), Round(r.Received), Round(r.Accepted), Round(r.Paid), Round(Math.Max(0, r.TotalAmount - r.Received)),
            r.TotalAmount == 0 ? 0 : Math.Round(r.Received * 100 / r.TotalAmount, 1)))];
    }

    /// <summary>Công nợ theo NCC: còn phải trả = đã nghiệm thu − đã thanh toán (UNC + chốt công nợ) − thu hồi tạm ứng.</summary>
    public async Task<List<SupplierDebtRowDto>> SupplierDebtAsync(CancellationToken ct)
    {
        var contracts = await db.Contracts.AsNoTracking().Where(c => c.Status != ContractStatus.Draft && c.Status != ContractStatus.Cancelled)
            .GroupBy(c => c.SupplierId).Select(g => new { g.Key, Count = g.Count(), Value = g.Sum(c => c.TotalAmount) }).ToListAsync(ct);
        var accepted = await db.AcceptanceRecords.AsNoTracking().Where(a => a.Status == PaymentRecordStatus.Completed)
            .GroupBy(a => a.SupplierId).Select(g => new { g.Key, Value = g.Sum(a => a.TotalAmount) }).ToDictionaryAsync(x => x.Key, x => x.Value, ct);
        var valued = await db.ValuationRecords.AsNoTracking().Where(v => v.Status == PaymentRecordStatus.Completed)
            .GroupBy(v => v.SupplierId)
            .Select(g => new { g.Key, Value = g.Sum(v => v.CurrentPeriodValue), Advance = g.Sum(v => v.AdvanceRecovered), Paid = g.Sum(v => v.PaymentOrders.Sum(p => p.Amount)) })
            .ToDictionaryAsync(x => x.Key, ct);
        var settled = await db.LiquidationRecords.AsNoTracking().Where(l => l.Status == PaymentRecordStatus.Completed)
            .GroupBy(l => l.SupplierId).Select(g => new { g.Key, Value = g.Sum(l => l.Settlements.Sum(s => s.Amount)) }).ToDictionaryAsync(x => x.Key, x => x.Value, ct);
        var ids = contracts.Select(c => c.Key).ToList();
        var suppliers = await db.Suppliers.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => new { s.Name, s.TaxCode }, ct);

        return [.. contracts.Select(c =>
        {
            var v = valued.GetValueOrDefault(c.Key);
            var paid = (v?.Paid ?? 0) + settled.GetValueOrDefault(c.Key);
            var acc = accepted.GetValueOrDefault(c.Key);
            return new SupplierDebtRowDto(c.Key, suppliers.GetValueOrDefault(c.Key)?.Name ?? string.Empty, suppliers.GetValueOrDefault(c.Key)?.TaxCode ?? string.Empty,
                c.Count, c.Value, acc, v?.Value ?? 0, paid, Math.Max(0, acc - paid - (v?.Advance ?? 0)));
        }).OrderByDescending(r => r.DebtValue).ThenBy(r => r.SupplierName)];
    }

    /// <summary>Giá trị đã thanh toán (ủy nhiệm chi) theo tháng / quý / năm, tùy chọn theo mảng, cơ sở hoặc NCC.</summary>
    public async Task<List<PaymentReportRowDto>> PaymentsAsync(PaymentReportFilter f, CancellationToken ct)
    {
        var year = f.Year ?? clock.UtcNow.ToOffset(TimeSpan.FromHours(7)).Year;
        var start = new DateOnly(f.Period == PaymentPeriod.Year ? year - 4 : year, 1, 1);
        var end = new DateOnly(year, 12, 31);
        var orders = await (from v in db.ValuationRecords.AsNoTracking()
                            where v.Status != PaymentRecordStatus.Cancelled
                            from p in v.PaymentOrders
                            where p.Date >= start && p.Date <= end
                            join c in db.Contracts.AsNoTracking() on v.ContractId equals c.Id
                            select new
                            {
                                p.Date,
                                p.Amount,
                                Domain = db.ItemCategories.Where(x => x.Id == c.CategoryId).Select(x => (ProcurementDomain?)x.Domain).FirstOrDefault(),
                                Facility = db.Facilities.Where(x => x.Id == c.FacilityId).Select(x => x.Name).FirstOrDefault(),
                                Supplier = db.Suppliers.Where(s => s.Id == c.SupplierId).Select(s => s.Name).FirstOrDefault(),
                            }).ToListAsync(ct);

        string Period(DateOnly d) => f.Period switch
        {
            PaymentPeriod.Year => d.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PaymentPeriod.Quarter => $"Quý {((d.Month - 1) / 3) + 1}/{d.Year}",
            _ => d.ToString("MM/yyyy", System.Globalization.CultureInfo.InvariantCulture),
        };

        string Group(ProcurementDomain? domain, string? facility, string? supplier) => f.Dimension switch
        {
            PaymentDimension.Domain => domain is { } d ? ProposalScope.DomainText(d) : "Khác",
            PaymentDimension.Facility => facility ?? "Toàn bệnh viện",
            PaymentDimension.Supplier => supplier ?? string.Empty,
            _ => "Tất cả",
        };

        return [.. orders.GroupBy(o => (Period: Period(o.Date), Sort: f.Period == PaymentPeriod.Year ? new DateOnly(o.Date.Year, 1, 1)
                    : f.Period == PaymentPeriod.Quarter ? new DateOnly(o.Date.Year, (((o.Date.Month - 1) / 3) * 3) + 1, 1) : new DateOnly(o.Date.Year, o.Date.Month, 1),
                Group: Group(o.Domain, o.Facility, o.Supplier)))
            .OrderBy(g => g.Key.Sort).ThenBy(g => g.Key.Group)
            .Select(g => new PaymentReportRowDto(g.Key.Period, g.Key.Group, g.Count(), g.Sum(o => o.Amount)))];
    }

    /// <summary>Tiến độ ký: thời gian xử lý trung bình mỗi bước; tài liệu tồn đọng theo người/vai trò được giao.</summary>
    public async Task<SigningReportDto> SigningAsync(DateTimeOffset? from, CancellationToken ct)
    {
        var since = from ?? clock.UtcNow.AddMonths(-6);
        var now = clock.UtcNow;
        var done = await (from s in db.WorkflowSteps.AsNoTracking()
                          join w in db.WorkflowInstances.AsNoTracking() on s.InstanceId equals w.Id
                          where s.Status == StepStatus.Done && s.ActivatedAt != null && s.ActedAt != null && s.ActedAt >= since
                          select new { w.TemplateCode, s.Code, s.Name, s.ActivatedAt, s.ActedAt, s.DueAt })
            .ToListAsync(ct);
        var steps = done.GroupBy(s => (s.TemplateCode, s.Code))
            .Select(g =>
            {
                var hours = g.Select(s => (decimal)(s.ActedAt!.Value - s.ActivatedAt!.Value).TotalHours).ToList();
                return new SigningStepRowDto(g.Key.TemplateCode, g.Key.Code, g.First().Name, g.Count(), Math.Round(hours.Average(), 1), Math.Round(hours.Max(), 1),
                    g.Count(s => s.DueAt != null && s.ActedAt > s.DueAt));
            })
            .OrderBy(r => r.WorkflowCode).ThenBy(r => r.StepCode).ToList();

        var active = await (from s in db.WorkflowSteps.AsNoTracking()
                            join w in db.WorkflowInstances.AsNoTracking() on s.InstanceId equals w.Id
                            where s.Status == StepStatus.Active && w.Status == WorkflowStatus.Running
                            select new { s.AssigneeType, s.RoleCode, s.AssigneeUserId, w.SupplierId, w.StartedByName, s.ActivatedAt, s.DueAt })
            .ToListAsync(ct);
        var roles = await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Code, r => r.Name, ct);
        var userIds = active.Where(a => a.AssigneeUserId != null).Select(a => a.AssigneeUserId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var supplierIds = active.Where(a => a.SupplierId != null).Select(a => a.SupplierId!.Value).Distinct().ToList();
        var suppliers = await db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var backlog = active.GroupBy(a => a.AssigneeType switch
            {
                AssigneeType.Role => ("Vai trò", roles.GetValueOrDefault(a.RoleCode ?? string.Empty, a.RoleCode ?? string.Empty)),
                AssigneeType.User => ("Người dùng", users.GetValueOrDefault(a.AssigneeUserId ?? Guid.Empty, "Người dùng")),
                AssigneeType.DocumentSupplier => ("Nhà cung cấp", a.SupplierId is { } sid ? suppliers.GetValueOrDefault(sid, "Nhà cung cấp") : "Nhà cung cấp"),
                _ => ("Người lập", a.StartedByName ?? "Người lập"),
            })
            .Select(g => new SigningBacklogRowDto(g.Key.Item2, g.Key.Item1, g.Count(), g.Count(a => a.DueAt != null && a.DueAt < now), g.Min(a => a.ActivatedAt)))
            .OrderByDescending(r => r.Pending).ToList();
        return new SigningReportDto(steps, backlog);
    }

    private static decimal Round(decimal v) => decimal.Round(v, 0, MidpointRounding.AwayFromZero);
}
