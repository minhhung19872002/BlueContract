# Áp design BlueProcure vào app React + Ant Design 5 — báo cáo

Nguồn thiết kế: project claude.ai/design `b9957c53-389d-434b-a5e3-b9c3932df594` (AntD Theme Guide, Screen map, `*.dc.html`).
Bản sao trong repo: `design/Design System.dc.html`, `design/_tokens.css`, `design/App Shell.dc.html`.

Mỗi giai đoạn đều chạy `npm run lint`, `npx tsc -b`, `npx vitest run` và E2E (`e2e/`, 4 kịch bản) trước khi commit; E2E 4/4 pass ở mọi commit.

## 1. Tệp đã sửa theo giai đoạn

| Giai đoạn | Commit | Nội dung | Tệp chính |
| --- | --- | --- | --- |
| 1. Nền tảng | `9d0e88d` | Theme sáng/tối (ConfigProvider), token `--bp-*`, StatusTag theo tone | `theme.ts`, `styles.css`, `hooks/useThemeMode.tsx`, `components/StatusTag.tsx` (+ test), `App.tsx` |
| 2. App Shell | `4b8e880` | Sider nhóm menu + badge, header (breadcrumb, Ctrl+K, môi trường, chuông, giao diện, người dùng), drawer mobile, trang Thông báo | `layouts/AppShell.tsx`, `layouts/navigation.tsx`, `layouts/shell.css`, `components/CommandPalette.tsx`, `NotificationBell.tsx`, `notificationStyle.tsx`, `pages/NotificationsPage.tsx`, `router.tsx` |
| 3. Mẫu dùng chung | `45ba825` | DataTable (tabs, bộ lọc, chip, thao tác hàng loạt, mật độ), PageHeader, KPI, cột luồng ký, modal ký số, thanh công cụ PDF, DocHeader/DetailGrid/InfoGrid/FormSection | `components/DataTable.tsx`, `dataTable.css`, `patterns.tsx`, `layout.tsx`, `layout.css`, `PdfViewer.tsx`, `pdfViewer.css`, `features/signing/WorkflowPanel.tsx`, `SignButton.tsx`, `ReturnDialog.tsx`, `signModal.css`, `workflowPanel.css`; E2E helper `confirmSign` |
| 4a. Đăng nhập | `c656cbf` | Khung đăng nhập 2 cột, chọn cổng, OTP 6 ô; đổi mật khẩu với checklist + độ mạnh | `pages/auth/LoginPage.tsx`, `ChangePasswordPage.tsx`, `auth.css`, `utils/validation.ts` (+ test) |
| 4b. Tổng quan | `b8ff0a9` | KPI có tone, hàng đợi theo loại + "Xử lý", biểu đồ chồng cột có trục, bảng HĐ sắp hết hạn / mặt hàng sắp hết | `pages/HomePage.tsx`, `home.css`, `components/BarChart.tsx`, `barChart.css`, `utils/chart.ts` (+ test) |
| 4c. Trình ký | `818936b` | Tabs có đếm, chip lọc, hạn xử lý, ký hàng loạt; chế độ "Xem và xử lý tài liệu"; chọn nhà cung cấp chữ ký | `features/signing/SigningInboxPage.tsx`, `signingInbox.css`, `providers.tsx`, `utils/due.ts` (+ test) |
| 4d. Xác minh | `9a253c3` | Vùng thả tệp, 3 kết quả (hợp lệ / không hợp lệ / bị sửa sau khi ký), danh sách chữ ký dạng accordion | `features/signing/VerifySignaturePage.tsx`, `verdict.ts` (+ test), `verify.css`, `features/documents/DocumentPage.tsx` |
| 4e. Tài liệu | `f738bb0` | DocHeader + DetailGrid (PDF, phiên bản / cột luồng ký), breadcrumb cho trang ngoài menu, `Panel` | `features/documents/DocumentPage.tsx`, `components/layout.*`, `layouts/AppShell.tsx` |
| 4f. Chi tiết nghiệp vụ | `3e569ac` | QĐ thầu, Hợp đồng (KPI, InfoGrid), Đề xuất, Tổng hợp, Gọi hàng, Phiếu nhập kho, Nghiệm thu, 08a, Thanh lý: DocHeader; Descriptions nhãn-trên | `features/bids/BidDecisionDetailPage.tsx`, `contracts/ContractDetailPage.tsx`, `proposals/*Detail*`, `CallOrdersPage.tsx`, `receipts/ReceiptDetailPage.tsx`, `payments/*Pages.tsx`, `styles.css` |
| 4g. Hồ sơ lưu trữ | `2aeee30` | Danh sách hồ sơ + dòng thời gian theo giai đoạn, thẻ tài liệu (SHA-256 rút gọn, sao chép, tải), drawer xem PDF | `features/archive/ArchivePage.tsx`, `archive.css`; E2E #4 cập nhật selector |
| 4h. Báo cáo | `6b07e1d` | Tab, bộ lọc, 4 KPI mỗi báo cáo, biểu đồ thanh ngang / cột, bảng trong panel có dòng tổng | `features/reports/ReportsPage.tsx`, `reports.css`, `components/HBarChart.tsx` |
| 4i. Vai trò + lỗi | `32430ce` | Ma trận quyền (dính đầu/cột, nhóm thu gọn, cột đang sửa, tìm quyền); trang 404 / 403 / 500 trong khung | `features/admin/RolesPage.tsx`, `roles.css`, `pages/ErrorPages.tsx`, `errorPages.css`, `auth/guards.tsx`, `router.tsx` |
| 4j. Tích hợp HIS | `6b4e591` | Lưới thẻ cấu hình (HIS, API, key, IP, rate limit, kéo định kỳ, giả lập, tài liệu) | `features/admin/IntegrationPage.tsx`, `integration.css` |
| 4k. Luồng ký | `c656c22` | Trình dựng luồng toàn trang: thẻ bước, nhãn trên trường, lên/xóa, xem trước | `features/admin/WorkflowTemplatesPage.tsx`, `workflowBuilder.css` |
| 4l. Tài khoản | `c915cbb` | Hồ sơ + avatar, thẻ 2FA có công tắc, thẻ đổi mật khẩu | `features/account/MyAccountPage.tsx`, `account.css` |
| 4m. Wizard HĐ | `3367f9b` | FormSteps, thanh hành động dính đáy có tổng tiền | `components/layout.*`, `features/contracts/ContractWizardPage.tsx` |
| 4n. Trang lập phiếu | `ff20637` | Đề xuất, phiếu trên cổng, tổng hợp, nghiệm thu, 08a, thanh lý: DocHeader + StickyActionBar | `features/proposals/*`, `receipts/ReceiptCreatePage.tsx`, `payments/*Pages.tsx` |
| 4o. Import danh mục | `7acd6c1` | Import 3 bước: Tải tệp → Kiểm tra → Xác nhận | `features/bids/ImportItemsDialog.tsx` |
| 5. Kiểm thử kết thúc | `7c55559` | Header 992–1279px, nhãn dài cắt "…", trạng thái rỗng biểu đồ | `layouts/shell.css`, `features/signing/*`, `components/BarChart.tsx` |

