using System.Collections.Frozen;

namespace BlueContract.Domain.Identity;

/// <summary>Every permission the system checks. Codes are stable; names are Vietnamese for the permission matrix UI.</summary>
public static class Permissions
{
    // Administration
    public const string OrganizationManage = "admin.organization";
    public const string FacilityManage = "admin.facility";
    public const string DepartmentManage = "admin.department";
    public const string WarehouseManage = "admin.warehouse";
    public const string UserManage = "admin.user";
    public const string RoleManage = "admin.role";
    public const string SupplierManage = "admin.supplier";
    public const string DelegationManage = "admin.delegation";
    public const string SettingManage = "admin.setting";
    public const string WorkflowManage = "admin.workflow";
    public const string TemplateManage = "admin.template";
    public const string IntegrationManage = "admin.integration";
    public const string AuditView = "audit.view";
    public const string MasterDataView = "masterdata.view";

    // Bid decisions & catalog
    public const string BidView = "bid.view";
    public const string BidManage = "bid.manage";
    public const string ItemView = "item.view";
    public const string ItemManage = "item.manage";

    // Contracts
    public const string ContractView = "contract.view";
    public const string ContractCreate = "contract.create";
    public const string ContractReview = "contract.review";
    public const string ContractSupplierReview = "contract.supplier_review";
    public const string ContractAccountantReview = "contract.accountant_review";
    public const string ContractSign = "contract.sign";
    public const string ContractInitial = "contract.initial";
    public const string ContractSeal = "contract.seal";

    // Proposals & call orders
    public const string ProposalView = "proposal.view";
    public const string ProposalCreate = "proposal.create";
    public const string ProposalAggregate = "proposal.aggregate";
    public const string ProposalApprove = "proposal.approve";
    public const string CallOrderView = "callorder.view";
    public const string CallOrderCreate = "callorder.create";
    public const string CallOrderConfirm = "callorder.confirm";

    // Warehouse receipts
    public const string ReceiptView = "receipt.view";
    public const string ReceiptManage = "receipt.manage";
    public const string ReceiptInvoice = "receipt.invoice";

    // Payment
    public const string PaymentView = "payment.view";
    public const string PaymentManage = "payment.manage";

    // Signing inbox (all document types)
    public const string SigningInbox = "signing.inbox";
    public const string SigningSign = "signing.sign";

    // Archive & reports
    public const string ArchiveView = "archive.view";
    public const string ArchiveDownload = "archive.download";
    public const string ReportView = "report.view";

    // Supplier portal
    public const string SupplierCompanyView = "supplier.company.view";
    public const string SupplierStaffManage = "supplier.staff.manage";
}

public sealed record PermissionDefinition(string Code, string Name, string Group);

