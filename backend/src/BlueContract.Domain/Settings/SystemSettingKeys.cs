namespace BlueContract.Domain.Settings;

/// <summary>Well-known setting keys, their defaults (seeded on first start) and allowed ranges (spec §11).</summary>
public static class SystemSettingKeys
{
    public const string AcceptanceJobDayOfMonth = "acceptance.job_day_of_month";
    public const string InvoiceRoundingTolerance = "receipt.invoice_rounding_tolerance";
    public const string ExpiryWarningDays = "receipt.expiry_warning_days";
    public const string ContractExpiryWarningDays = "contract.expiry_warning_days";
    public const string ContractLowQuantityPercent = "contract.low_quantity_percent";
    public const string WorkflowDefaultSlaHours = "workflow.default_sla_hours";
    public const string SummaryResignAfterHeadEdit = "proposal.summary_resign_after_head_edit";
    public const string CertificateExpiryWarningDays = "certificate.expiry_warning_days";
    public const string AcceptanceJobHour = "job.acceptance_hour";
    public const string IntegrityCheckHour = "job.integrity_check_hour";
    public const string WorkflowReminderMinutes = "job.workflow_reminder_minutes";

    /// <summary>Settings that change when a recurring job runs: saving one re-registers the jobs.</summary>
    public static readonly IReadOnlySet<string> ScheduleKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        AcceptanceJobHour,
        IntegrityCheckHour,
        WorkflowReminderMinutes,
    };

    private static readonly Dictionary<string, (decimal Min, decimal Max)> Ranges = new(StringComparer.Ordinal)
    {
        [AcceptanceJobDayOfMonth] = (1, 28),
        [InvoiceRoundingTolerance] = (0, 1_000_000),
        [ExpiryWarningDays] = (0, 3650),
        [ContractExpiryWarningDays] = (1, 365),
        [ContractLowQuantityPercent] = (0, 100),
        [WorkflowDefaultSlaHours] = (0, 720),
        [CertificateExpiryWarningDays] = (1, 365),
        [AcceptanceJobHour] = (0, 23),
        [IntegrityCheckHour] = (0, 23),
        [WorkflowReminderMinutes] = (5, 60),
    };

    public static IReadOnlyList<SystemSetting> Defaults() =>
    [
        new(AcceptanceJobDayOfMonth, "1", SettingValueType.WholeNumber, "Ngày trong tháng chạy tổng hợp nghiệm thu tự động"),
        new(InvoiceRoundingTolerance, "1000", SettingValueType.DecimalNumber, "Sai số làm tròn cho phép giữa tổng tiền hóa đơn và phiếu nhập kho (₫)"),
        new(ExpiryWarningDays, "180", SettingValueType.WholeNumber, "Cảnh báo khi hạn dùng còn ít hơn số ngày này"),
        new(ContractExpiryWarningDays, "30", SettingValueType.WholeNumber, "Cảnh báo hợp đồng sắp hết hạn (ngày)"),
        new(ContractLowQuantityPercent, "10", SettingValueType.DecimalNumber, "Cảnh báo hợp đồng sắp hết số lượng (%)"),
        new(WorkflowDefaultSlaHours, "24", SettingValueType.WholeNumber, "SLA mặc định (giờ) cho bước ký không đặt SLA riêng; 0 = không nhắc"),
        new(SummaryResignAfterHeadEdit, "true", SettingValueType.YesNo, "Tổ trưởng sửa số lượng duyệt thì trả về người tổng hợp ký lại (Không: luồng đi tiếp, chữ ký người tổng hợp nằm ở phiên bản trước)"),
        new(CertificateExpiryWarningDays, "30", SettingValueType.WholeNumber, "Cảnh báo chứng thư số của người dùng sắp hết hạn (ngày)"),
        new(AcceptanceJobHour, "1", SettingValueType.WholeNumber, "Giờ chạy (giờ Việt Nam) của tác vụ lập nghiệm thu tự động, kiểm tra hằng ngày"),
        new(IntegrityCheckHour, "2", SettingValueType.WholeNumber, "Giờ chạy (giờ Việt Nam) của tác vụ kiểm tra toàn vẹn tệp hằng ngày"),
        new(WorkflowReminderMinutes, "60", SettingValueType.WholeNumber, "Chu kỳ (phút) kiểm tra bước ký quá hạn để gửi nhắc việc"),
    ];

    /// <summary>Allowed range of a numeric setting; null when the setting has none.</summary>
    public static (decimal Min, decimal Max)? RangeOf(string key) => Ranges.TryGetValue(key, out var r) ? r : null;

    /// <summary>Cron of the SLA reminder job: every N minutes, hourly at 60.</summary>
    public static string ReminderCron(int minutes) => minutes >= 60 ? "0 * * * *" : $"*/{minutes} * * * *";

    /// <summary>Cron of a daily job at the given hour (minute 0).</summary>
    public static string DailyCron(int hour) => $"0 {hour} * * *";
}
