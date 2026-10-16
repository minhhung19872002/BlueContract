using BlueContract.Domain.Contracts;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Receipts;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Common;

/// <summary>Facilities and domains a user is limited to; both empty = no limit.</summary>
public sealed record ScopeSet(IReadOnlyList<Guid> Facilities, IReadOnlyList<ProcurementDomain> Domains)
{
    public static ScopeSet None { get; } = new([], []);

    public bool Unrestricted => Facilities.Count == 0 && Domains.Count == 0;
}

/// <summary>
/// Phạm vi dữ liệu (spec §4.2, AUDIT-1 §10.7) in one place. A user scoped to facilities (directly, or through the facility of
/// a department scope) sees contracts of those facilities plus contracts valid for every facility; a user scoped to domains
/// sees contracts whose category belongs to one of them. Everything tied to a contract (receipts, payments, call orders,
/// archive, reports, dashboards, contract documents) is filtered through <see cref="ContractIdsAsync"/>, so counts and
/// rows always come from the same filtered query. Supplier users are isolated by supplier instead.
/// </summary>
public sealed class DataScope(IAppDbContext db, ICurrentUser current)
{
    private ScopeSet? _mine;

    public async Task<ScopeSet> MineAsync(CancellationToken ct) =>
        _mine ??= current.IsSupplier ? ScopeSet.None : await ResolveAsync(current.DataScopes, ct);

    /// <summary>Scope of another user — a signing delegate acts within the delegator's scope.</summary>
    public async Task<ScopeSet> OfUserAsync(Guid userId, CancellationToken ct)
    {
        var scopes = await db.Users.IgnoreQueryFilters().Where(u => u.Id == userId)
            .SelectMany(u => u.DataScopes.Select(s => new { s.ScopeType, s.Value })).ToListAsync(ct);
        return await ResolveAsync([.. scopes.Select(s => (s.ScopeType, s.Value))], ct);
    }

    public IQueryable<Contract> Apply(IQueryable<Contract> contracts, ScopeSet scope)
    {
        if (scope.Facilities.Count > 0)
        {
            var facilities = scope.Facilities;
            contracts = contracts.Where(c => c.FacilityIds.Count == 0 || c.FacilityIds.Any(f => facilities.Contains(f)));
        }

        if (scope.Domains.Count > 0)
        {
            var domains = scope.Domains;
            contracts = contracts.Where(c => db.ItemCategories.Any(cat => cat.Id == c.CategoryId && domains.Contains(cat.Domain)));
        }

        return contracts;
    }

    /// <summary>Ids of the contracts the current user may see, as a sub-query; null when the user is not limited.</summary>
    public async Task<IQueryable<Guid>?> ContractIdsAsync(CancellationToken ct)
    {
        var scope = await MineAsync(ct);
        return scope.Unrestricted ? null : Apply(db.Contracts, scope).Select(c => c.Id);
    }

    public async Task<IQueryable<Contract>> ContractsAsync(IQueryable<Contract> contracts, CancellationToken ct) =>
        Apply(contracts, await MineAsync(ct));

    /// <summary>
    /// Receipts of visible contracts; a receipt not matched to a contract yet is shown only to users without a domain limit,
    /// and to facility-limited users when it arrived at a warehouse of their facility.
    /// </summary>
    public async Task<IQueryable<WarehouseReceipt>> ReceiptsAsync(IQueryable<WarehouseReceipt> receipts, CancellationToken ct)
    {
        var scope = await MineAsync(ct);
        if (scope.Unrestricted)
        {
            return receipts;
        }

        var ids = Apply(db.Contracts, scope).Select(c => c.Id);
        var facilities = scope.Facilities;
        var noDomainLimit = scope.Domains.Count == 0;
        return receipts.Where(r => r.ContractId != null
            ? ids.Contains(r.ContractId.Value)
            : noDomainLimit && (facilities.Count == 0 || db.Warehouses.Any(w => w.Id == r.WarehouseId && facilities.Contains(w.FacilityId))));
    }

    public async Task<bool> CanSeeContractAsync(Guid? contractId, CancellationToken ct)
    {
        var ids = await ContractIdsAsync(ct);
        return ids is null || (contractId is { } id && await ids.AnyAsync(x => x == id, ct));
    }

    /// <summary>True when the scope covers a workflow's facility / domain (null = not tied to one).</summary>
    public static bool Covers(ScopeSet scope, Guid? facilityId, string? domain) =>
        (facilityId is null || scope.Facilities.Count == 0 || scope.Facilities.Contains(facilityId.Value))
        && (domain is null || scope.Domains.Count == 0 || (Enum.TryParse<ProcurementDomain>(domain, out var d) && scope.Domains.Contains(d)));

    private async Task<ScopeSet> ResolveAsync(IReadOnlyList<(DataScopeType Type, string Value)> scopes, CancellationToken ct)
    {
        var facilities = scopes.Where(s => s.Type == DataScopeType.Facility)
            .Select(s => Guid.TryParse(s.Value, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();
        var departments = scopes.Where(s => s.Type == DataScopeType.Department)
            .Select(s => Guid.TryParse(s.Value, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();
        if (departments.Count > 0)
        {
            facilities.AddRange(await db.Departments.IgnoreQueryFilters().Where(d => departments.Contains(d.Id)).Select(d => d.FacilityId).ToListAsync(ct));
        }

        var domains = scopes.Where(s => s.Type == DataScopeType.Domain)
            .Select(s => Enum.TryParse<ProcurementDomain>(s.Value, out var d) ? (ProcurementDomain?)d : null)
            .Where(d => d is not null).Select(d => d!.Value).ToList();
        return new ScopeSet([.. facilities.Distinct()], [.. domains.Distinct()]);
    }
}
