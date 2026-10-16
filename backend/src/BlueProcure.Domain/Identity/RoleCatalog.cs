using P = BlueProcure.Domain.Identity.Permissions;

namespace BlueProcure.Domain.Identity;

public sealed record RoleDefinition(string Code, string Name, string Description, IReadOnlyList<string> Permissions);

/// <summary>Default roles seeded on first start (spec §4.1). Permissions stay editable from the admin matrix.</summary>
public static class RoleCatalog
{
    public const string SysAdmin = "SYS_ADMIN";
    public const string Procurement = "PROCUREMENT";
    public const string Reviewer = "REVIEWER";
    public const string DeptUser = "DEPT_USER";
    public const string Aggregator = "AGGREGATOR";
    public const string DomainHead = "DOMAIN_HEAD";
    public const string Accountant = "ACCOUNTANT";
    public const string ChiefAccountant = "CHIEF_ACCOUNTANT";
    public const string WarehouseAccountant = "WAREHOUSE_ACCOUNTANT";
    public const string Storekeeper = "STOREKEEPER";
    public const string DeptHead = "DEPT_HEAD";
    public const string Director = "DIRECTOR";
    public const string Clerk = "CLERK";
    public const string SupplierAdmin = "SUPPLIER_ADMIN";
    public const string SupplierUser = "SUPPLIER_USER";
    public const string Viewer = "VIEWER";

    private static readonly string[] InternalBase = [P.MasterDataView, P.SigningInbox, P.SigningSign];

    public static IReadOnlyList<RoleDefinition> All { get; } =
    [
        new(SysAdmin, "Quản trị hệ thống", "Cấu hình hệ thống, người dùng, phân quyền, luồng ký",
            [.. InternalBase, P.OrganizationManage, P.FacilityManage, P.DepartmentManage, P.WarehouseManage, P.UserManage,
             P.RoleManage, P.SupplierManage, P.DelegationManage, P.SettingManage, P.WorkflowManage, P.TemplateManage,
             P.IntegrationManage, P.AuditView, P.BidView, P.ItemView, P.ContractView, P.ProposalView, P.CallOrderView,
             P.ReceiptView, P.PaymentView, P.ArchiveView, P.ReportView]),
        new(Procurement, "Tổ Mua sắm", "Tạo quyết định thầu, hợp đồng, biên bản nghiệm thu / xác định giá trị / thanh lý",
            [.. InternalBase, P.SupplierManage, P.BidView, P.BidManage, P.ItemView, P.ItemManage, P.ContractView,
             P.ContractCreate, P.ProposalView, P.CallOrderView, P.ReceiptView, P.ReceiptManage, P.PaymentView,
             P.PaymentManage, P.ArchiveView, P.ArchiveDownload, P.ReportView]),
        new(Reviewer, "Tổ Rà soát", "Rà soát hợp đồng, biên bản trước khi gửi ký",
            [.. InternalBase, P.BidView, P.ItemView, P.ContractView, P.ContractReview, P.ReceiptView, P.PaymentView,
             P.ArchiveView]),
        new(DeptUser, "Khoa/Phòng", "Tạo đề xuất gọi hàng",
            [.. InternalBase, P.ItemView, P.ContractView, P.ProposalView, P.ProposalCreate]),
        new(Aggregator, "Người tổng hợp", "Tổng hợp đề xuất, gọi hàng",
            [.. InternalBase, P.ItemView, P.ContractView, P.ProposalView, P.ProposalCreate, P.ProposalAggregate,
             P.CallOrderView, P.CallOrderCreate, P.ReceiptView]),
        new(DomainHead, "Tổ trưởng mảng", "Duyệt tổng hợp đề xuất, được sửa số lượng duyệt",
            [.. InternalBase, P.ItemView, P.ContractView, P.ProposalView, P.ProposalApprove, P.CallOrderView]),
        new(Accountant, "Kế toán", "Xem thay đổi hợp đồng do NCC sửa, ký nháy hợp đồng, tham gia thanh toán",
            [.. InternalBase, P.BidView, P.ItemView, P.ContractView, P.ContractAccountantReview, P.ContractInitial,
             P.ReceiptView, P.PaymentView, P.PaymentManage, P.ArchiveView, P.ReportView]),
        new(ChiefAccountant, "Kế toán trưởng", "Ký phiếu nhập kho, hoàn trả",
            [.. InternalBase, P.ContractView, P.ReceiptView, P.PaymentView, P.ArchiveView, P.ReportView]),
        new(WarehouseAccountant, "Kế toán kho", "Ký phiếu nhập kho, hoàn trả",
            [.. InternalBase, P.ContractView, P.ReceiptView]),
        new(Storekeeper, "Thủ kho", "Ký nhận hàng tại kho",
            [.. InternalBase, P.ContractView, P.CallOrderView, P.ReceiptView]),
        new(DeptHead, "Trưởng khoa", "Ký phiếu nhập kho, hoàn trả",
            [.. InternalBase, P.ContractView, P.ProposalView, P.ReceiptView]),
        new(Director, "Giám đốc", "Ký hợp đồng, tổng hợp đề xuất, nghiệm thu, xác định giá trị, thanh lý",
            [.. InternalBase, P.BidView, P.ItemView, P.ContractView, P.ContractSign, P.ProposalView, P.CallOrderView,
             P.ReceiptView, P.PaymentView, P.ArchiveView, P.ArchiveDownload, P.ReportView, P.DelegationManage]),
        new(Clerk, "Văn thư", "Đóng dấu điện tử của Bệnh viện",
            [.. InternalBase, P.ContractView, P.ContractSeal, P.ArchiveView]),
        new(SupplierAdmin, "Quản trị NCC", "Quản lý tài khoản nhân viên của NCC",
            [P.SigningInbox, P.SigningSign, P.ContractView, P.ContractSupplierReview, P.CallOrderView, P.CallOrderConfirm,
             P.ReceiptView, P.ReceiptInvoice, P.PaymentView, P.SupplierCompanyView, P.SupplierStaffManage]),
        new(SupplierUser, "Người dùng NCC", "Rà soát/sửa hợp đồng, xác nhận gọi hàng, ký hồ sơ, là người giao hàng",
            [P.SigningInbox, P.SigningSign, P.ContractView, P.ContractSupplierReview, P.CallOrderView, P.CallOrderConfirm,
             P.ReceiptView, P.ReceiptInvoice, P.PaymentView, P.SupplierCompanyView]),
        new(Viewer, "Chỉ xem", "Xem hồ sơ lưu trữ, báo cáo",
            [P.MasterDataView, P.ContractView, P.ArchiveView, P.ReportView]),
    ];
}