Kiểm thử cuối: không tràn ngang ở 390 / 768 / 1024 / 1440 trên Tổng quan, Trình ký (2 chế độ), QĐ thầu, Danh mục, Hợp đồng (danh sách, chi tiết, wizard), Đề xuất, Gọi hàng, Phiếu nhập kho, Thanh toán, Nghiệm thu, Hồ sơ, Báo cáo, Xác minh, Tài khoản, Thông báo, Tài liệu và toàn bộ cổng NCC; xem cả giao diện sáng và tối.

## 2. Phần chưa áp được và lý do

| Thiết kế | Lý do |
| --- | --- |
| Tài khoản → "Chứng thư số của tôi" (bảng chứng thư, "Đăng ký chứng thư") | Hệ thống chưa có danh mục chứng thư theo người dùng: chứng thư được đọc lúc ký (USB token / ký từ xa). Cần thêm API + bảng trước khi dựng giao diện. |
| Trình ký → số bước dạng "6/8" và số tài liệu trong danh sách | `InboxItemDto` không có thứ tự bước / tổng số bước và số văn bản; hiển thị tên bước và loại tài liệu thay thế. |
| Trình ký → đếm cho các tab "Tôi đã xử lý / Tôi đã gửi / Bị trả lại" | API chỉ trả số đếm "Chờ tôi xử lý"; tab đang mở hiện tổng từ trang kết quả. |
| Hồ sơ lưu trữ → huy hiệu "Toàn vẹn ✓ · kiểm tra lúc …" ở đầu hồ sơ | Kết quả kiểm tra toàn vẹn hằng ngày nằm ở trang Quản trị → Toàn vẹn, chưa có theo từng hợp đồng; thay bằng "Hồ sơ đầy đủ". |
| Tích hợp HIS → phiên bản API HIS, độ trễ kết nối, số request đã dùng trong ngày, lần chạy kéo phiếu gần nhất / tiếp theo, nút "Tạo lại key" / "Thêm IP" | Cấu hình tích hợp lấy từ biến môi trường (không sửa được trên giao diện) và API cấu hình không trả các số đo này. |
| Báo cáo → bộ lọc "Thời gian" cho Thực hiện HĐ / Công nợ | API báo cáo chưa nhận khoảng ngày cho hai báo cáo này. |
| Luồng ký builder → kéo thả bằng chuột | Đã có (dnd-kit) — thêm nút "lên trên" như thiết kế; không có nút "xuống" vì thiết kế không có. |
| Trang 500 → "Mã tham chiếu: req …" | Lỗi hiển thị là lỗi phía trình duyệt; mã tham chiếu dùng thời điểm + thông điệp lỗi thay cho request id của máy chủ. |

