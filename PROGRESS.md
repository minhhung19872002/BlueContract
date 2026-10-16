# PROGRESS — BlueProcure

Theo dõi tiến độ theo Phase (spec §14). Đánh dấu `[x]` khi hạng mục đạt, kèm ghi chú kiểm chứng.

## Phase 0 — Khởi tạo ✅ (16/10/2026)
- [x] Solution Clean Architecture: `Domain` / `Application` / `Infrastructure` / `Api` + 3 project test (net10.0, warnings-as-errors)
- [x] Base entity (UUID v7, audit cols, soft delete, xmin concurrency), `SystemSetting` + khóa mặc định, `Result<T>`, `Error`, ánh xạ thông điệp lỗi tiếng Việt
- [x] EF Core + Npgsql + snake_case, migration `InitialCreate`, interceptor đóng dấu thời gian/soft delete
- [x] Khởi tạo lần đầu: migrate + seed idempotent + tạo bucket MinIO; cờ `--seed-only`
- [x] `IFileStorage` → MinIO (SHA-256 mỗi file, presigned URL qua endpoint public)
- [x] Serilog (console/file/Seq tùy chọn), correlation id middleware, ProblemDetails + global exception handler
- [x] OpenAPI + Scalar UI tại `/docs`; health `/health`, `/health/ready` (PostgreSQL, MinIO, Gotenberg)
- [x] Frontend Vite + React 18 + TS strict + AntD 5 (vi_VN) + TanStack Query + React Router 6 + dayjs vi; axios client chuẩn hóa lỗi; định dạng tiền/ngày VN (có test)
- [x] Sinh TS client từ OpenAPI (`openapi-typescript`)
- [x] Docker: Dockerfile multi-stage non-root (api, web/nginx-unprivileged), compose đủ service: postgres, api, web, minio, onlyoffice, gotenberg, mailpit, seq (profile)
- [x] Scripts `.ps1` (tương thích Windows PowerShell 5.1) + `.sh`: dev-up, reset-db, seed, backup
- [x] GitHub Actions: backend (format, build, test + coverage), frontend (prettier, eslint, vitest, build), build image
- [x] Pre-commit hook (`.githooks/pre-commit`), ESLint + Prettier, `dotnet format`
- [x] README, DECISIONS.md

**Kiểm chứng:** `dotnet test` 30/30 pass (14 Domain, 9 Application, 7 Integration với Testcontainers) · `npm run lint/test/build` pass · `scripts\dev-up.ps1` → `http://localhost:47080` mở được, `/health/ready` = Healthy (postgres, minio, gotenberg) · `seed.ps1`, `backup.ps1` chạy được.

## Phase 1 — Nền tảng ✅ (16/10/2026)
- [x] Auth: JWT access 15' (HS256) + refresh token opaque lưu SHA-256 trong DB, xoay vòng mỗi lần dùng, phát hiện dùng lại → thu hồi cả "family"; cookie httpOnly SameSite=Strict Path=/api/auth; PasswordHasher (ASP.NET Identity); khóa 15' sau 5 lần sai; rate limit đăng nhập theo IP; 2FA TOTP (RFC 6238, QR trong UI); bắt buộc đổi mật khẩu tạm
- [x] Security stamp: đổi mật khẩu / vai trò / khóa / vô hiệu hóa → token cũ bị từ chối (cache 15 s)
- [x] User / Role / Permission (48 quyền, 16 vai trò) / UserRole / RolePermission / UserDataScope (Cơ sở, Khoa/phòng, Mảng) / SigningDelegation (KT. GIÁM ĐỐC)
- [x] `[HasPermission(...)]` policy-based authorization (policy provider động)
- [x] Cô lập dữ liệu NCC bằng EF Core named query filter (`SupplierIsolation`) — test: NCC A không đọc/sửa được dữ liệu NCC B kể cả đoán ID
- [x] AuditLog tự động (interceptor, JSONB trước/sau, che mật khẩu) + ghi tay hành động nghiệp vụ (đăng nhập, khóa, đổi mật khẩu...) + trigger append-only ở DB
- [x] CRUD + lịch sử phiên bản: Organization, Facility, Department(+History), Warehouse, Supplier(+History, cấp tài khoản + email), User(+History, đặt lại mật khẩu, mở khóa)
- [x] Layout Nội bộ & Cổng NCC, menu lọc theo quyền, guard theo cổng, axios interceptor refresh single-flight, trang đăng nhập (2 bước khi bật 2FA), đổi mật khẩu, tài khoản & bảo mật
- [x] Ma trận phân quyền Role × Permission (checkbox, lưu theo vai trò, tạo vai trò mới)
- [x] Màn hình: Thông tin bệnh viện, Cơ sở, Khoa/phòng, Kho, NCC, Người dùng, Vai trò, Ủy quyền ký, Tham số hệ thống, Nhật ký hoạt động (lọc + diff trước/sau); Cổng NCC: Tài khoản công ty (SUPPLIER_ADMIN quản lý nhân viên)
- [x] Bảng: phân trang/sort/lọc server-side, bộ lọc lưu trên URL, xuất Excel (ClosedXML)
- [x] Seed demo: 1 bệnh viện, 4 cơ sở, 17 khoa/phòng, 5 kho, tài khoản cho mọi vai trò + 3 NCC × 2 tài khoản, 1 ủy quyền ký; `--reset-demo`
- [x] SystemSetting chỉnh sửa trên UI

