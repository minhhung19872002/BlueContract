using BlueContract.Domain.Catalog;
using BlueContract.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BlueContract.Infrastructure.Persistence.Seeding;

/// <summary>Item categories (spec §5.1). Reference data, seeded in every environment; existing rows are never changed.</summary>
internal sealed class CatalogSeeder(AppDbContext db, ILogger<CatalogSeeder> logger)
{
    public static readonly (string Code, string Name, ProcurementDomain Domain, ItemImportKind Kind, string Template)[] Categories =
    [
        ("THUOC", "Thuốc", ProcurementDomain.Drug, ItemImportKind.Medical, "HD_THUOC"),
        ("VTYT", "Vật tư y tế", ProcurementDomain.MedicalSupply, ItemImportKind.Medical, "HD_VTYT"),
        ("HOA_CHAT", "Hóa chất", ProcurementDomain.Chemical, ItemImportKind.Medical, "HD_VTYT"),
        ("SINH_PHAM", "Sinh phẩm xét nghiệm", ProcurementDomain.Chemical, ItemImportKind.Medical, "HD_VTYT"),
        ("TAI_SAN", "Tài sản, trang thiết bị", ProcurementDomain.Equipment, ItemImportKind.General, "HD_HANGHOA_THAU"),
        ("HANG_HOA", "Hàng hóa", ProcurementDomain.Administration, ItemImportKind.General, "HD_HANGHOA_THAU"),
        ("DICH_VU", "Dịch vụ", ProcurementDomain.Administration, ItemImportKind.General, "HD_DICHVU_THAU"),
        ("SUA_CHUA", "Sửa chữa", ProcurementDomain.Administration, ItemImportKind.General, "HD_DICHVU_KHONGTHAU"),
    ];

    public async Task SeedAsync(CancellationToken ct)
    {
        var existing = await db.ItemCategories.IgnoreQueryFilters().Select(c => c.Code).ToListAsync(ct);
        var order = 0;
        var created = 0;
        foreach (var (code, name, domain, kind, template) in Categories)
        {
            order += 10;
            if (existing.Contains(code))
            {
                continue;
            }

            db.ItemCategories.Add(new ItemCategory(code, name, domain, kind, template, order));
            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Count} item categories", created);
        }
    }
}
