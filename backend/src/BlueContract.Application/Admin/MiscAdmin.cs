using System.Globalization;
using BlueContract.Application.Common;
using BlueContract.Domain.Common;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Settings;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Admin;

// ---------- Signing delegation ----------

public sealed record DelegationDto(
    Guid Id,
    Guid DelegatorUserId,
    string DelegatorName,
    Guid DelegateUserId,
    string DelegateName,
    string DelegatorTitle,
    string DelegateTitle,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    string? Reason,
    DateTimeOffset? RevokedAt,
    bool IsEffective);

public sealed record CreateDelegationRequest(
    Guid DelegatorUserId,
    Guid DelegateUserId,
    string DelegatorTitle,
    string DelegateTitle,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    string? Reason);

internal sealed class CreateDelegationRequestValidator : AbstractValidator<CreateDelegationRequest>
{
    public CreateDelegationRequestValidator()
    {
        RuleFor(x => x.DelegatorUserId).NotEmpty().WithName("Người ủy quyền");
        RuleFor(x => x.DelegateUserId).NotEmpty().WithName("Người được ủy quyền")
            .NotEqual(x => x.DelegatorUserId).WithMessage("Người được ủy quyền phải khác người ủy quyền.");
        RuleFor(x => x.DelegatorTitle).NotEmpty().WithName("Chức danh người ủy quyền");
        RuleFor(x => x.DelegateTitle).NotEmpty().WithName("Chức danh người được ủy quyền");
        RuleFor(x => x.ValidTo).GreaterThan(x => x.ValidFrom).WithMessage("Thời gian kết thúc phải sau thời gian bắt đầu.");
    }
}

public sealed class DelegationService(IAppDbContext db, ICurrentUser current, IClock clock)
{
    public async Task<PagedResult<DelegationDto>> ListAsync(PageQuery q, bool onlyEffective, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var query = db.SigningDelegations.AsNoTracking();
        if (onlyEffective)
        {
            query = query.Where(d => d.RevokedAt == null && d.ValidFrom <= now && d.ValidTo > now);
        }

        // Non-administrators only see delegations they are part of.
        if (!current.HasPermission(Permissions.UserManage))
        {
            var me = current.UserId;
            query = query.Where(d => d.DelegatorUserId == me || d.DelegateUserId == me);
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            query = query.Where(d => db.Users.Any(u => (u.Id == d.DelegatorUserId || u.Id == d.DelegateUserId) && EF.Functions.ILike(u.FullName, p))
                || (d.Reason != null && EF.Functions.ILike(d.Reason, p)));
        }

        return await query
            .OrderByDescending(d => d.ValidFrom)
            .ThenByDescending(d => d.Id)
            .Select(d => new DelegationDto(
                d.Id,
                d.DelegatorUserId,
                db.Users.Where(u => u.Id == d.DelegatorUserId).Select(u => u.FullName).First(),
                d.DelegateUserId,
                db.Users.Where(u => u.Id == d.DelegateUserId).Select(u => u.FullName).First(),
                d.DelegatorTitle,
                d.DelegateTitle,
                d.ValidFrom,
                d.ValidTo,
                d.Reason,
                d.RevokedAt,
                d.RevokedAt == null && d.ValidFrom <= now && d.ValidTo > now))
            .ToPagedAsync(q, ct);
    }

    public async Task<Result<Guid>> CreateAsync(CreateDelegationRequest r, CancellationToken ct)
    {
        // A signer may delegate their own authority; administrators may record any delegation.
        if (!current.HasPermission(Permissions.UserManage) && r.DelegatorUserId != current.UserId)
        {
            return Error.Forbidden("DELEGATION_NOT_OWNER");
        }

        var users = await db.Users.Where(u => u.Id == r.DelegatorUserId || u.Id == r.DelegateUserId)
            .Select(u => new { u.Id, u.UserType }).ToListAsync(ct);
        if (users.Count != 2 || users.Any(u => u.UserType != UserType.Internal))
        {
            return Error.Validation("DELEGATION_USER_INVALID");
        }

        var overlap = await db.SigningDelegations.AnyAsync(
            d => d.DelegatorUserId == r.DelegatorUserId && d.RevokedAt == null && d.ValidFrom < r.ValidTo && r.ValidFrom < d.ValidTo, ct);
        if (overlap)
        {
            return Error.Conflict("DELEGATION_OVERLAP");
        }

        var delegation = new SigningDelegation(r.DelegatorUserId, r.DelegateUserId, r.DelegatorTitle, r.DelegateTitle, r.ValidFrom, r.ValidTo, r.Reason);
        db.SigningDelegations.Add(delegation);
        await db.SaveChangesAsync(ct);
        return delegation.Id;
    }

