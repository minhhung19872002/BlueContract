using BlueContract.Application.Auth;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlueContract.Infrastructure.Persistence.Seeding;

/// <summary>
/// Demo data for the sample customer (spec §12). Idempotent: every record is looked up by its business key first.
/// Later phases extend this with bid decisions, contracts and documents in every workflow state.
/// </summary>
internal sealed class DemoSeeder(AppDbContext db, IPasswordService passwords, IOptions<SeedOptions> options, ILogger<DemoSeeder> logger)
{
    private readonly Dictionary<string, Guid> _facilities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _departments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> _suppliers = new(StringComparer.Ordinal);
    private Dictionary<string, Guid> _roles = new(StringComparer.Ordinal);

    public async Task SeedAsync(CancellationToken ct)
    {
        await SeedOrganizationAsync(ct);
        await SeedFacilitiesAsync(ct);
        await SeedDepartmentsAsync(ct);
        await SeedWarehousesAsync(ct);
        await SeedSuppliersAsync(ct);
        _roles = await db.Roles.ToDictionaryAsync(r => r.Code, r => r.Id, StringComparer.Ordinal, ct);
        await SeedInternalUsersAsync(ct);
        await SeedSupplierUsersAsync(ct);
        await SeedDelegationAsync(ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Demo data ensured");
    }

    private async Task SeedOrganizationAsync(CancellationToken ct)
    {
        if (await db.Organizations.AnyAsync(ct))
        {
            return;
        }

        var org = new Organization("Bệnh viện Đa khoa Thành phố Vinh", "2900000001");
        org.Update(
            "Bệnh viện Đa khoa Thành phố Vinh",
            "BVĐK TP Vinh",
            "2900000001",
            "Thành phố Vinh, tỉnh Nghệ An",
            "0238 3800 000",
            "bvdk.tpvinh@example.vn",
            "Nguyễn Văn Minh",
            "Giám đốc",
            "3711 0000 0001",
            "Kho bạc Nhà nước tỉnh Nghệ An",
            "9527.1.1000001",
            "Kho bạc Nhà nước TP Vinh");
        db.Organizations.Add(org);
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedFacilitiesAsync(CancellationToken ct)
    {
        (string Code, string Name)[] data =
        [
            ("CS1", "Cơ sở 1"),
            ("CS2", "Cơ sở 2"),
            ("CS3", "Cơ sở 3"),
            ("YC", "Khám chữa bệnh theo yêu cầu"),
        ];
        var existing = await db.Facilities.ToDictionaryAsync(f => f.Code, f => f.Id, ct);
        foreach (var (code, name) in data)
        {
            if (!existing.TryGetValue(code, out var id))
            {
                var facility = new Facility(code, name, "Thành phố Vinh, tỉnh Nghệ An");
                facility.Update(name, "Thành phố Vinh, tỉnh Nghệ An", code, true);
                db.Facilities.Add(facility);
                id = facility.Id;
            }

            _facilities[code] = id;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedDepartmentsAsync(CancellationToken ct)
    {
        (string Code, string Name, DepartmentType Type, string Facility)[] data =
        [
            ("KD", "Khoa Dược", DepartmentType.Paraclinical, "CS1"),
            ("VTTB", "Phòng Vật tư - Thiết bị y tế", DepartmentType.Functional, "CS1"),
            ("TCHC", "Phòng Tổ chức - Hành chính", DepartmentType.Functional, "CS1"),
            ("TCKT", "Phòng Tài chính - Kế toán", DepartmentType.Functional, "CS1"),
            ("KHTH", "Phòng Kế hoạch tổng hợp", DepartmentType.Functional, "CS1"),
            ("CNTT", "Phòng Công nghệ thông tin", DepartmentType.Functional, "CS1"),
            ("KNOI", "Khoa Nội tổng hợp", DepartmentType.Clinical, "CS1"),
            ("KNGOAI", "Khoa Ngoại tổng hợp", DepartmentType.Clinical, "CS1"),
            ("KSAN", "Khoa Phụ sản", DepartmentType.Clinical, "CS1"),
            ("KNHI", "Khoa Nhi", DepartmentType.Clinical, "CS2"),
            ("KCC", "Khoa Cấp cứu", DepartmentType.Clinical, "CS1"),
            ("KHSTC", "Khoa Hồi sức tích cực - Chống độc", DepartmentType.Clinical, "CS1"),
            ("KXN", "Khoa Xét nghiệm", DepartmentType.Paraclinical, "CS1"),
            ("KCDHA", "Khoa Chẩn đoán hình ảnh", DepartmentType.Paraclinical, "CS1"),
            ("KNOI2", "Khoa Nội - Cơ sở 2", DepartmentType.Clinical, "CS2"),
            ("KKB3", "Khoa Khám bệnh - Cơ sở 3", DepartmentType.Clinical, "CS3"),
            ("KYC", "Khoa Khám chữa bệnh theo yêu cầu", DepartmentType.Clinical, "YC"),
        ];
        var existing = await db.Departments.ToDictionaryAsync(d => d.Code, d => d.Id, ct);
        foreach (var (code, name, type, facility) in data)
        {
            if (!existing.TryGetValue(code, out var id))
            {
                var department = new Department(code, name, type, _facilities[facility], "HIS_" + code);
                db.Departments.Add(department);
                id = department.Id;
            }

            _departments[code] = id;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedWarehousesAsync(CancellationToken ct)
    {
        (string Code, string Name, WarehouseType Type, string Facility, string His, string Dept)[] data =
        [
            ("KHO_THUOC", "Kho Thuốc chính", WarehouseType.Drug, "CS1", "KT01", "KD"),
            ("KHO_VTYT", "Kho Vật tư y tế", WarehouseType.MedicalSupply, "CS1", "KV01", "VTTB"),
            ("KHO_HOACHAT", "Kho Hóa chất - Sinh phẩm", WarehouseType.Chemical, "CS1", "KH01", "KXN"),
            ("KHO_TAISAN", "Kho Tài sản - Trang thiết bị", WarehouseType.Asset, "CS1", "KTS01", "VTTB"),
            ("KHO_THUOC_CS2", "Kho Thuốc Cơ sở 2", WarehouseType.Drug, "CS2", "KT02", "KD"),
        ];
        var existing = await db.Warehouses.Select(w => w.Code).ToListAsync(ct);
        foreach (var (code, name, type, facility, his, dept) in data.Where(d => !existing.Contains(d.Code)))
        {
            var warehouse = new Warehouse(code, name, type, _facilities[facility], his);
            warehouse.Update(name, type, _facilities[facility], his, _departments[dept], true);
            db.Warehouses.Add(warehouse);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedSuppliersAsync(CancellationToken ct)
    {
        (string Key, string TaxCode, string Name, string Short, string Rep, string Email)[] data =
        [
            ("ncc1", "0101234567", "Công ty Cổ phần Dược phẩm Minh An", "Dược Minh An", "Trần Thị Lan", "ncc1@example.vn"),
            ("ncc2", "0302345678", "Công ty TNHH Thiết bị Y tế Phương Đông", "TBYT Phương Đông", "Lê Văn Hùng", "ncc2@example.vn"),
            ("ncc3", "2901234567", "Công ty TNHH Kỹ thuật và Dịch vụ Hồng Lam", "Kỹ thuật Hồng Lam", "Phạm Quốc Bảo", "ncc3@example.vn"),
        ];
        var existing = await db.Suppliers.ToDictionaryAsync(s => s.TaxCode, s => s.Id, ct);
        foreach (var (key, taxCode, name, shortName, rep, email) in data)
        {
            if (!existing.TryGetValue(taxCode, out var id))
            {
                var supplier = new Supplier(taxCode, name);
                supplier.Update(name, shortName, "Việt Nam", rep, "Giám đốc", null, "0001 0002 0003", "Ngân hàng TMCP Ngoại thương Việt Nam",
                    email, "0900 000 000", true);
                db.Suppliers.Add(supplier);
                id = supplier.Id;
            }

            _suppliers[key] = id;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedInternalUsersAsync(CancellationToken ct)
    {
        (string UserName, string FullName, string Title, string Dept, string Role, (DataScopeType, string)[] Scopes)[] data =
        [
            ("admin", "Quản trị hệ thống", "Quản trị hệ thống", "CNTT", RoleCatalog.SysAdmin, []),
            ("muasam", "Nguyễn Thị Hoa", "Chuyên viên Tổ Mua sắm", "VTTB", RoleCatalog.Procurement, []),
            ("rasoat", "Hoàng Văn Nam", "Tổ trưởng Tổ Rà soát", "TCKT", RoleCatalog.Reviewer, []),
            ("khoanoi", "Đặng Thị Thu", "Điều dưỡng trưởng Khoa Nội", "KNOI", RoleCatalog.DeptUser, [(DataScopeType.Department, "KNOI")]),
            ("tonghop", "Võ Thị Mai", "Dược sĩ tổng hợp", "KD", RoleCatalog.Aggregator, [(DataScopeType.Domain, nameof(ProcurementDomain.Drug))]),
            ("tonghopvt", "Bùi Văn Long", "Chuyên viên tổng hợp vật tư", "VTTB", RoleCatalog.Aggregator,
                [(DataScopeType.Domain, nameof(ProcurementDomain.MedicalSupply)), (DataScopeType.Domain, nameof(ProcurementDomain.Chemical))]),
            ("totruongduoc", "Ngô Thị Hạnh", "Tổ trưởng Dược", "KD", RoleCatalog.DomainHead, [(DataScopeType.Domain, nameof(ProcurementDomain.Drug))]),
            ("totruongvattu", "Trịnh Văn Đức", "Tổ trưởng Vật tư", "VTTB", RoleCatalog.DomainHead,
                [(DataScopeType.Domain, nameof(ProcurementDomain.MedicalSupply)), (DataScopeType.Domain, nameof(ProcurementDomain.Chemical))]),
            ("ketoan", "Phan Thị Ngọc", "Kế toán viên", "TCKT", RoleCatalog.Accountant, []),
            ("ketoantruong", "Lý Văn Thành", "Kế toán trưởng", "TCKT", RoleCatalog.ChiefAccountant, []),
            ("ketoankho", "Đỗ Thị Yến", "Kế toán kho", "TCKT", RoleCatalog.WarehouseAccountant, []),
            ("thukho", "Mai Văn Tú", "Thủ kho Dược", "KD", RoleCatalog.Storekeeper, []),
            ("truongkhoa", "Lương Thị Hằng", "Trưởng khoa Dược", "KD", RoleCatalog.DeptHead, [(DataScopeType.Department, "KD")]),
            ("giamdoc", "Nguyễn Văn Minh", "Giám đốc", "TCHC", RoleCatalog.Director, []),
            ("phogiamdoc", "Trần Quang Vinh", "Phó Giám đốc", "TCHC", RoleCatalog.Director, []),
            ("vanthu", "Hồ Thị Kim", "Văn thư", "TCHC", RoleCatalog.Clerk, []),
            ("xem", "Cao Văn Bình", "Thanh tra", "KHTH", RoleCatalog.Viewer, []),
        ];

        var existing = await db.Users.IgnoreQueryFilters().Select(u => u.UserName).ToListAsync(ct);
        foreach (var (userName, fullName, title, dept, role, scopes) in data)
        {
            if (existing.Contains(userName))
            {
                continue;
            }

            var user = new User(userName, fullName, $"{userName}@bvdktpvinh.example.vn", UserType.Internal, null);
            var departmentId = _departments[dept];
            var facilityId = await db.Departments.Where(d => d.Id == departmentId).Select(d => d.FacilityId).FirstAsync(ct);
            user.UpdateProfile(fullName, user.Email, null, title, departmentId, facilityId);
            user.SetPasswordHash(passwords.Hash(user, options.Value.DemoPassword), mustChange: false);
            user.SetRoles([_roles[role]]);
            user.SetDataScopes(scopes.Select(s => (s.Item1, s.Item1 == DataScopeType.Department ? _departments[s.Item2].ToString() : s.Item2)));
            db.Users.Add(user);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedSupplierUsersAsync(CancellationToken ct)
    {
        var existing = await db.Users.IgnoreQueryFilters().Select(u => u.UserName).ToListAsync(ct);
        foreach (var (key, supplierId) in _suppliers)
        {
            foreach (var (userName, isAdmin) in new[] { (key, true), ($"{key}.user", false) })
            {
                if (existing.Contains(userName))
                {
                    continue;
                }

                var fullName = isAdmin ? $"Quản trị {key.ToUpperInvariant()}" : $"Nhân viên {key.ToUpperInvariant()}";
                var user = new User(userName, fullName, $"{userName}@example.vn", UserType.Supplier, supplierId);
                user.UpdateProfile(fullName, user.Email, null, isAdmin ? "Giám đốc kinh doanh" : "Nhân viên kinh doanh", null, null);
                user.SetPasswordHash(passwords.Hash(user, options.Value.DemoPassword), mustChange: false);
                user.SetRoles([_roles[isAdmin ? RoleCatalog.SupplierAdmin : RoleCatalog.SupplierUser]]);
                db.Users.Add(user);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedDelegationAsync(CancellationToken ct)
    {
        if (await db.SigningDelegations.AnyAsync(ct))
        {
            return;
        }

        var director = await db.Users.Where(u => u.UserName == "giamdoc").Select(u => u.Id).FirstAsync(ct);
        var deputy = await db.Users.Where(u => u.UserName == "phogiamdoc").Select(u => u.Id).FirstAsync(ct);
        var start = DateTimeOffset.UtcNow.Date;
        db.SigningDelegations.Add(new SigningDelegation(director, deputy, "Giám đốc", "Phó Giám đốc",
            new DateTimeOffset(start.AddDays(30), TimeSpan.Zero), new DateTimeOffset(start.AddDays(37), TimeSpan.Zero),
            "Giám đốc đi công tác (dữ liệu demo)"));
    }
}
