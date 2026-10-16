import {
  ApartmentOutlined,
  AuditOutlined,
  BankOutlined,
  DashboardOutlined,
  FileWordOutlined,
  HomeOutlined,
  IdcardOutlined,
  SettingOutlined,
  ShopOutlined,
  TeamOutlined,
} from '@ant-design/icons';
import { P } from '../auth/permissions';
import type { NavItem } from './AppShell';

/** Hospital staff menu. Business modules are added here as each phase lands. */
export const internalNav: NavItem[] = [
  { key: '/tong-quan', label: 'Tổng quan', icon: <DashboardOutlined /> },
  {
    key: '/quan-tri',
    label: 'Quản trị',
    icon: <SettingOutlined />,
    children: [
      {
        key: '/quan-tri/benh-vien',
        label: 'Thông tin bệnh viện',
        icon: <BankOutlined />,
        permissions: [P.OrganizationManage],
      },
      { key: '/quan-tri/co-so', label: 'Cơ sở', icon: <HomeOutlined />, permissions: [P.FacilityManage] },
      {
        key: '/quan-tri/khoa-phong',
        label: 'Khoa/phòng',
        icon: <ApartmentOutlined />,
        permissions: [P.DepartmentManage],
      },
      { key: '/quan-tri/kho', label: 'Kho', icon: <ShopOutlined />, permissions: [P.WarehouseManage] },
      {
        key: '/quan-tri/nha-cung-cap',
        label: 'Nhà cung cấp',
        icon: <IdcardOutlined />,
        permissions: [P.SupplierManage],
      },
      {
        key: '/quan-tri/nguoi-dung',
        label: 'Người dùng',
        icon: <TeamOutlined />,
        permissions: [P.UserManage],
      },
      { key: '/quan-tri/vai-tro', label: 'Vai trò & phân quyền', permissions: [P.RoleManage] },
      { key: '/quan-tri/uy-quyen', label: 'Ủy quyền ký', permissions: [P.DelegationManage, P.UserManage] },
      {
        key: '/quan-tri/mau-bieu',
        label: 'Mẫu biểu',
        icon: <FileWordOutlined />,
        permissions: [P.TemplateManage],
      },
      { key: '/quan-tri/tham-so', label: 'Tham số hệ thống', permissions: [P.SettingManage] },
      {
        key: '/quan-tri/nhat-ky',
        label: 'Nhật ký hoạt động',
        icon: <AuditOutlined />,
        permissions: [P.AuditView],
      },
    ],
  },
];

/** Supplier portal menu. */
export const supplierNav: NavItem[] = [
  { key: '/ncc/tong-quan', label: 'Tổng quan', icon: <DashboardOutlined /> },
  {
    key: '/ncc/cong-ty',
    label: 'Tài khoản công ty',
    icon: <IdcardOutlined />,
    permissions: [P.SupplierCompanyView],
  },
];
