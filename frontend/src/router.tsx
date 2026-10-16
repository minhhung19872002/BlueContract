import { createBrowserRouter } from 'react-router-dom';
import { AppShell } from './layouts/AppShell';
import { internalNav, supplierNav } from './layouts/navigation';
import { HomePage, SupplierHomePage } from './pages/HomePage';
import { NotFoundPage } from './pages/NotFoundPage';
import { LoginPage } from './pages/auth/LoginPage';
import { ChangePasswordPage } from './pages/auth/ChangePasswordPage';
import { HomeRedirect, PortalGuard, RequireAuth, RequirePermission } from './auth/guards';
import { P } from './auth/permissions';
import { OrganizationPage } from './features/admin/OrganizationPage';
import { FacilitiesPage } from './features/admin/FacilitiesPage';
import { DepartmentsPage } from './features/admin/DepartmentsPage';
import { WarehousesPage } from './features/admin/WarehousesPage';
import { SuppliersPage } from './features/admin/SuppliersPage';
import { UsersPage } from './features/admin/UsersPage';
import { RolesPage } from './features/admin/RolesPage';
import { DelegationsPage } from './features/admin/DelegationsPage';
import { SettingsPage } from './features/admin/SettingsPage';
import { AuditLogsPage } from './features/admin/AuditLogsPage';
import { MyAccountPage } from './features/account/MyAccountPage';
import { CompanyPage } from './features/supplier/CompanyPage';
import { TemplatesPage } from './features/templates/TemplatesPage';
import { DocumentPage } from './features/documents/DocumentPage';

const guard = (permissions: string[], element: React.ReactNode) => (
  <RequirePermission any={permissions}>{element}</RequirePermission>
);

export const router = createBrowserRouter([
  { path: '/dang-nhap', element: <LoginPage /> },
  {
    element: <RequireAuth />,
    children: [
      { path: '/', element: <HomeRedirect /> },
      { path: '/doi-mat-khau', element: <ChangePasswordPage /> },
      {
        element: (
          <PortalGuard portal="internal">
            <AppShell
              brand="BlueProcure"
              subtitle="Cổng Quản lý & Thanh toán Mua sắm Hàng hóa Trực tuyến"
              nav={internalNav}
            />
          </PortalGuard>
        ),
        children: [
          { path: '/tong-quan', element: <HomePage /> },
          { path: '/tai-khoan', element: <MyAccountPage /> },
          { path: '/quan-tri/benh-vien', element: guard([P.OrganizationManage], <OrganizationPage />) },
          { path: '/quan-tri/co-so', element: guard([P.FacilityManage], <FacilitiesPage />) },
          { path: '/quan-tri/khoa-phong', element: guard([P.DepartmentManage], <DepartmentsPage />) },
          { path: '/quan-tri/kho', element: guard([P.WarehouseManage], <WarehousesPage />) },
          { path: '/quan-tri/nha-cung-cap', element: guard([P.SupplierManage], <SuppliersPage />) },
          { path: '/quan-tri/nguoi-dung', element: guard([P.UserManage], <UsersPage />) },
          { path: '/quan-tri/vai-tro', element: guard([P.RoleManage], <RolesPage />) },
          {
            path: '/quan-tri/uy-quyen',
            element: guard([P.DelegationManage, P.UserManage], <DelegationsPage />),
          },
          { path: '/quan-tri/tham-so', element: guard([P.SettingManage], <SettingsPage />) },
          { path: '/quan-tri/nhat-ky', element: guard([P.AuditView], <AuditLogsPage />) },
          { path: '/quan-tri/mau-bieu', element: guard([P.TemplateManage], <TemplatesPage />) },
          { path: '/tai-lieu/:id', element: <DocumentPage /> },
        ],
      },
      {
        path: '/ncc',
        element: (
          <PortalGuard portal="supplier">
            <AppShell brand="BlueProcure" subtitle="Cổng Nhà cung cấp" nav={supplierNav} />
          </PortalGuard>
        ),
        children: [
          { index: true, element: <SupplierHomePage /> },
          { path: 'tong-quan', element: <SupplierHomePage /> },
          { path: 'tai-khoan', element: <MyAccountPage /> },
          { path: 'cong-ty', element: guard([P.SupplierCompanyView], <CompanyPage />) },
        ],
      },
    ],
  },
  { path: '*', element: <NotFoundPage /> },
]);
