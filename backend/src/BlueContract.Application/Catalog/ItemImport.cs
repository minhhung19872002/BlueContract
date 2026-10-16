using BlueContract.Domain.Catalog;

namespace BlueContract.Application.Catalog;

/// <summary>Columns of the item import workbooks (spec §10 items 1–2). Order = column order in the sheet.</summary>
public static class ItemImportColumns
{
    public const string HisKey = "HisKey";
    public const string Code = "Code";
    public const string Name = "Name";
    public const string ActiveIngredient = "ActiveIngredient";
    public const string Strength = "Strength";
    public const string Specification = "Specification";
    public const string Unit = "Unit";
    public const string Manufacturer = "Manufacturer";
    public const string Country = "Country";
    public const string RegistrationNumber = "RegistrationNumber";
    public const string BidGroup = "BidGroup";
    public const string Category = "Category";
    public const string SupplierTaxCode = "SupplierTaxCode";
    public const string Quantity = "Quantity";
    public const string UnitPrice = "UnitPrice";
    public const string VatRate = "VatRate";

    public sealed record Column(string Key, string Header, bool Required, double Width, string? Note = null);

    public static IReadOnlyList<Column> For(ItemImportKind kind) => kind == ItemImportKind.Medical ? Medical : General;

    private static readonly Column[] Medical =
    [
        new(HisKey, "Mã key HIS", false, 14, "Mã định danh mặt hàng trên HIS (nếu đã có)"),
        new(Code, "Mã hàng (*)", true, 14),
        new(Name, "Tên thuốc / vật tư / hóa chất (*)", true, 34),
        new(ActiveIngredient, "Hoạt chất / thành phần", false, 26),
        new(Strength, "Nồng độ, hàm lượng", false, 16),
        new(Specification, "Quy cách đóng gói", false, 18),
        new(Unit, "Đơn vị tính (*)", true, 10),
        new(Manufacturer, "Hãng sản xuất", false, 20),
        new(Country, "Nước sản xuất", false, 14),
        new(RegistrationNumber, "Số đăng ký / GPNK", false, 16),
        new(BidGroup, "Nhóm thầu", false, 10),
        new(Category, "Mã hạng mục", false, 12, "THUOC, VTYT, HOA_CHAT, SINH_PHAM — để trống: theo quyết định thầu"),
        new(SupplierTaxCode, "MST nhà cung cấp (*)", true, 16, "Mã số thuế NCC trúng thầu (10 hoặc 13 ký tự)"),
        new(Quantity, "Số lượng trúng thầu (*)", true, 14),
        new(UnitPrice, "Đơn giá trúng thầu (*)", true, 16, "Đơn giá đã gồm thuế GTGT, đơn vị: đồng"),
        new(VatRate, "Thuế suất GTGT (%)", false, 12, "Mặc định 5% (thuốc, VTYT)"),
    ];

    private static readonly Column[] General =
    [
        new(HisKey, "Mã key HIS", false, 14, "Để trống với dịch vụ, sửa chữa không quản lý kho"),
        new(Code, "Mã hàng / dịch vụ (*)", true, 14),
        new(Name, "Tên hàng hóa / tài sản / dịch vụ (*)", true, 36),
        new(ActiveIngredient, "Thông số kỹ thuật / mô tả", false, 34),
        new(Specification, "Quy cách", false, 16),
        new(Unit, "Đơn vị tính (*)", true, 10),
        new(Manufacturer, "Hãng sản xuất", false, 20),
        new(Country, "Xuất xứ", false, 14),
        new(RegistrationNumber, "Ký mã hiệu / Model", false, 16),
        new(Category, "Mã hạng mục", false, 12, "TAI_SAN, HANG_HOA, DICH_VU, SUA_CHUA — để trống: theo quyết định thầu / hợp đồng"),
        new(SupplierTaxCode, "MST nhà cung cấp", false, 16, "Bắt buộc khi import vào quyết định thầu"),
        new(Quantity, "Số lượng (*)", true, 12),
        new(UnitPrice, "Đơn giá (*)", true, 16, "Đơn giá đã gồm thuế GTGT, đơn vị: đồng"),
        new(VatRate, "Thuế suất GTGT (%)", false, 12, "Mặc định 10%"),
    ];
}

/// <summary>One data row read from a workbook: raw text per column key (trimmed, empty → null).</summary>
public sealed record ItemSheetRow(int RowNumber, IReadOnlyDictionary<string, string?> Values);

/// <summary>Reads and writes the item import workbooks (implemented with ClosedXML).</summary>
public interface IItemWorkbook
{
    /// <summary>Reads the first sheet; throws <see cref="InvalidDataException"/> when the file is not a valid workbook.</summary>
    IReadOnlyList<ItemSheetRow> Read(byte[] xlsx, ItemImportKind kind);

    /// <summary>Blank template with headers, notes, an instructions sheet and the category list.</summary>
    byte[] CreateTemplate(ItemImportKind kind, IReadOnlyList<(string Code, string Name)> categories);
}

public sealed record ImportCellError(string Column, string Message);

public sealed record ImportRowDto(
    int RowNumber,
    string? HisKey,
    string? Code,
    string? Name,
    string? Unit,
    string? SupplierTaxCode,
    string? SupplierName,
    string? CategoryCode,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal? VatRate,
    bool IsUpdate,
    IReadOnlyList<ImportCellError> Errors);

public sealed record ImportPreviewDto(int TotalRows, int ValidRows, int ErrorRows, IReadOnlyList<ImportRowDto> Rows);

public sealed record ImportResultDto(int Created, int Updated);

/// <summary>A validated row ready to be written.</summary>
public sealed record ParsedItemRow(
    int RowNumber,
    string? HisKey,
    string Code,
    string Name,
    string? ActiveIngredient,
    string? Strength,
    string? Specification,
    string Unit,
    string? Manufacturer,
    string? Country,
    string? RegistrationNumber,
    string? BidGroup,
    Guid CategoryId,
    Guid? SupplierId,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRate);