public static class PermissionCatalog
{
    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Permissions.OrganizationManage, "Cấu hình thông tin bệnh viện", "Quản trị"),
        new(Permissions.FacilityManage, "Quản lý cơ sở", "Quản trị"),
        new(Permissions.DepartmentManage, "Quản lý khoa/phòng", "Quản trị"),
        new(Permissions.WarehouseManage, "Quản lý kho", "Quản trị"),
        new(Permissions.UserManage, "Quản lý người dùng", "Quản trị"),
        new(Permissions.RoleManage, "Quản lý vai trò & phân quyền", "Quản trị"),
        new(Permissions.SupplierManage, "Quản lý nhà cung cấp", "Quản trị"),
        new(Permissions.DelegationManage, "Quản lý ủy quyền ký", "Quản trị"),
        new(Permissions.SettingManage, "Cấu hình tham số hệ thống", "Quản trị"),
        new(Permissions.WorkflowManage, "Cấu hình luồng ký", "Quản trị"),
        new(Permissions.TemplateManage, "Quản lý mẫu biểu", "Quản trị"),
        new(Permissions.IntegrationManage, "Cấu hình tích hợp HIS", "Quản trị"),
        new(Permissions.AuditView, "Xem nhật ký hoạt động", "Quản trị"),
        new(Permissions.MasterDataView, "Xem danh mục dùng chung", "Quản trị"),

        new(Permissions.BidView, "Xem quyết định thầu", "Quyết định thầu"),
        new(Permissions.BidManage, "Tạo/sửa quyết định thầu", "Quyết định thầu"),
        new(Permissions.ItemView, "Xem danh mục hàng hóa", "Quyết định thầu"),
        new(Permissions.ItemManage, "Quản lý danh mục hàng hóa", "Quyết định thầu"),

        new(Permissions.ContractView, "Xem hợp đồng", "Hợp đồng"),
        new(Permissions.ContractCreate, "Soạn thảo hợp đồng, phụ lục", "Hợp đồng"),
        new(Permissions.ContractReview, "Rà soát hợp đồng, biên bản", "Hợp đồng"),
        new(Permissions.ContractSupplierReview, "Nhà cung cấp rà soát/sửa hợp đồng", "Hợp đồng"),
        new(Permissions.ContractAccountantReview, "Kế toán xem xét thay đổi của NCC", "Hợp đồng"),
        new(Permissions.ContractSign, "Ký số hợp đồng", "Hợp đồng"),
        new(Permissions.ContractInitial, "Ký nháy hợp đồng", "Hợp đồng"),
        new(Permissions.ContractSeal, "Đóng dấu điện tử bệnh viện", "Hợp đồng"),

        new(Permissions.ProposalView, "Xem đề xuất", "Đề xuất & gọi hàng"),
        new(Permissions.ProposalCreate, "Tạo đề xuất gọi hàng", "Đề xuất & gọi hàng"),
        new(Permissions.ProposalAggregate, "Tổng hợp đề xuất", "Đề xuất & gọi hàng"),
        new(Permissions.ProposalApprove, "Duyệt tổng hợp đề xuất (sửa SL duyệt)", "Đề xuất & gọi hàng"),
        new(Permissions.CallOrderView, "Xem gọi hàng", "Đề xuất & gọi hàng"),
        new(Permissions.CallOrderCreate, "Gọi hàng", "Đề xuất & gọi hàng"),
        new(Permissions.CallOrderConfirm, "NCC xác nhận gọi hàng", "Đề xuất & gọi hàng"),

        new(Permissions.ReceiptView, "Xem phiếu nhập kho", "Nhập kho"),
        new(Permissions.ReceiptManage, "Tạo phiếu nhập kho trên cổng / phiếu ảo", "Nhập kho"),
        new(Permissions.ReceiptInvoice, "Bổ sung hóa đơn", "Nhập kho"),

        new(Permissions.PaymentView, "Xem hồ sơ thanh toán", "Thanh toán"),
        new(Permissions.PaymentManage, "Lập biên bản nghiệm thu, 08a, thanh lý", "Thanh toán"),

        new(Permissions.SigningInbox, "Truy cập menu Trình ký", "Trình ký"),
        new(Permissions.SigningSign, "Ký/duyệt/trả lại tài liệu được giao", "Trình ký"),

        new(Permissions.ArchiveView, "Xem hồ sơ lưu trữ", "Hồ sơ & báo cáo"),
        new(Permissions.ArchiveDownload, "Tải hồ sơ (ZIP)", "Hồ sơ & báo cáo"),
        new(Permissions.ReportView, "Xem báo cáo", "Hồ sơ & báo cáo"),

        new(Permissions.SupplierCompanyView, "Xem thông tin công ty (NCC)", "Cổng nhà cung cấp"),
        new(Permissions.SupplierStaffManage, "Quản lý tài khoản nhân viên NCC", "Cổng nhà cung cấp"),
    ];

    private static readonly FrozenSet<string> Codes = All.Select(p => p.Code).ToFrozenSet(StringComparer.Ordinal);

    public static bool Exists(string code) => Codes.Contains(code);
}
