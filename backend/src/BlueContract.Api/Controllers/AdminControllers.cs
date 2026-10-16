using BlueContract.Api.Auth;
using BlueContract.Application.Admin;
using BlueContract.Application.Common;
using BlueContract.Application.Signing;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Organizations;
using Microsoft.AspNetCore.Mvc;
using P = BlueContract.Domain.Identity.Permissions;

namespace BlueContract.Api.Controllers;

[Route("api/admin/organization")]
public sealed class OrganizationController(OrganizationService service, SignatureImageService images) : ApiControllerBase
{
    /// <summary>Seal image drawn by "đóng dấu điện tử" (spec §8).</summary>
    [HttpPost("seal")]
    [HasPermission(P.OrganizationManage)]
    [RequestSizeLimit(FileInspector.MaxUploadBytes + (1024 * 1024))]
    public async Task<IActionResult> SetSeal(IFormFile? file, CancellationToken ct)
    {
        var upload = await TemplatesController.ReadUploadAsync(file, ct);
        return upload.IsSuccess ? FromResult(await images.SetSealAsync(upload.Value, ct)) : Problem(upload.Error!);
    }

    [HttpGet("seal")]
    [HasPermission(P.OrganizationManage, P.MasterDataView)]
    public async Task<IActionResult> GetSeal(CancellationToken ct)
    {
        var image = await images.GetSealAsync(ct);
        return image.IsSuccess ? File(image.Value.Content, image.Value.ContentType) : Problem(image.Error!);
    }

    [HttpDelete("seal")]
    [HasPermission(P.OrganizationManage)]
    public async Task<IActionResult> RemoveSeal(CancellationToken ct) => FromResult(await images.RemoveSealAsync(ct));

    [HttpGet]
    [HasPermission(P.OrganizationManage, P.MasterDataView)]
    public async Task<ActionResult<OrganizationDto>> Get(CancellationToken ct) => FromResult(await service.GetAsync(ct));

    [HttpPut]
    [HasPermission(P.OrganizationManage)]
    public async Task<ActionResult<OrganizationDto>> Update(UpdateOrganizationRequest request, CancellationToken ct) =>
        FromResult(await service.UpdateAsync(request, ct));
}

[Route("api/admin/facilities")]
public sealed class FacilitiesController(FacilityService service) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(P.FacilityManage, P.MasterDataView)]
    public Task<PagedResult<FacilityDto>> List([FromQuery] PageQuery query, [FromQuery] bool? isActive, CancellationToken ct) =>
        service.ListAsync(query, isActive, ct);

    [HttpPost]
    [HasPermission(P.FacilityManage)]
    public async Task<ActionResult<FacilityDto>> Create(SaveFacilityRequest request, CancellationToken ct) =>
        FromResult(await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(P.FacilityManage)]
    public async Task<ActionResult<FacilityDto>> Update(Guid id, SaveFacilityRequest request, CancellationToken ct) =>
        FromResult(await service.UpdateAsync(id, request, ct));
}

