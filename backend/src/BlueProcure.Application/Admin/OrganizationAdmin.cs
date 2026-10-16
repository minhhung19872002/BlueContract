using System.Linq.Expressions;
using BlueProcure.Application.Common;
using BlueProcure.Domain.Organizations;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BlueProcure.Application.Admin;

// ---------- Organization ----------

public sealed record OrganizationDto(
    Guid Id,
    string Name,
    string? ShortName,
    string TaxCode,
    string? Address,
    string? Phone,
    string? Email,
    string? RepresentativeName,
    string? RepresentativeTitle,
    string? BankAccountNumber,
    string? BankName,
    string? TreasuryAccountNumber,
    string? TreasuryName,
    bool HasLogo,
    bool HasSealImage);

public sealed record UpdateOrganizationRequest(
    string Name,
    string? ShortName,
    string TaxCode,
    string? Address,
    string? Phone,
    string? Email,
    string? RepresentativeName,
    string? RepresentativeTitle,
    string? BankAccountNumber,
    string? BankName,
    string? TreasuryAccountNumber,
    string? TreasuryName);

internal sealed class UpdateOrganizationRequestValidator : AbstractValidator<UpdateOrganizationRequest>
{
    public UpdateOrganizationRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithName("Tên đơn vị").MaximumLength(300);
        RuleFor(x => x.TaxCode).NotEmpty().WithName("Mã số thuế").MaximumLength(20);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithName("Email");
    }
}

public sealed class OrganizationService(IAppDbContext db)
{
    public async Task<Result<OrganizationDto>> GetAsync(CancellationToken ct)
    {
        var org = await db.Organizations.AsNoTracking().OrderBy(o => o.CreatedAt).FirstOrDefaultAsync(ct);
        return org is null ? Error.NotFound("ORGANIZATION_NOT_CONFIGURED") : ToDto(org);
    }

    public async Task<Result<OrganizationDto>> UpdateAsync(UpdateOrganizationRequest r, CancellationToken ct)
    {
        var org = await db.Organizations.OrderBy(o => o.CreatedAt).FirstOrDefaultAsync(ct);
        if (org is null)
        {
            org = new Organization(r.Name, r.TaxCode);
            db.Organizations.Add(org);
        }

        org.Update(r.Name, r.ShortName, r.TaxCode, r.Address, r.Phone, r.Email, r.RepresentativeName, r.RepresentativeTitle,
            r.BankAccountNumber, r.BankName, r.TreasuryAccountNumber, r.TreasuryName);
        await db.SaveChangesAsync(ct);
        return ToDto(org);
    }

    private static OrganizationDto ToDto(Organization o) => new(
        o.Id, o.Name, o.ShortName, o.TaxCode, o.Address, o.Phone, o.Email, o.RepresentativeName, o.RepresentativeTitle,
        o.BankAccountNumber, o.BankName, o.TreasuryAccountNumber, o.TreasuryName, o.LogoFileKey is not null, o.SealImageFileKey is not null);
}

// ---------- Facility ----------

public sealed record FacilityDto(Guid Id, string Code, string Name, string? Address, string? HisFacilityCode, bool IsActive);

public sealed record SaveFacilityRequest(string Code, string Name, string? Address, string? HisFacilityCode, bool IsActive = true);

internal sealed class SaveFacilityRequestValidator : AbstractValidator<SaveFacilityRequest>
{
    public SaveFacilityRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithName("Mã cơ sở").MaximumLength(30);
        RuleFor(x => x.Name).NotEmpty().WithName("Tên cơ sở").MaximumLength(200);
    }
}

public sealed class FacilityService(IAppDbContext db)
{
    private static readonly Dictionary<string, Expression<Func<Facility, object?>>> Sorts = new()
    {
        ["code"] = x => x.Code,
        ["name"] = x => x.Name,
    };

    public Task<PagedResult<FacilityDto>> ListAsync(PageQuery q, bool? isActive, CancellationToken ct)
    {
        var query = db.Facilities.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            query = query.Where(f => EF.Functions.ILike(f.Code, p) || EF.Functions.ILike(f.Name, p));
        }

        if (isActive is not null)
        {
            query = query.Where(f => f.IsActive == isActive);
        }