**Kiểm chứng:** backend 114/114 test (36 Domain, 23 Application, 55 Integration) · frontend 19/19 test, lint 0 lỗi · `docker compose up` → đăng nhập `admin`, `ncc1` bằng trình duyệt; menu đúng quyền; reload giữ phiên qua refresh cookie.
## Phase 2 — Lõi tài liệu ✅ (16/10/2026)
- [x] `IFileStorage` (S3) + SHA-256 mỗi tệp; tải xuống qua presigned URL ngắn hạn hoặc stream qua API, có ghi nhật ký
- [x] Template engine DOCX tự viết (OpenXml): placeholder, bộ lọc `money/words/date/datetext/datetime/number/percent/upper/lower`, lặp dòng bảng & lặp khối `{{#X}}…{{/X}}`, `{{@index}}`, `{{#if}}`/`{{#if !}}` (khối và inline), anchor chữ ký `{{SIG_*}}` (chữ trắng 1pt, vẫn trích xuất được từ PDF); tự gộp run khi Word tách thẻ
- [x] Đọc số tiền bằng chữ tiếng Việt (39 test, ví dụ spec: "Một tỷ hai trăm linh năm triệu không trăm ba mươi nghìn đồng")
- [x] `IPdfConverter` → Gotenberg (LibreOffice)
- [x] Document / DocumentVersion: số phiên bản, DOCX + PDF + SHA-256, bất biến sau chữ ký đầu, phiên bản mới sau khi đã ký → đánh dấu phải ký lại
- [x] `IDocumentEditorService` → ONLYOFFICE Docs: cấu hình ký JWT, chế độ Sửa / Rà soát (bắt buộc track changes) / Xem; URL tải file & callback có token ký thời hạn; callback xác thực JWT, tạo phiên bản mới, chống phát lại
- [x] Danh mục placeholder hợp lệ theo 10 loại mẫu + dữ liệu mẫu; validator báo placeholder sai/bộ lọc sai/thẻ không cân bằng khi tải lên
- [x] Quản lý mẫu biểu: tải lên (kiểm tra magic bytes + placeholder), phiên bản, kích hoạt lại phiên bản cũ, xem trước PDF với dữ liệu mẫu, tải DOCX
- [x] 16 mẫu chuẩn (spec §10 mục 3–16) sinh từ code, cài đặt tự động khi khởi động; xuất ra `templates/` bằng `--export-templates`
- [x] UI: trang Mẫu biểu, trang Tài liệu (phiên bản, hash, tạo PDF, xem PDF, tải, sửa trực tuyến), `PdfViewer` (pdf.js), `DocEditor` (ONLYOFFICE)
- [ ] Biểu mẫu import Excel (spec §10 mục 1–2) → Phase 4 cùng chức năng import

**Kiểm chứng:** backend 195/195 test (gồm render đủ 16 mẫu, PDF thật qua Gotenberg container, luồng callback ONLYOFFICE có JWT) · trên trình duyệt: xem trước hợp đồng thuốc 4 trang PDF → tạo tài liệu thử → mở ONLYOFFICE, sửa → đóng → phiên bản v2 được lưu.
## Phase 3 — Workflow + Ký số ✅ (16/10/2026)
- [x] Workflow engine: WorkflowTemplate (bước theo vai trò / người cụ thể / NCC của tài liệu / người lập, SLA, bước tùy chọn, danh sách bước được trả về) → WorkflowInstance + StepInstance; state machine idempotent (bấm 2 lần không ký 2 lần), xmin concurrency + unique (instance, sequence) cho sự kiện → race trả 409; trả lại về bất kỳ bước trước (0 = về người lập, tài liệu về bản nháp); trình lại; hủy; phiên bản mới → luồng khởi động lại; 19 unit test chuyển trạng thái
- [x] 10 luồng mặc định (HĐ, phụ lục, đề xuất, tổng hợp, phiếu nhập kho 5 chữ ký, nghiệm thu, 08a, đề nghị TT, thanh lý, WF_SAMPLE để thử); khóa sửa tài liệu khi đang trình ký
- [x] Ký số PAdES (iText 9, incremental update, đặt chữ ký theo neo {{SIG_*}} trong mẫu, font Noto Sans nhúng): MOCK (chứng thư self-signed theo user, PFX mã hóa bằng Data Protection), MOCK_REMOTE (giả lập xác nhận trên điện thoại + poll), USB_TOKEN (server trả hash cần ký → plugin ký → server ghép CMS, kiểm tra chữ ký RSA), khung VNPT SmartCA / Viettel MySign / FPT CA (báo "chưa cấu hình" rõ ràng)
- [x] Ký nháy không cần CA (provider nội bộ INITIAL_STAMP, cấu hình Signing:InitialRequiresCa), đóng dấu tổ chức (chỉ CLERK, không ủy quyền), ủy quyền ký hiển thị "KT. …" trên chữ ký và lịch sử
- [x] Menu **Trình ký** (nội bộ + Cổng NCC): 4 tab, lọc loại/NCC/ngày/từ khóa (lưu trên URL), ký lẻ, **ký hàng loạt** (server-side / remote poll song song / USB token tuần tự có tiến độ), xem PDF + các bước + ý kiến cạnh nhau, Trả lại (bắt buộc lý do, chọn bước), Chuyển tiếp ý kiến, badge số việc chờ trên menu
- [x] Thông báo in-app + email (Hangfire, PostgreSQL storage, retry) + SignalR /hubs/notifications; push/email chỉ gửi **sau khi commit** (NotificationOutbox + interceptor); job nhắc SLA mỗi giờ; chuông thông báo
- [x] UI cấu hình luồng ký (kéo thả dnd-kit), nút "Gửi ký" + luồng ký + xác minh chữ ký trên trang tài liệu; trang **Xác minh chữ ký** (upload PDF)