[Route("api/admin/departments")]
public sealed class DepartmentsController(DepartmentService service, HistoryService history, IExcelExporter excel) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(P.DepartmentManage, P.MasterDataView)]
    public Task<PagedResult<DepartmentDto>> List(
        [FromQuery] PageQuery query, [FromQuery] Guid? facilityId, [FromQuery] DepartmentType? type, [FromQuery] bool? isActive, CancellationToken ct) =>
        service.ListAsync(query, facilityId, type, isActive, ct);

    [HttpGet("export")]
    [HasPermission(P.DepartmentManage, P.MasterDataView)]
    public async Task<FileContentResult> Export(
        [FromQuery] PageQuery query, [FromQuery] Guid? facilityId, [FromQuery] DepartmentType? type, [FromQuery] bool? isActive, CancellationToken ct)
    {
        var page = await service.ListAsync(query with { Page = 1, PageSize = PageQuery.MaxExportRows, ForExport = true }, facilityId, type, isActive, ct);
        var bytes = excel.Export("Khoa phòng", [
            new ExcelColumn<DepartmentDto>("Mã", x => x.Code, Width: 14),
            new ExcelColumn<DepartmentDto>("Tên khoa/phòng", x => x.Name, Width: 40),
            new ExcelColumn<DepartmentDto>("Loại", x => Labels.Of(x.Type), Width: 18),
            new ExcelColumn<DepartmentDto>("Cơ sở", x => x.FacilityName, Width: 28),
            new ExcelColumn<DepartmentDto>("Mã HIS", x => x.HisDepartmentCode, Width: 14),
            new ExcelColumn<DepartmentDto>("Đang hoạt động", x => x.IsActive, Width: 14),
        ], page.Items);
        return Excel(bytes, "khoa-phong.xlsx");
    }

    [HttpGet("{id:guid}")]
    [HasPermission(P.DepartmentManage, P.MasterDataView)]
    public async Task<ActionResult<DepartmentDto>> Get(Guid id, CancellationToken ct) => FromResult(await service.GetAsync(id, ct));

    [HttpGet("{id:guid}/history")]
    [HasPermission(P.DepartmentManage)]
    public Task<List<HistoryEntryDto>> History(Guid id, CancellationToken ct) => history.DepartmentAsync(id, ct);

    [HttpPost]
    [HasPermission(P.DepartmentManage)]
    public async Task<ActionResult<DepartmentDto>> Create(SaveDepartmentRequest request, CancellationToken ct) =>
        FromResult(await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(P.DepartmentManage)]
    public async Task<ActionResult<DepartmentDto>> Update(Guid id, SaveDepartmentRequest request, CancellationToken ct) =>
        FromResult(await service.UpdateAsync(id, request, ct));
}

[Route("api/admin/warehouses")]
public sealed class WarehousesController(WarehouseService service) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(P.WarehouseManage, P.MasterDataView)]
    public Task<PagedResult<WarehouseDto>> List([FromQuery] PageQuery query, [FromQuery] Guid? facilityId, [FromQuery] bool? isActive, CancellationToken ct) =>
        service.ListAsync(query, facilityId, isActive, ct);

    [HttpPost]
    [HasPermission(P.WarehouseManage)]
    public async Task<ActionResult<WarehouseDto>> Create(SaveWarehouseRequest request, CancellationToken ct) =>
        FromResult(await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(P.WarehouseManage)]
    public async Task<ActionResult<WarehouseDto>> Update(Guid id, SaveWarehouseRequest request, CancellationToken ct) =>
        FromResult(await service.UpdateAsync(id, request, ct));
}

[Route("api/admin/suppliers")]
public sealed class SuppliersController(SupplierService service, HistoryService history, IExcelExporter excel) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(P.SupplierManage, P.MasterDataView)]
    public Task<PagedResult<SupplierDto>> List([FromQuery] PageQuery query, [FromQuery] bool? isActive, CancellationToken ct) =>
        service.ListAsync(query, isActive, ct);

    [HttpGet("export")]
    [HasPermission(P.SupplierManage, P.MasterDataView)]
    public async Task<FileContentResult> Export([FromQuery] PageQuery query, [FromQuery] bool? isActive, CancellationToken ct)
    {
        var page = await service.ListAsync(query with { Page = 1, PageSize = PageQuery.MaxExportRows, ForExport = true }, isActive, ct);
        var bytes = excel.Export("Nhà cung cấp", [
            new ExcelColumn<SupplierDto>("Mã số thuế", x => x.TaxCode, Width: 16),
            new ExcelColumn<SupplierDto>("Tên nhà cung cấp", x => x.Name, Width: 45),
            new ExcelColumn<SupplierDto>("Địa chỉ", x => x.Address, Width: 40),
            new ExcelColumn<SupplierDto>("Người đại diện", x => x.RepresentativeName, Width: 24),
            new ExcelColumn<SupplierDto>("Chức vụ", x => x.RepresentativeTitle, Width: 18),
            new ExcelColumn<SupplierDto>("Số tài khoản", x => x.BankAccountNumber, Width: 20),
            new ExcelColumn<SupplierDto>("Ngân hàng", x => x.BankName, Width: 30),
            new ExcelColumn<SupplierDto>("Email nhận thông báo", x => x.NotificationEmail, Width: 28),
            new ExcelColumn<SupplierDto>("Điện thoại", x => x.Phone, Width: 16),
            new ExcelColumn<SupplierDto>("Số tài khoản truy cập", x => x.AccountCount, ExcelColumnFormat.Number, 12),
            new ExcelColumn<SupplierDto>("Đang hoạt động", x => x.IsActive, Width: 12),
        ], page.Items);
        return Excel(bytes, "nha-cung-cap.xlsx");
    }

    [HttpGet("{id:guid}")]
    [HasPermission(P.SupplierManage, P.MasterDataView)]
    public async Task<ActionResult<SupplierDto>> Get(Guid id, CancellationToken ct) => FromResult(await service.GetAsync(id, ct));

    [HttpGet("{id:guid}/history")]
    [HasPermission(P.SupplierManage)]
    public Task<List<HistoryEntryDto>> History(Guid id, CancellationToken ct) => history.SupplierAsync(id, ct);

    [HttpPost]
    [HasPermission(P.SupplierManage)]
    public async Task<ActionResult<SupplierDto>> Create(SaveSupplierRequest request, CancellationToken ct) =>
        FromResult(await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(P.SupplierManage)]
    public async Task<ActionResult<SupplierDto>> Update(Guid id, SaveSupplierRequest request, CancellationToken ct) =>
        FromResult(await service.UpdateAsync(id, request, ct));

    /// <summary>Cấp tài khoản cho nhà cung cấp; credentials are emailed.</summary>
    [HttpPost("{id:guid}/accounts")]
    [HasPermission(P.SupplierManage)]
    public async Task<ActionResult<Guid>> CreateAccount(Guid id, SupplierAccountRequest request, [FromQuery] bool asAdmin, CancellationToken ct) =>
        FromResult(await service.CreateSupplierAccountAsync(id, request, asAdmin, ct));
}

