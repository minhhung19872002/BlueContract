namespace BlueContract.Application.Common;

/// <summary>
/// Vietnamese words for what the audit trail stores (spec §9.3 "không lộ chữ kỹ thuật"): actions and the kinds of
/// records. Every action the code writes and every audited entity must be listed (checked by AuditVocabularyTests).
/// </summary>
public static class AuditVocabulary
{
    public static readonly IReadOnlyDictionary<string, string> Actions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Generic changes (persistence interceptor)
        ["Create"] = "Tạo mới",
        ["Update"] = "Cập nhật",
        ["Delete"] = "Xóa",

        // Sign-in and account
        ["Login"] = "Đăng nhập",
        ["LoginFailed"] = "Đăng nhập không thành công",
        ["Logout"] = "Đăng xuất",
        ["LockedOut"] = "Khóa tài khoản do nhập sai mật khẩu",
        ["PasswordChanged"] = "Đổi mật khẩu",
        ["PasswordReset"] = "Đặt lại mật khẩu",
        ["TwoFactorEnabled"] = "Bật xác thực 2 lớp",
        ["TwoFactorDisabled"] = "Tắt xác thực 2 lớp",
        ["RefreshTokenReuse"] = "Phát hiện dùng lại phiên đăng nhập",
        ["CertificateRegister"] = "Đăng ký chứng thư số",
        ["CertificateRemove"] = "Hủy đăng ký chứng thư số",
        ["SignatureImageSet"] = "Cập nhật ảnh chữ ký",
        ["SignatureImageRemove"] = "Xóa ảnh chữ ký",

        // Contracts and documents
        ["ContractCreate"] = "Tạo hợp đồng",
        ["ContractCancel"] = "Hủy hợp đồng",
        ["ContractRevisions"] = "Xử lý thay đổi của nhà cung cấp",
        ["ContractEffective"] = "Hợp đồng có hiệu lực",
        ["AppendixApplied"] = "Áp dụng phụ lục",
        ["OpenEditor"] = "Mở trình soạn thảo",
        ["EditorSave"] = "Lưu phiên bản từ trình soạn thảo",
        ["View"] = "Xem tài liệu",
        ["Download"] = "Tải tài liệu",

        // Signing workflow
        ["WorkflowStart"] = "Gửi ký / rà soát",
        ["WorkflowReturn"] = "Trả lại",
        ["WorkflowReset"] = "Bắt đầu lại luồng ký",
        ["WorkflowContinued"] = "Luồng ký tiếp tục",
        ["WorkflowCompleted"] = "Ký xong",
        ["WorkflowCancel"] = "Hủy luồng ký",
        ["Sign"] = "Ký số",

        // Proposals, call orders, receipts
        ["CallOrderCreate"] = "Tạo đơn gọi hàng",
        ["CallOrderConfirm"] = "Nhà cung cấp xác nhận gọi hàng",
        ["CallOrderCancel"] = "Hủy đơn gọi hàng",
        ["ReceiptCreate"] = "Lập phiếu nhập kho",
        ["ReceiptCancel"] = "Hủy phiếu nhập kho",
        ["ReceiptInvoice"] = "Cập nhật hóa đơn",
        ["ReceiptInvoiceDownload"] = "Tải hóa đơn",
        ["ReceiptVirtualConfirm"] = "Xác nhận phiếu xác nhận khối lượng",
        ["HisReceiptReceived"] = "Nhận phiếu nhập kho từ HIS",
        ["HisReceiptUpdated"] = "HIS cập nhật phiếu nhập kho",
        ["HisSync"] = "Đồng bộ danh mục từ HIS",
        ["IntegrationRetry"] = "Gửi lại yêu cầu tích hợp",
        ["ItemImport"] = "Nhập danh mục hàng hóa từ Excel",