## 3. Token và CSS ghi đè ngoài ConfigProvider

Token màu, bo góc, font, kích thước control đặt trong `theme.ts` (`lightTheme`, `darkTheme`). Các phần sau phải viết CSS vì AntD không có token tương ứng:

- `styles.css` `:root` / `[data-theme='dark']`: bộ biến `--bp-*` (text, caption, border, card, hover, th, sider, input-bd, shadow và các họ màu `primary / success / warning / orange / danger / info / purple / indigo / teal / neutral` × `fg / bg / bd / solid`) — dùng cho mọi thành phần tự dựng và cho chế độ tối.
- `styles.css`: số liệu `tabular-nums` cho `.ant-table`, `.ant-statistic`; trọng lượng chữ `.ant-btn`, `.ant-tag`, `.ant-segmented-item-label`; tiêu đề bảng `.ant-table-thead > tr > th` (12/600, nền `--bp-th`); dòng bảng `.data-table`; sider tối (`.app-sider .ant-menu-dark …` mục chọn + vạch trái, nhóm, badge); viền focus `:focus-visible` cho `.ant-btn`, `.ant-tabs-tab-btn`; luồng ký `.sign-flow .ant-steps-*`; Descriptions không viền hiển thị nhãn ở trên, bỏ dấu ":" (`.ant-descriptions:not(.ant-descriptions-bordered) …`).
- `layouts/shell.css`: kích thước header, ô tìm kiếm, nút người dùng; thu gọn header 992–1279px.
- `components/layout.css`: `.doc-header__actions .ant-space-compact-block { width: auto }` (Dropdown.Button mặc định chiếm cả dòng).
- `pages/auth/auth.css`: Segmented chọn cổng, ô OTP dạng lưới 6 cột, prefix icon của Input.
- `features/archive/archive.css`, `features/signing/verify.css`: viền nét đứt và padding của `Upload.Dragger` (`!important` cho padding vì AntD đặt inline).
- Các thành phần tự dựng theo thiết kế (không dùng component AntD): biểu đồ (`barChart.css`), KPI (`dataTable.css`), panel / banner / InfoGrid / FormSteps / StickyActionBar (`layout.css`), cột luồng ký, modal ký số, PDF viewer, hồ sơ lưu trữ, ma trận vai trò, thẻ tích hợp HIS, trình dựng luồng ký, trang lỗi, tài khoản.