[Route("api/admin/users")]
public sealed class UsersController(UserService service, HistoryService history, IExcelExporter excel) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(P.UserManage)]
    public Task<PagedResult<UserListItemDto>> List([FromQuery] PageQuery query, [FromQuery] UserListFilter filter, CancellationToken ct) =>
        service.ListAsync(query, filter, ct);

    [HttpGet("export")]
    [HasPermission(P.UserManage)]
    public async Task<FileContentResult> Export([FromQuery] PageQuery query, [FromQuery] UserListFilter filter, CancellationToken ct)
    {
        var page = await service.ListAsync(query with { Page = 1, PageSize = PageQuery.MaxExportRows, ForExport = true }, filter, ct);
        var bytes = excel.Export("Người dùng", [
            new ExcelColumn<UserListItemDto>("Tên đăng nhập", x => x.UserName, Width: 18),
            new ExcelColumn<UserListItemDto>("Họ tên", x => x.FullName, Width: 28),
            new ExcelColumn<UserListItemDto>("Email", x => x.Email, Width: 30),
            new ExcelColumn<UserListItemDto>("Chức danh", x => x.JobTitle, Width: 24),
            new ExcelColumn<UserListItemDto>("Loại", x => Labels.Of(x.UserType), Width: 14),
            new ExcelColumn<UserListItemDto>("Khoa/phòng", x => x.DepartmentName, Width: 28),
            new ExcelColumn<UserListItemDto>("Nhà cung cấp", x => x.SupplierName, Width: 32),
            new ExcelColumn<UserListItemDto>("Vai trò", x => string.Join(", ", x.RoleCodes), Width: 28),
            new ExcelColumn<UserListItemDto>("Trạng thái", x => Labels.Of(x.Status), Width: 14),
            new ExcelColumn<UserListItemDto>("Đăng nhập gần nhất", x => x.LastLoginAt, ExcelColumnFormat.DateTime, 18),
        ], page.Items);
        return Excel(bytes, "nguoi-dung.xlsx");
    }

    [HttpGet("{id:guid}")]
    [HasPermission(P.UserManage)]
    public async Task<ActionResult<UserDetailDto>> Get(Guid id, CancellationToken ct) => FromResult(await service.GetAsync(id, ct));

    [HttpGet("{id:guid}/history")]
    [HasPermission(P.UserManage)]
    public Task<List<HistoryEntryDto>> History(Guid id, CancellationToken ct) => history.UserAsync(id, ct);

    [HttpPost]
    [HasPermission(P.UserManage)]
    public async Task<ActionResult<UserDetailDto>> Create(CreateUserRequest request, CancellationToken ct) =>
        FromResult(await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(P.UserManage)]
    public async Task<ActionResult<UserDetailDto>> Update(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, request, ct);
        AuthSetup.InvalidateStamp(HttpContext.RequestServices, id);
        return FromResult(result);
    }

    [HttpPost("{id:guid}/unlock")]
    [HasPermission(P.UserManage)]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken ct) => FromResult(await service.UnlockAsync(id, ct));

    [HttpPost("{id:guid}/reset-password")]
    [HasPermission(P.UserManage)]
    public async Task<ActionResult<ResetPasswordResponse>> ResetPassword(Guid id, CancellationToken ct)
    {
        var result = await service.ResetPasswordAsync(id, ct);
        AuthSetup.InvalidateStamp(HttpContext.RequestServices, id);
        return FromResult(result);
    }
}

