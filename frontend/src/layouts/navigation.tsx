import {
  Archive,
  Building2,
  ChartColumn,
  ClipboardList,
  FileCheck2,
  FileText,
  LayoutDashboard,
  Merge,
  Package,
  PenLine,
  Settings,
  ShieldCheck,
  Truck,
  Wallet,
  Warehouse,
  type LucideIcon,
} from 'lucide-react';
import { P } from '../auth/permissions';
import type { NavGroup } from './AppShell';

const icon = (Icon: LucideIcon) => <Icon size={18} strokeWidth={1.75} aria-hidden />;

/** Hospital staff menu, grouped as in design/App Shell.dc.html. */
export const internalNav: NavGroup[] = [
  {
    group: 'Làm việc',
    items: [
      { key: '/tong-quan', label: 'Tổng quan', icon: icon(LayoutDashboard) },
      {
        key: '/trinh-ky',
        label: 'Trình ký',
        icon: icon(PenLine),
        permissions: [P.SigningInbox],
        badge: 'inbox',
      },
    ],
  },
  {
    group: 'Mua sắm',
    items: [
      {
        key: '/quyet-dinh-thau',
        label: 'Quyết định thầu',
        icon: icon(FileCheck2),
        permissions: [P.BidView],
      },
      {
        key: '/danh-muc-hang-hoa',
        label: 'Danh mục hàng hóa',
        icon: icon(Package),
        permissions: [P.ItemView],
      },
      { key: '/hop-dong', label: 'Hợp đồng', icon: icon(FileText), permissions: [P.ContractView] },
      { key: '/de-xuat', label: 'Đề xuất', icon: icon(ClipboardList), permissions: [P.ProposalView] },
      {
        key: '/tong-hop-de-xuat',
        label: 'Tổng hợp đề xuất',
        icon: icon(Merge),
        permissions: [P.ProposalAggregate, P.ProposalApprove],
      },
      {
        key: '/goi-hang',
        label: 'Gọi hàng',
        icon: icon(Truck),
        permissions: [P.CallOrderView],
        badge: 'callOrders',
      },
    ],
  },
  {
    group: 'Kho & Tài chính',
    items: [
      {
        key: '/nhap-kho',
        label: 'Phiếu nhập kho',
        icon: icon(Warehouse),
        permissions: [P.ReceiptView],
        badge: 'receipts',
      },
      { key: '/thanh-toan', label: 'Thanh toán', icon: icon(Wallet), permissions: [P.PaymentView] },
    ],
  },
  {
    group: 'Tra cứu',
    items: [
      {
        key: '/ho-so-luu-tru',
        label: 'Hồ sơ lưu trữ',
        icon: icon(Archive),
        permissions: [P.ArchiveView],
      },
      { key: '/bao-cao', label: 'Báo cáo', icon: icon(ChartColumn), permissions: [P.ReportView] },
      { key: '/xac-minh-chu-ky', label: 'Xác minh chữ ký', icon: icon(ShieldCheck) },
    ],
  },
  {
    group: 'Quản trị',
    items: [
      {
        key: '/quan-tri',
        label: 'Quản trị',
        icon: icon(Settings),
        children: [
          { key: '/quan-tri/benh-vien', label: 'Thông tin bệnh viện', permissions: [P.OrganizationManage] },
          { key: '/quan-tri/co-so', label: 'Cơ sở', permissions: [P.FacilityManage] },
          { key: '/quan-tri/khoa-phong', label: 'Khoa/phòng', permissions: [P.DepartmentManage] },
          { key: '/quan-tri/kho', label: 'Kho', permissions: [P.WarehouseManage] },
          { key: '/quan-tri/nha-cung-cap', label: 'Nhà cung cấp', permissions: [P.SupplierManage] },
          { key: '/quan-tri/nguoi-dung', label: 'Người dùng', permissions: [P.UserManage] },
          { key: '/quan-tri/vai-tro', label: 'Vai trò & phân quyền', permissions: [P.RoleManage] },
          {
            key: '/quan-tri/uy-quyen',
            label: 'Ủy quyền ký',
            permissions: [P.DelegationManage, P.UserManage],
          },
          { key: '/quan-tri/mau-bieu', label: 'Mẫu biểu', permissions: [P.TemplateManage] },
          { key: '/quan-tri/luong-ky', label: 'Cấu hình luồng ký', permissions: [P.WorkflowManage] },
          { key: '/quan-tri/tich-hop-his', label: 'Tích hợp HIS', permissions: [P.IntegrationManage] },
          { key: '/quan-tri/tham-so', label: 'Tham số hệ thống', permissions: [P.SettingManage] },
          {
            key: '/quan-tri/toan-ven',
            label: 'Kiểm tra toàn vẹn',
            permissions: [P.SettingManage, P.AuditView],
          },
          { key: '/quan-tri/nhat-ky', label: 'Nhật ký hoạt động', permissions: [P.AuditView] },
        ],
      },
    ],
  },
];

/** Supplier portal menu. */
export const supplierNav: NavGroup[] = [
  {
    group: 'Làm việc',
    items: [
      { key: '/ncc/tong-quan', label: 'Tổng quan', icon: icon(LayoutDashboard) },
      {
        key: '/ncc/trinh-ky',
        label: 'Trình ký',
        icon: icon(PenLine),
        permissions: [P.SigningInbox],
        badge: 'inbox',
      },
    ],
  },
  {
    group: 'Giao dịch',
    items: [
      { key: '/ncc/hop-dong', label: 'Hợp đồng', icon: icon(FileText), permissions: [P.ContractView] },
      {
        key: '/ncc/goi-hang',
        label: 'Gọi hàng',
        icon: icon(Truck),
        permissions: [P.CallOrderView],
        badge: 'callOrders',
      },
      {
        key: '/ncc/nhap-kho',
        label: 'Phiếu nhập kho',
        icon: icon(Warehouse),
        permissions: [P.ReceiptView],
        badge: 'receipts',
      },
      { key: '/ncc/thanh-toan', label: 'Thanh toán', icon: icon(Wallet), permissions: [P.PaymentView] },
    ],
  },
  {
    group: 'Khác',
    items: [
      { key: '/ncc/xac-minh-chu-ky', label: 'Xác minh chữ ký', icon: icon(ShieldCheck) },
      {
        key: '/ncc/cong-ty',
        label: 'Tài khoản công ty',
        icon: icon(Building2),
        permissions: [P.SupplierCompanyView],
      },
    ],
  },
];
