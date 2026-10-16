import { createBrowserRouter } from 'react-router-dom';
import { NotificationsPage } from './pages/NotificationsPage';
import { AppShell } from './layouts/AppShell';
import { internalNav, supplierNav } from './layouts/navigation';
import { HomePage, SupplierHomePage } from './pages/HomePage';
import { NotFoundPage, RouteErrorPage } from './pages/ErrorPages';
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
import { EmailTemplatesPage } from './features/admin/EmailTemplatesPage';
import { AuditLogsPage } from './features/admin/AuditLogsPage';
import { MyAccountPage } from './features/account/MyAccountPage';
import { CompanyPage } from './features/supplier/CompanyPage';
import { TemplatesPage } from './features/templates/TemplatesPage';
import { DocumentPage } from './features/documents/DocumentPage';
import { SigningInboxPage } from './features/signing/SigningInboxPage';
import { VerifySignaturePage } from './features/signing/VerifySignaturePage';
import { ReviewPage } from './features/signing/ReviewPage';
import { WorkflowTemplatesPage } from './features/admin/WorkflowTemplatesPage';
import { ItemsPage } from './features/catalog/ItemsPage';
import { BidDecisionsPage } from './features/bids/BidDecisionsPage';
import { BidDecisionDetailPage } from './features/bids/BidDecisionDetailPage';
import { ContractsPage } from './features/contracts/ContractsPage';
import { ContractWizardPage } from './features/contracts/ContractWizardPage';
import { ContractDetailPage } from './features/contracts/ContractDetailPage';
import { ProposalsPage } from './features/proposals/ProposalsPage';
import { ProposalEditorPage } from './features/proposals/ProposalEditorPage';
import { ProposalDetailPage } from './features/proposals/ProposalDetailPage';
import { SummariesPage, SummaryCreatePage } from './features/proposals/SummariesPage';
import { SummaryDetailPage } from './features/proposals/SummaryDetailPage';
import { CallCreatePage, CallOrderDetailPage, CallOrdersPage } from './features/proposals/CallOrdersPage';
import { ReceiptsPage } from './features/receipts/ReceiptsPage';
import { ReceiptDetailPage } from './features/receipts/ReceiptDetailPage';
import { ReceiptCreatePage } from './features/receipts/ReceiptCreatePage';
import { IntegrationPage } from './features/admin/IntegrationPage';
import { PaymentsPage } from './features/payments/PaymentsPage';
import { ArchivePage } from './features/archive/ArchivePage';
import { IntegrityPage } from './features/admin/IntegrityPage';
import { ReportsPage } from './features/reports/ReportsPage';
import { AcceptanceDetailPage, AcceptanceEditorPage } from './features/payments/AcceptancePages';
import { ValuationCreatePage, ValuationDetailPage } from './features/payments/ValuationPages';
import { LiquidationCreatePage, LiquidationDetailPage } from './features/payments/LiquidationPages';

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
          {
            errorElement: <RouteErrorPage />,
            children: [
              { path: '/tong-quan', element: <HomePage /> },
              { path: '/tai-khoan', element: <MyAccountPage /> },
              { path: '/thong-bao', element: <NotificationsPage /> },
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
              { path: '/quan-tri/mau-email', element: guard([P.SettingManage], <EmailTemplatesPage />) },
              { path: '/quan-tri/nhat-ky', element: guard([P.AuditView], <AuditLogsPage />) },
              { path: '/quan-tri/mau-bieu', element: guard([P.TemplateManage], <TemplatesPage />) },
              { path: '/tai-lieu/:id', element: <DocumentPage /> },
              { path: '/trinh-ky', element: guard([P.SigningInbox], <SigningInboxPage />) },
              {
                path: '/ra-soat',
                element: guard([P.ContractReview, P.ContractAccountantReview], <ReviewPage />),
              },
              { path: '/quyet-dinh-thau', element: guard([P.BidView], <BidDecisionsPage />) },
              { path: '/quyet-dinh-thau/:id', element: guard([P.BidView], <BidDecisionDetailPage />) },
              { path: '/danh-muc-hang-hoa', element: guard([P.ItemView], <ItemsPage />) },
              { path: '/hop-dong', element: guard([P.ContractView], <ContractsPage />) },
              { path: '/hop-dong/tao-moi', element: guard([P.ContractCreate], <ContractWizardPage />) },
              { path: '/hop-dong/:id', element: guard([P.ContractView], <ContractDetailPage />) },
              { path: '/hop-dong/:id/sua', element: guard([P.ContractCreate], <ContractWizardPage />) },
              { path: '/de-xuat', element: guard([P.ProposalView], <ProposalsPage />) },
              { path: '/de-xuat/tao-moi', element: guard([P.ProposalCreate], <ProposalEditorPage />) },
              { path: '/de-xuat/:id', element: guard([P.ProposalView], <ProposalDetailPage />) },
              { path: '/de-xuat/:id/sua', element: guard([P.ProposalCreate], <ProposalEditorPage />) },
              {
                path: '/tong-hop-de-xuat',
                element: guard([P.ProposalView, P.CallOrderView], <SummariesPage />),
              },
              {
                path: '/tong-hop-de-xuat/tao-moi',
                element: guard([P.ProposalAggregate], <SummaryCreatePage />),
              },
              {
                path: '/tong-hop-de-xuat/:id',
                element: guard([P.ProposalView, P.CallOrderView], <SummaryDetailPage />),
              },
              { path: '/goi-hang', element: guard([P.CallOrderView], <CallOrdersPage />) },
              { path: '/goi-hang/tao-moi', element: guard([P.CallOrderCreate], <CallCreatePage />) },
              { path: '/goi-hang/:id', element: guard([P.CallOrderView], <CallOrderDetailPage />) },
              { path: '/nhap-kho', element: guard([P.ReceiptView], <ReceiptsPage />) },
              { path: '/nhap-kho/tao-moi', element: guard([P.ReceiptManage], <ReceiptCreatePage />) },
              { path: '/nhap-kho/:id', element: guard([P.ReceiptView], <ReceiptDetailPage />) },
              { path: '/nhap-kho/:id/sua', element: guard([P.ReceiptManage], <ReceiptCreatePage />) },
              { path: '/quan-tri/tich-hop-his', element: guard([P.IntegrationManage], <IntegrationPage />) },
              { path: '/thanh-toan', element: guard([P.PaymentView], <PaymentsPage />) },
              { path: '/ho-so-luu-tru', element: guard([P.ArchiveView], <ArchivePage />) },
              { path: '/bao-cao', element: guard([P.ReportView], <ReportsPage />) },
              {
                path: '/quan-tri/toan-ven',
                element: guard([P.SettingManage, P.AuditView], <IntegrityPage />),
              },
              {
                path: '/thanh-toan/nghiem-thu/tao-moi',
                element: guard([P.PaymentManage], <AcceptanceEditorPage />),
              },
              {
                path: '/thanh-toan/nghiem-thu/:id',
                element: guard([P.PaymentView], <AcceptanceDetailPage />),
              },
              {
                path: '/thanh-toan/nghiem-thu/:id/sua',
                element: guard([P.PaymentManage], <AcceptanceEditorPage />),
              },
              {
                path: '/thanh-toan/xac-dinh-gia-tri/tao-moi',
                element: guard([P.PaymentManage], <ValuationCreatePage />),
              },
              {
                path: '/thanh-toan/xac-dinh-gia-tri/:id',
                element: guard([P.PaymentView], <ValuationDetailPage />),
              },
              {
                path: '/thanh-toan/thanh-ly/tao-moi',
                element: guard([P.PaymentManage], <LiquidationCreatePage />),
              },
              {
                path: '/thanh-toan/thanh-ly/:id',
                element: guard([P.PaymentView], <LiquidationDetailPage />),
              },
              { path: '/xac-minh-chu-ky', element: <VerifySignaturePage /> },
              { path: '/quan-tri/luong-ky', element: guard([P.WorkflowManage], <WorkflowTemplatesPage />) },
              { path: '*', element: <NotFoundPage /> },
            ],
          },
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
          {
            errorElement: <RouteErrorPage />,
            children: [
              { index: true, element: <SupplierHomePage /> },
              { path: 'tong-quan', element: <SupplierHomePage /> },
              { path: 'tai-khoan', element: <MyAccountPage /> },
              { path: 'thong-bao', element: <NotificationsPage /> },
              { path: 'cong-ty', element: guard([P.SupplierCompanyView], <CompanyPage />) },
              { path: 'trinh-ky', element: guard([P.SigningInbox], <SigningInboxPage />) },
              { path: 'hop-dong', element: guard([P.ContractView], <ContractsPage />) },
              { path: 'hop-dong/:id', element: guard([P.ContractView], <ContractDetailPage />) },
              { path: 'goi-hang', element: guard([P.CallOrderView], <CallOrdersPage />) },
              { path: 'goi-hang/:id', element: guard([P.CallOrderView], <CallOrderDetailPage />) },
              { path: 'nhap-kho', element: guard([P.ReceiptView], <ReceiptsPage />) },
              { path: 'nhap-kho/:id', element: guard([P.ReceiptView], <ReceiptDetailPage />) },
              { path: 'thanh-toan', element: guard([P.PaymentView], <PaymentsPage />) },
              {
                path: 'thanh-toan/nghiem-thu/:id',
                element: guard([P.PaymentView], <AcceptanceDetailPage />),
              },
              {
                path: 'thanh-toan/xac-dinh-gia-tri/:id',
                element: guard([P.PaymentView], <ValuationDetailPage />),
              },
              { path: 'thanh-toan/thanh-ly/:id', element: guard([P.PaymentView], <LiquidationDetailPage />) },
              { path: 'xac-minh-chu-ky', element: <VerifySignaturePage /> },
              { path: 'tai-lieu/:id', element: <DocumentPage /> },
              { path: '*', element: <NotFoundPage /> },
            ],
          },
        ],
      },
    ],
  },
  { path: '*', element: <NotFoundPage /> },
]);