        return query.ApplySort(q, Sorts, "code")
            .Select(f => new FacilityDto(f.Id, f.Code, f.Name, f.Address, f.HisFacilityCode, f.IsActive))
            .ToPagedAsync(q, ct);
    }

    public async Task<Result<FacilityDto>> CreateAsync(SaveFacilityRequest r, CancellationToken ct)
    {
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.Facilities.IgnoreQueryFilters().AnyAsync(f => f.Code == code, ct))
        {
            return Error.Conflict("FACILITY_CODE_EXISTS");
        }

        var facility = new Facility(code, r.Name, r.Address);
        facility.Update(r.Name, r.Address, r.HisFacilityCode, r.IsActive);
        db.Facilities.Add(facility);
        await db.SaveChangesAsync(ct);
        return ToDto(facility);
    }

    public async Task<Result<FacilityDto>> UpdateAsync(Guid id, SaveFacilityRequest r, CancellationToken ct)
    {
        var facility = await db.Facilities.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (facility is null)
        {
            return Error.NotFound();
        }

        facility.Update(r.Name, r.Address, r.HisFacilityCode, r.IsActive);
        await db.SaveChangesAsync(ct);
        return ToDto(facility);
    }

    private static FacilityDto ToDto(Facility f) => new(f.Id, f.Code, f.Name, f.Address, f.HisFacilityCode, f.IsActive);
}

// ---------- Department ----------

public sealed record DepartmentDto(
    Guid Id,
    string Code,
    string Name,
    DepartmentType Type,
    Guid FacilityId,
    string? FacilityName,
    string? HisDepartmentCode,
    bool IsActive);

public sealed record SaveDepartmentRequest(string Code, string Name, DepartmentType Type, Guid FacilityId, string? HisDepartmentCode, bool IsActive = true);

internal sealed class SaveDepartmentRequestValidator : AbstractValidator<SaveDepartmentRequest>
{
    public SaveDepartmentRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithName("Mã khoa/phòng").MaximumLength(30);
        RuleFor(x => x.Name).NotEmpty().WithName("Tên khoa/phòng").MaximumLength(200);
        RuleFor(x => x.Type).IsInEnum().WithName("Loại");
        RuleFor(x => x.FacilityId).NotEmpty().WithName("Cơ sở");
    }
}

public sealed class DepartmentService(IAppDbContext db)
{
    private static readonly Dictionary<string, Expression<Func<Department, object?>>> Sorts = new()
    {
        ["code"] = x => x.Code,
        ["name"] = x => x.Name,
        ["facilityName"] = x => x.Facility!.Name,
        ["type"] = x => x.Type,
    };

    public Task<PagedResult<DepartmentDto>> ListAsync(PageQuery q, Guid? facilityId, DepartmentType? type, bool? isActive, CancellationToken ct)
    {
        var query = db.Departments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            query = query.Where(d => EF.Functions.ILike(d.Code, p) || EF.Functions.ILike(d.Name, p));
        }

        if (facilityId is not null)
        {
            query = query.Where(d => d.FacilityId == facilityId);
        }

        if (type is not null)
        {
            query = query.Where(d => d.Type == type);
        }

        if (isActive is not null)
        {
            query = query.Where(d => d.IsActive == isActive);
        }

        return query
            .ApplySort(q, Sorts, "code")
            .Select(d => new DepartmentDto(d.Id, d.Code, d.Name, d.Type, d.FacilityId, d.Facility!.Name, d.HisDepartmentCode, d.IsActive))
            .ToPagedAsync(q, ct);
    }

    public async Task<Result<DepartmentDto>> CreateAsync(SaveDepartmentRequest r, CancellationToken ct)
    {
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.Departments.IgnoreQueryFilters().AnyAsync(d => d.Code == code, ct))
        {
            return Error.Conflict("DEPARTMENT_CODE_EXISTS");
        }

        if (!await db.Facilities.AnyAsync(f => f.Id == r.FacilityId, ct))
        {
            return Error.Validation("FACILITY_NOT_FOUND");
        }

        var department = new Department(code, r.Name, r.Type, r.FacilityId, r.HisDepartmentCode);
        department.Update(r.Name, r.Type, r.FacilityId, r.HisDepartmentCode, r.IsActive);
        db.Departments.Add(department);
        await db.SaveChangesAsync(ct);
        return await GetAsync(department.Id, ct);
    }

    public async Task<Result<DepartmentDto>> UpdateAsync(Guid id, SaveDepartmentRequest r, CancellationToken ct)
    {
        var department = await db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (department is null)
        {
            return Error.NotFound();
        }

        if (!await db.Facilities.AnyAsync(f => f.Id == r.FacilityId, ct))
        {
            return Error.Validation("FACILITY_NOT_FOUND");
        }

        department.Update(r.Name, r.Type, r.FacilityId, r.HisDepartmentCode, r.IsActive);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<Result<DepartmentDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await db.Departments.AsNoTracking().Where(d => d.Id == id)
            .Select(d => new DepartmentDto(d.Id, d.Code, d.Name, d.Type, d.FacilityId, d.Facility!.Name, d.HisDepartmentCode, d.IsActive))
            .FirstOrDefaultAsync(ct);
        return dto is null ? Error.NotFound() : dto;
    }
}