**Kiểm chứng:** dotnet test 229/229 (101 Domain, 25 Application, 103 Integration — gồm 12 test API ký: 5 người ký PHIEU_NHAP_KHO mẫu và verify 5 chữ ký toàn vẹn, trả lại đúng bước, idempotent + 2 click đồng thời chỉ 1 chữ ký, ủy quyền "KT. KẾ TOÁN TRƯỞNG", remote poll, USB token với chứng thư tạo trong test, ký hàng loạt, ký nháy không cần CA, khóa sửa khi trình ký) · 
pm run lint/test/build pass (23 test) · Trình duyệt (Docker): thủ kho ký từ xa → PDF có khung chữ ký đúng ô "Thủ kho"; NCC1 ký hàng loạt trên Cổng NCC; kế toán kho trả lại → thủ kho nhận toast realtime, badge cập nhật ngay; lưu cấu hình luồng.

## Phase 4 — QĐ thầu & Hợp đồng
- [ ] BidDecision (+Supplier, +Item), Item, ItemCategory
- [ ] Import Excel (preview, highlight lỗi) + biểu mẫu .xlsx
- [ ] `IHisClient` + MockHisClient, đồng bộ danh mục theo (Số QĐ + MST)
- [ ] Contract wizard 4 bước, sinh DOCX từ mẫu, sửa ONLYOFFICE
- [ ] Luồng ký HĐ đầy đủ (vòng NCC sửa ↔ Kế toán), chốt bản → PDF → ký
- [ ] Phụ lục; HĐ ngoài thầu (tạo NCC ngay trong form)
- [ ] E2E #1

## Phase 5 — Đề xuất – Tổng hợp – Gọi hàng
- [ ] Proposal (X–N–T từ HIS, rule ≤ còn lại HĐ)
- [ ] ProposalSummary (tổ trưởng sửa SL duyệt), ký duyệt
- [ ] CallOrder (≤ SL duyệt, tách theo NCC), Cổng NCC xác nhận, email deep link
- [ ] E2E #2

## Phase 6 — Nhập kho
- [ ] API inbound HIS (HMAC, chống replay, whitelist IP, rate limit, idempotent) + IntegrationLog + gửi lại
- [ ] Pull job Hangfire; endpoint dev simulate-receipt
- [ ] Validation engine (mỗi rule một class) — Invalid chặn ký / Warning bắt lý do
- [ ] Parse XML hóa đơn điện tử
- [ ] Luồng 5 chữ ký; phiếu hoàn trả; phiếu ảo (VirtualPortal)

## Phase 7 — Thanh toán
- [ ] Job nghiệm thu hằng tháng; BB nghiệm thu
- [ ] 08a + lũy kế + UNC + Đề nghị thanh toán
- [ ] Thanh lý + chốt công nợ → Contract `Liquidated`
- [ ] Cột theo dõi thực hiện ContractItem
- [ ] E2E #3

## Phase 8 — Hồ sơ lưu trữ & Báo cáo
- [ ] Cây hồ sơ, tải ZIP + `muc_luc.pdf`
- [ ] Dashboard, báo cáo + xuất Excel
- [x] Xác minh chữ ký (làm sớm ở Phase 3)
- [ ] E2E #4

## Phase 9 — Hoàn thiện
- [ ] Bảo mật: rate limit, lockout, 2FA, upload checks (magic bytes), HTTPS nginx
- [ ] Job kiểm tra hash MinIO ↔ DB
- [ ] Tối ưu index/hiệu năng (100k bản ghi < 500 ms)
- [ ] RemoteSigningProvider (VNPT SmartCA / Viettel MySign / FPT CA): khung + cấu hình + UI poll đã có từ Phase 3; còn tài liệu hướng dẫn đăng ký API key
- [ ] DEPLOY.md, USER_GUIDE.md, HIS_INTEGRATION.md, API.md
- [ ] Rà soát UI tiếng Việt; checklist mục 13; coverage Domain+Application ≥ 70%