[Route("api/admin/roles")]
public sealed class RolesController(RoleService service) : ApiControllerBase
{
    [HttpGet("matrix")]
    [HasPermission(P.RoleManage)]
    public Task<PermissionMatrixDto> Matrix(CancellationToken ct) => service.GetMatrixAsync(ct);

    [HttpPost]
    [HasPermission(P.RoleManage)]
    public async Task<ActionResult<Guid>> Create(SaveRoleRequest request, CancellationToken ct) => FromResult(await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(P.RoleManage)]
    public async Task<IActionResult> Update(Guid id, SaveRoleRequest request, CancellationToken ct) => FromResult(await service.UpdateAsync(id, request, ct));

    [HttpPut("{id:guid}/permissions")]
    [HasPermission(P.RoleManage)]
    public async Task<IActionResult> SetPermissions(Guid id, SetRolePermissionsRequest request, CancellationToken ct) =>
        FromResult(await service.SetPermissionsAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [HasPermission(P.RoleManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => FromResult(await service.DeleteAsync(id, ct));
}

[Route("api/admin/delegations")]
public sealed class DelegationsController(DelegationService service) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(P.DelegationManage, P.UserManage)]
    public Task<List<DelegationDto>> List([FromQuery] bool onlyEffective, CancellationToken ct) => service.ListAsync(onlyEffective, ct);

    [HttpPost]
    [HasPermission(P.DelegationManage, P.UserManage)]
    public async Task<ActionResult<Guid>> Create(CreateDelegationRequest request, CancellationToken ct) => FromResult(await service.CreateAsync(request, ct));

    [HttpPost("{id:guid}/revoke")]
    [HasPermission(P.DelegationManage, P.UserManage)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct) => FromResult(await service.RevokeAsync(id, ct));
}

[Route("api/admin/settings")]
public sealed class SettingsController(SettingService service) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(P.SettingManage)]
    public Task<List<SystemSettingDto>> List(CancellationToken ct) => service.ListAsync(ct);

    [HttpPut("{key}")]
    [HasPermission(P.SettingManage)]
    public async Task<ActionResult<SystemSettingDto>> Update(string key, UpdateSettingRequest request, CancellationToken ct) =>
        FromResult(await service.UpdateAsync(key, request, ct));
}

[Route("api/admin/audit-logs")]
public sealed class AuditLogsController(AuditLogService service, IExcelExporter excel) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(P.AuditView)]
    public Task<PagedResult<AuditLogDto>> List([FromQuery] PageQuery query, [FromQuery] AuditLogFilter filter, CancellationToken ct) =>
        service.ListAsync(query, filter, ct);

    [HttpGet("entity-types")]
    [HasPermission(P.AuditView)]
    public Task<List<string>> EntityTypes(CancellationToken ct) => service.EntityTypesAsync(ct);

    [HttpGet("export")]
    [HasPermission(P.AuditView)]
    public async Task<FileContentResult> Export([FromQuery] PageQuery query, [FromQuery] AuditLogFilter filter, CancellationToken ct)
    {
        var page = await service.ListAsync(query with { Page = 1, PageSize = PageQuery.MaxExportRows, ForExport = true }, filter, ct);
        var bytes = excel.Export("Nhật ký", [
            new ExcelColumn<AuditLogDto>("Thời gian", x => x.OccurredAt, ExcelColumnFormat.DateTime, 18),
            new ExcelColumn<AuditLogDto>("Người dùng", x => x.UserName, Width: 18),
            new ExcelColumn<AuditLogDto>("Hành động", x => x.Action, Width: 16),
            new ExcelColumn<AuditLogDto>("Đối tượng", x => x.EntityType, Width: 20),
            new ExcelColumn<AuditLogDto>("Mã đối tượng", x => x.EntityId, Width: 38),
            new ExcelColumn<AuditLogDto>("Địa chỉ IP", x => x.IpAddress, Width: 16),
            new ExcelColumn<AuditLogDto>("Ghi chú", x => x.Note, Width: 30),
            new ExcelColumn<AuditLogDto>("Dữ liệu trước", x => x.OldValues, Width: 50),
            new ExcelColumn<AuditLogDto>("Dữ liệu sau", x => x.NewValues, Width: 50),
        ], page.Items);
        return Excel(bytes, "nhat-ky-hoat-dong.xlsx");
    }
}