// ---------- Warehouse ----------

public sealed record WarehouseDto(
    Guid Id,
    string Code,
    string Name,
    WarehouseType Type,
    Guid FacilityId,
    string? FacilityName,
    string? HisWarehouseCode,
    Guid? ManagingDepartmentId,
    bool IsActive);

public sealed record SaveWarehouseRequest(
    string Code,
    string Name,
    WarehouseType Type,
    Guid FacilityId,
    string? HisWarehouseCode,
    Guid? ManagingDepartmentId,
    bool IsActive = true);

internal sealed class SaveWarehouseRequestValidator : AbstractValidator<SaveWarehouseRequest>
{
    public SaveWarehouseRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithName("Mã kho").MaximumLength(30);
        RuleFor(x => x.Name).NotEmpty().WithName("Tên kho").MaximumLength(200);
        RuleFor(x => x.Type).IsInEnum().WithName("Loại kho");
        RuleFor(x => x.FacilityId).NotEmpty().WithName("Cơ sở");
    }
}

public sealed class WarehouseService(IAppDbContext db)
{
    private static readonly Dictionary<string, Expression<Func<Warehouse, object?>>> Sorts = new()
    {
        ["code"] = x => x.Code,
        ["name"] = x => x.Name,
        ["facilityName"] = x => x.Facility!.Name,
    };

    public Task<PagedResult<WarehouseDto>> ListAsync(PageQuery q, Guid? facilityId, bool? isActive, CancellationToken ct)
    {
        var query = db.Warehouses.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            query = query.Where(w => EF.Functions.ILike(w.Code, p) || EF.Functions.ILike(w.Name, p));
        }

        if (facilityId is not null)
        {
            query = query.Where(w => w.FacilityId == facilityId);
        }

        if (isActive is not null)
        {
            query = query.Where(w => w.IsActive == isActive);
        }

        return query
            .ApplySort(q, Sorts, "code")
            .Select(w => new WarehouseDto(w.Id, w.Code, w.Name, w.Type, w.FacilityId, w.Facility!.Name, w.HisWarehouseCode, w.ManagingDepartmentId, w.IsActive))
            .ToPagedAsync(q, ct);
    }

    public async Task<Result<WarehouseDto>> CreateAsync(SaveWarehouseRequest r, CancellationToken ct)
    {
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.Warehouses.IgnoreQueryFilters().AnyAsync(w => w.Code == code, ct))
        {
            return Error.Conflict("WAREHOUSE_CODE_EXISTS");
        }

        if (!await db.Facilities.AnyAsync(f => f.Id == r.FacilityId, ct))
        {
            return Error.Validation("FACILITY_NOT_FOUND");
        }

        var warehouse = new Warehouse(code, r.Name, r.Type, r.FacilityId, r.HisWarehouseCode);
        warehouse.Update(r.Name, r.Type, r.FacilityId, r.HisWarehouseCode, r.ManagingDepartmentId, r.IsActive);
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync(ct);
        return ToDto(warehouse);
    }

    public async Task<Result<WarehouseDto>> UpdateAsync(Guid id, SaveWarehouseRequest r, CancellationToken ct)
    {
        var warehouse = await db.Warehouses.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (warehouse is null)
        {
            return Error.NotFound();
        }

        warehouse.Update(r.Name, r.Type, r.FacilityId, r.HisWarehouseCode, r.ManagingDepartmentId, r.IsActive);
        await db.SaveChangesAsync(ct);
        return ToDto(warehouse);
    }

    private static WarehouseDto ToDto(Warehouse w) =>
        new(w.Id, w.Code, w.Name, w.Type, w.FacilityId, null, w.HisWarehouseCode, w.ManagingDepartmentId, w.IsActive);
}