        // Payments
        ["AcceptanceCreate"] = "Lập biên bản nghiệm thu",
        ["AcceptanceCancel"] = "Hủy biên bản nghiệm thu",
        ["ValuationCreate"] = "Lập biên bản xác định giá trị (08a)",
        ["ValuationCancel"] = "Hủy biên bản xác định giá trị",
        ["PaymentOrderAdd"] = "Thêm ủy nhiệm chi",
        ["PaymentOrderRemove"] = "Xóa ủy nhiệm chi",
        ["LiquidationCreate"] = "Lập biên bản thanh lý",
        ["LiquidationCancel"] = "Hủy biên bản thanh lý",
        ["LiquidationSettle"] = "Chốt khoản chưa thanh toán",

        // Archive
        ["ArchiveView"] = "Xem hồ sơ lưu trữ",
        ["ArchiveDownload"] = "Tải hồ sơ lưu trữ",
        ["ArchiveFileDownload"] = "Tải tệp trong hồ sơ",
        ["ArchiveComplete"] = "Xác nhận hồ sơ đầy đủ",
        ["ArchiveReopen"] = "Mở lại hồ sơ",

        // Administration
        ["SettingUpdate"] = "Sửa tham số hệ thống",
        ["EmailTemplateUpdate"] = "Sửa mẫu email",
        ["EmailTemplateReset"] = "Khôi phục mẫu email mặc định",
        ["SystemConfigUpdate"] = "Sửa cấu hình email / ký số",
        ["SystemConfigReset"] = "Dùng lại cấu hình máy chủ",
        ["SystemConfigTestMail"] = "Gửi thư thử",
        ["OrganizationCertificateInstall"] = "Cài chứng thư số tổ chức",
        ["OrganizationCertificateRemove"] = "Gỡ chứng thư số tổ chức",
        ["SealImageSet"] = "Cập nhật ảnh con dấu",
        ["SealImageRemove"] = "Xóa ảnh con dấu",
        ["IntegrityCheck"] = "Kiểm tra toàn vẹn tệp",
    };

    public static readonly IReadOnlyDictionary<string, string> Subjects = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AcceptanceRecord"] = "Biên bản nghiệm thu",
        ["BidDecision"] = "Quyết định thầu",
        ["CallOrder"] = "Đơn gọi hàng",
        ["ConfigOverride"] = "Cấu hình hệ thống",
        ["Contract"] = "Hợp đồng",
        ["ContractAppendix"] = "Phụ lục hợp đồng",
        ["Department"] = "Khoa/phòng",
        ["Document"] = "Tài liệu",
        ["DocumentTemplate"] = "Mẫu biểu",
        ["EmailTemplate"] = "Mẫu email",
        ["Facility"] = "Cơ sở",
        ["IntegrationLog"] = "Nhật ký tích hợp",
        ["IntegrityCheckRun"] = "Lần kiểm tra toàn vẹn",
        ["Item"] = "Hàng hóa",
        ["ItemCategory"] = "Hạng mục hàng hóa",
        ["LiquidationRecord"] = "Biên bản thanh lý",
        ["Organization"] = "Thông tin bệnh viện",
        ["Proposal"] = "Phiếu đề xuất",
        ["ProposalSummary"] = "Phiếu tổng hợp đề xuất",
        ["Role"] = "Vai trò",
        ["SigningDelegation"] = "Ủy quyền ký",
        ["Supplier"] = "Nhà cung cấp",
        ["SystemSetting"] = "Tham số hệ thống",
        ["User"] = "Người dùng",
        ["ValuationRecord"] = "Biên bản xác định giá trị",
        ["Warehouse"] = "Kho",
        ["WarehouseReceipt"] = "Phiếu nhập kho",
        ["WorkflowInstance"] = "Luồng ký",
        ["WorkflowTemplate"] = "Cấu hình luồng ký",
        ["Email"] = "Cấu hình email",
        ["Signing"] = "Cấu hình ký số",
    };

    /// <summary>Vietnamese action; an unknown code reads as a generic change, never as the code itself.</summary>
    public static string Action(string action) => Actions.GetValueOrDefault(action, "Thao tác khác");

    public static string Subject(string? entityType) =>
        entityType is null ? string.Empty : Subjects.GetValueOrDefault(entityType, "Dữ liệu khác");
}