/// <summary>Small option lists for selects; available to any signed-in user.</summary>
[Route("api/lookups")]
[Microsoft.AspNetCore.Authorization.Authorize]
public sealed class LookupsController(LookupService service) : ApiControllerBase
{
    [HttpGet("facilities")]
    public Task<List<LookupItem>> Facilities(CancellationToken ct) => service.FacilitiesAsync(ct);

    [HttpGet("departments")]
    public Task<List<LookupItem>> Departments([FromQuery] Guid? facilityId, CancellationToken ct) => service.DepartmentsAsync(facilityId, ct);

    [HttpGet("warehouses")]
    public Task<List<LookupItem>> Warehouses([FromQuery] Guid? facilityId, CancellationToken ct) => service.WarehousesAsync(facilityId, ct);

    [HttpGet("suppliers")]
    public Task<List<LookupItem>> Suppliers([FromQuery] string? search, CancellationToken ct) => service.SuppliersAsync(search, ct);

    [HttpGet("roles")]
    [HasPermission(P.UserManage, P.RoleManage)]
    public Task<List<LookupItem>> Roles(CancellationToken ct) => service.RolesAsync(ct);

    [HttpGet("internal-users")]
    [HasPermission(P.UserManage, P.DelegationManage)]
    public Task<List<LookupItem>> InternalUsers([FromQuery] string? search, CancellationToken ct) => service.InternalUsersAsync(search, ct);
}

/// <summary>Supplier portal — "Tài khoản công ty".</summary>
[Route("api/supplier/company")]
public sealed class SupplierCompanyController(SupplierCompanyService service) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(P.SupplierCompanyView)]
    public async Task<ActionResult<SupplierDto>> Get(CancellationToken ct) => FromResult(await service.GetMyCompanyAsync(ct));

    [HttpGet("staff")]
    [HasPermission(P.SupplierCompanyView)]
    public async Task<ActionResult<List<SupplierStaffDto>>> Staff(CancellationToken ct) => FromResult(await service.ListStaffAsync(ct));

    [HttpPost("staff")]
    [HasPermission(P.SupplierStaffManage)]
    public async Task<ActionResult<Guid>> CreateStaff(CreateSupplierStaffRequest request, CancellationToken ct) =>
        FromResult(await service.CreateStaffAsync(request, ct));

    [HttpPut("staff/{id:guid}")]
    [HasPermission(P.SupplierStaffManage)]
    public async Task<IActionResult> UpdateStaff(Guid id, UpdateSupplierStaffRequest request, CancellationToken ct) =>
        FromResult(await service.UpdateStaffAsync(id, request, ct));
}

/// <summary>Vietnamese labels for enums in exports.</summary>
internal static class Labels
{
    public static string Of(DepartmentType t) => t switch
    {
        DepartmentType.Clinical => "Lâm sàng",
        DepartmentType.Paraclinical => "Cận lâm sàng",
        _ => "Phòng chức năng",
    };

    public static string Of(UserType t) => t == UserType.Supplier ? "Nhà cung cấp" : "Nội bộ";

    public static string Of(UserStatus s) => s switch
    {
        UserStatus.Active => "Hoạt động",
        UserStatus.Inactive => "Ngừng hoạt động",
        _ => "Chờ kích hoạt",
    };
}
