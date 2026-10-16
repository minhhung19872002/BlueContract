using BlueProcure.Application.Common;
using BlueProcure.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace BlueProcure.Application.Admin;

public sealed record HistoryEntryDto(int Version, HistoryChangeType ChangeType, DateTimeOffset ChangedAt, string? ChangedByName, string Snapshot);

/// <summary>Reads the "lịch sử cập nhật" of master data (departments, users, suppliers).</summary>
public sealed class HistoryService(IAppDbContext db)
{
    public Task<List<HistoryEntryDto>> DepartmentAsync(Guid id, CancellationToken ct) => Read(db.DepartmentHistories, id, ct);

    public Task<List<HistoryEntryDto>> UserAsync(Guid id, CancellationToken ct) => Read(db.UserHistories, id, ct);

    public Task<List<HistoryEntryDto>> SupplierAsync(Guid id, CancellationToken ct) => Read(db.SupplierHistories, id, ct);

    private static Task<List<HistoryEntryDto>> Read<T>(IQueryable<T> set, Guid id, CancellationToken ct)
        where T : HistoryRecord =>
        set.AsNoTracking()
            .Where(h => h.EntityId == id)
            .OrderByDescending(h => h.Version)
            .Select(h => new HistoryEntryDto(h.Version, h.ChangeType, h.ChangedAt, h.ChangedByName, h.Snapshot))
            .ToListAsync(ct);
}