    public async Task<Result> RevokeAsync(Guid id, CancellationToken ct)
    {
        var delegation = await db.SigningDelegations.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (delegation is null)
        {
            return Result.Failure(Error.NotFound());
        }

        if (!current.HasPermission(Permissions.UserManage) && delegation.DelegatorUserId != current.UserId)
        {
            return Result.Failure(Error.Forbidden("DELEGATION_NOT_OWNER"));
        }

        delegation.Revoke(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ---------- System settings ----------

public sealed record SystemSettingDto(Guid Id, string Key, string Value, SettingValueType ValueType, string Description, decimal? Min, decimal? Max);

public sealed record UpdateSettingRequest(string Value);

/// <summary>Registers the recurring jobs (Hangfire) with the schedules of the settings in effect.</summary>
public interface IRecurringJobScheduler
{
    Task ApplyAsync(CancellationToken ct);
}

public sealed class SettingService(IAppDbContext db, IRecurringJobScheduler scheduler, IAuditLogger audit)
{
    public async Task<List<SystemSettingDto>> ListAsync(CancellationToken ct) =>
        [.. (await db.SystemSettings.AsNoTracking().OrderBy(s => s.Key).ToListAsync(ct)).Select(ToDto)];

    public async Task<Result<SystemSettingDto>> UpdateAsync(string key, UpdateSettingRequest r, CancellationToken ct)
    {
        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null)
        {
            return Error.NotFound();
        }

        var value = r.Value?.Trim() ?? string.Empty;
        if (SystemSettingKeys.RangeOf(key) is { } range
            && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            && (number < range.Min || number > range.Max))
        {
            return Error.Validation("SETTING_VALUE_OUT_OF_RANGE") with
            {
                Detail = string.Create(CultureInfo.GetCultureInfo("vi-VN"), $"Giá trị phải từ {range.Min:#,##0.##} đến {range.Max:#,##0.##}."),
            };
        }

        var old = setting.Value;
        setting.UpdateValue(value);
        audit.Log("SettingUpdate", "SystemSetting", key, new { Old = old, New = setting.Value });
        await db.SaveChangesAsync(ct);
        if (SystemSettingKeys.ScheduleKeys.Contains(key))
        {
            await scheduler.ApplyAsync(ct);
        }

        return ToDto(setting);
    }

    private static SystemSettingDto ToDto(SystemSetting s) =>
        new(s.Id, s.Key, s.Value, s.ValueType, s.Description, SystemSettingKeys.RangeOf(s.Key)?.Min, SystemSettingKeys.RangeOf(s.Key)?.Max);

    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken ct)
    {
        var value = await db.SystemSettings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    public async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken ct)
    {
        var value = await db.SystemSettings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return bool.TryParse(value, out var v) ? v : fallback;
    }

    public async Task<decimal> GetDecimalAsync(string key, decimal fallback, CancellationToken ct)
    {
        var value = await db.SystemSettings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}

// ---------- Audit log ----------

public sealed record AuditLogDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? UserId,
    string? UserName,
    string? IpAddress,
    string? UserAgent,
    string Action,
    string EntityType,
    string? EntityId,
    string? OldValues,
    string? NewValues,
    string? Note,
    string? CorrelationId,
    string? UserFullName = null,
    string ActionLabel = "",
    string EntityLabel = "");

/// <summary>A kind of audited record: stored name and its Vietnamese label.</summary>
public sealed record AuditEntityTypeDto(string Value, string Label);

public sealed record AuditLogFilter(Guid? UserId, string? Action, string? EntityType, string? EntityId, DateTimeOffset? From, DateTimeOffset? To);

public sealed class AuditLogService(IAppDbContext db)
{
    public async Task<PagedResult<AuditLogDto>> ListAsync(PageQuery q, AuditLogFilter f, CancellationToken ct)
    {
        var query = db.AuditLogs.AsNoTracking();
        if (f.UserId is not null)
        {
            query = query.Where(a => a.UserId == f.UserId);
        }

        if (!string.IsNullOrWhiteSpace(f.Action))
        {
            query = query.Where(a => a.Action == f.Action);
        }

        if (!string.IsNullOrWhiteSpace(f.EntityType))
        {
            query = query.Where(a => a.EntityType == f.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(f.EntityId))
        {
            query = query.Where(a => a.EntityId == f.EntityId);
        }

        if (f.From is not null)
        {
            query = query.Where(a => a.OccurredAt >= f.From);
        }

        if (f.To is not null)
        {
            query = query.Where(a => a.OccurredAt < f.To);
        }

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            query = query.Where(a => EF.Functions.ILike(a.UserName!, p) || EF.Functions.ILike(a.Note!, p) || EF.Functions.ILike(a.EntityId!, p));
        }

        var page = await query.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Select(a => new AuditLogDto(a.Id, a.OccurredAt, a.UserId, a.UserName, a.IpAddress, a.UserAgent, a.Action, a.EntityType,
                a.EntityId, a.OldValues, a.NewValues, a.Note, a.CorrelationId,
                db.Users.IgnoreQueryFilters().Where(u => u.Id == a.UserId).Select(u => u.FullName).FirstOrDefault(), "", ""))
            .ToPagedAsync(q, ct);

        // People read who did what to which record, in Vietnamese (spec §9.3).
        return page with
        {
            Items = [.. page.Items.Select(a => a with
            {
                UserFullName = a.UserFullName ?? a.UserName,
                ActionLabel = AuditVocabulary.Action(a.Action),
                EntityLabel = AuditVocabulary.Subject(a.EntityType),
            })],
        };
    }

