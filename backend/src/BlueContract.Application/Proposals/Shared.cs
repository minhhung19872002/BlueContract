using BlueContract.Application.Common;
using BlueContract.Application.Notifications;
using BlueContract.Domain.Common;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Proposals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueContract.Application.Proposals;

/// <summary>Document numbers "DX-2026-0001" (per prefix and Vietnam calendar year); saved with the caller's unit of work.</summary>
public sealed class DocumentNumbers(IAppDbContext db, IClock clock)
{
    public const string Proposal = "DX";
    public const string Summary = "TH";
    public const string CallOrder = "GH";

    public async Task<string> NextAsync(string prefix, CancellationToken ct)
    {
        var year = clock.UtcNow.ToOffset(TimeSpan.FromHours(7)).Year;
        var sequence = db.NumberSequences.Local.FirstOrDefault(s => s.Prefix == prefix && s.Year == year)
            ?? await db.NumberSequences.FirstOrDefaultAsync(s => s.Prefix == prefix && s.Year == year, ct);
        if (sequence is null)
        {
            sequence = new NumberSequence(prefix, year);
            db.NumberSequences.Add(sequence);
        }

        return sequence.Next();
    }
}

/// <summary>
/// Keeps the execution columns of contract items in sync (spec §5.3 "các cột tính toán"): đã đề xuất, đã duyệt, đã gọi.
/// Received / accepted / paid are owned by the receipt and payment modules and preserved here.
/// </summary>
public sealed class ContractTracking(IAppDbContext db, PostCommitWork postCommit)
{
    /// <summary>
    /// Recalculates after the current unit of work commits (workflow observers run before SaveChanges, when the new
    /// statuses are not in the database yet).
    /// </summary>
    public void Defer(IEnumerable<Guid> contractItemIds)
    {
        var ids = contractItemIds.Distinct().ToList();
        postCommit.Enqueue(async (services, ct) =>
        {
            await services.GetRequiredService<ContractTracking>().RecalculateAsync(ids, ct);
            await services.GetRequiredService<IAppDbContext>().SaveChangesAsync(ct);
        });
    }

    public async Task RecalculateAsync(IReadOnlyCollection<Guid> contractItemIds, CancellationToken ct)
    {
        if (contractItemIds.Count == 0)
        {
            return;
        }

        var ids = contractItemIds.Distinct().ToList();
        var proposed = await db.Proposals.AsNoTracking()
            .Where(p => p.Status == ProposalStatus.Submitted || p.Status == ProposalStatus.Aggregated)
            .SelectMany(p => p.Lines).Where(l => ids.Contains(l.ContractItemId))
            .GroupBy(l => l.ContractItemId).Select(g => new { g.Key, Qty = g.Sum(l => l.ProposedQuantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var approved = await db.ProposalSummaries.AsNoTracking()
            .Where(s => s.Status == ProposalSummaryStatus.Approved)
            .SelectMany(s => s.Lines).Where(l => ids.Contains(l.ContractItemId))
            .GroupBy(l => l.ContractItemId).Select(g => new { g.Key, Qty = g.Sum(l => l.ApprovedQuantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var called = await db.CallOrders.AsNoTracking()
            .Where(o => o.Status != CallOrderStatus.Cancelled && o.Status != CallOrderStatus.Rejected)
            .SelectMany(o => o.Lines).Where(l => ids.Contains(l.ContractItemId))
            .GroupBy(l => l.ContractItemId).Select(g => new { g.Key, Qty = g.Sum(l => l.ConfirmedQuantity ?? l.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

        var contracts = await db.Contracts.Include(c => c.Items).Where(c => c.Items.Any(i => ids.Contains(i.Id))).ToListAsync(ct);
        foreach (var item in contracts.SelectMany(c => c.Items).Where(i => ids.Contains(i.Id)))
        {
            item.Track(proposed.GetValueOrDefault(item.Id), approved.GetValueOrDefault(item.Id), called.GetValueOrDefault(item.Id),
                item.ReceivedQuantity, item.AcceptedQuantity, item.PaidQuantity);
        }
    }
}

/// <summary>Department / domain restrictions of the caller for proposals (spec §4.2 data scope).</summary>
public sealed class ProposalScope(IAppDbContext db, ICurrentUser current)
{
    private IReadOnlyList<Guid>? _departments;

    /// <summary>Departments the caller may propose for: Department scopes, otherwise the user's own department.</summary>
    public async Task<IReadOnlyList<Guid>> DepartmentsAsync(CancellationToken ct)
    {
        if (_departments is not null)
        {
            return _departments;
        }

        var scoped = current.DataScopes.Where(s => s.Type == DataScopeType.Department).Select(s => Guid.TryParse(s.Value, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty).ToList();
        if (scoped.Count == 0 && current.UserId is { } me)
        {
            var own = await db.Users.Where(u => u.Id == me).Select(u => u.DepartmentId).FirstOrDefaultAsync(ct);
            if (own is { } d)
            {
                scoped.Add(d);
            }
        }

        return _departments = scoped;
    }

    /// <summary>Domains the caller handles (empty = all).</summary>
    public IReadOnlyList<ProcurementDomain> Domains() =>
        [.. current.DataScopes.Where(s => s.Type == DataScopeType.Domain)
            .Select(s => Enum.TryParse<ProcurementDomain>(s.Value, out var d) ? (ProcurementDomain?)d : null)
            .Where(d => d is not null).Select(d => d!.Value)];

    public bool CanSeeAllDepartments =>
        current.HasPermission(Permissions.ProposalAggregate) || current.HasPermission(Permissions.ProposalApprove) || current.HasPermission(Permissions.CallOrderCreate);

    public static string DomainText(ProcurementDomain domain) => domain switch
    {
        ProcurementDomain.Drug => "Thuốc",
        ProcurementDomain.MedicalSupply => "Vật tư y tế",
        ProcurementDomain.Chemical => "Hóa chất, sinh phẩm",
        ProcurementDomain.Equipment => "Tài sản, trang thiết bị",
        _ => "Hành chính, dịch vụ",
    };

    /// <summary>Callable contracts of a domain: effective, call-off contracts whose category belongs to the domain.</summary>
    public IQueryable<Contract> CallableContracts(ProcurementDomain domain) =>
        db.Contracts.Where(c => c.Status == ContractStatus.Effective && c.Kind == ContractKind.UnitPriceCallOff
            && db.ItemCategories.Any(cat => cat.Id == c.CategoryId && cat.Domain == domain));
}
