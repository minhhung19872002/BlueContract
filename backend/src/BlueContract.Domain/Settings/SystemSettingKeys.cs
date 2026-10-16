namespace BlueContract.Domain.Settings;

/// <summary>Well-known setting keys and their defaults (seeded on first start).</summary>
public static class SystemSettingKeys
{
    public const string AcceptanceJobDayOfMonth = "acceptance.job_day_of_month";
    public const string InvoiceRoundingTolerance = "receipt.invoice_rounding_tolerance";
    public const string ExpiryWarningDays = "receipt.expiry_warning_days";
    public const string ContractExpiryWarningDays = "contract.expiry_warning_days";
    public const string ContractLowQuantityPercent = "contract.low_quantity_percent";
    public const string WorkflowDefaultSlaHours = "workflow.default_sla_hours";

    public static IReadOnlyList<SystemSetting> Defaults() =>
    [
        new(AcceptanceJobDayOfMonth, "1", SettingValueType.WholeNumber, "Ngày trong tháng chạy tổng hợp nghiệm thu tự động"),
        new(InvoiceRoundingTolerance, "1000", SettingValueType.DecimalNumber, "Sai số làm tròn cho phép giữa tổng tiền hóa đơn và phiếu nhập kho (₫)"),
        new(ExpiryWarningDays, "180", SettingValueType.WholeNumber, "Cảnh báo khi hạn dùng còn ít hơn số ngày này"),
        new(ContractExpiryWarningDays, "30", SettingValueType.WholeNumber, "Cảnh báo hợp đồng sắp hết hạn (ngày)"),
        new(ContractLowQuantityPercent, "10", SettingValueType.DecimalNumber, "Cảnh báo hợp đồng sắp hết số lượng (%)"),
        new(WorkflowDefaultSlaHours, "24", SettingValueType.WholeNumber, "SLA mặc định cho mỗi bước ký (giờ) để nhắc việc"),
    ];
}