    public async Task<List<AuditEntityTypeDto>> EntityTypesAsync(CancellationToken ct) =>
        [.. (await db.AuditLogs.AsNoTracking().Select(a => a.EntityType).Distinct().ToListAsync(ct))
            .Select(t => new AuditEntityTypeDto(t, AuditVocabulary.Subject(t))).OrderBy(t => t.Label, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("vi-VN"), false))];
}

// ---------- Lookups for selects ----------

public sealed record LookupItem(Guid Id, string Code, string Name);

public sealed class LookupService(IAppDbContext db)
{
    public Task<List<LookupItem>> FacilitiesAsync(CancellationToken ct) =>
        db.Facilities.AsNoTracking().Where(f => f.IsActive).OrderBy(f => f.Code).Select(f => new LookupItem(f.Id, f.Code, f.Name)).ToListAsync(ct);

    public Task<List<LookupItem>> DepartmentsAsync(Guid? facilityId, CancellationToken ct) =>
        db.Departments.AsNoTracking().Where(d => d.IsActive && (facilityId == null || d.FacilityId == facilityId))
            .OrderBy(d => d.Name).Select(d => new LookupItem(d.Id, d.Code, d.Name)).ToListAsync(ct);

    public Task<List<LookupItem>> WarehousesAsync(Guid? facilityId, CancellationToken ct) =>
        db.Warehouses.AsNoTracking().Where(w => w.IsActive && (facilityId == null || w.FacilityId == facilityId))
            .OrderBy(w => w.Name).Select(w => new LookupItem(w.Id, w.Code, w.Name)).ToListAsync(ct);

    public Task<List<LookupItem>> SuppliersAsync(string? search, CancellationToken ct)
    {
        var query = db.Suppliers.AsNoTracking().Where(s => s.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var p = PagingExtensions.LikePattern(search);
            query = query.Where(s => EF.Functions.ILike(s.Name, p) || EF.Functions.ILike(s.TaxCode, p));
        }

        return query.OrderBy(s => s.Name).Take(50).Select(s => new LookupItem(s.Id, s.TaxCode, s.Name)).ToListAsync(ct);
    }

    public Task<List<LookupItem>> RolesAsync(CancellationToken ct) =>
        db.Roles.AsNoTracking().OrderBy(r => r.Code).Select(r => new LookupItem(r.Id, r.Code, r.Name)).ToListAsync(ct);

    public Task<List<LookupItem>> InternalUsersAsync(string? search, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking().Where(u => u.UserType == UserType.Internal && u.Status == UserStatus.Active);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var p = PagingExtensions.LikePattern(search);
            query = query.Where(u => EF.Functions.ILike(u.FullName, p) || EF.Functions.ILike(u.UserName, p));
        }

        return query.OrderBy(u => u.FullName).Take(50).Select(u => new LookupItem(u.Id, u.UserName, u.FullName)).ToListAsync(ct);
    }
}
