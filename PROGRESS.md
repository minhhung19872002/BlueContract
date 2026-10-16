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

## Phase 1 — Nền tảng
- [ ] Auth: JWT access 15' + refresh token (DB, rotate, httpOnly cookie SameSite=Strict); PasswordHasher; khóa sau 5 lần sai; 2FA TOTP (tùy chọn)
- [ ] User / Role / Permission / UserRole / RolePermission / UserDataScope / SigningDelegation
- [ ] `[HasPermission]` policy-based authorization
- [ ] Global query filter theo `SupplierId` cho user NCC + integration test cô lập NCC A/B (kể cả đoán ID)
- [ ] AuditLog interceptor (JSONB trước/sau) + trigger append-only
- [ ] CRUD + lịch sử: Organization, Facility, Department(+History), Warehouse, Supplier(+History), User(+History)
- [ ] Layout Nội bộ & Cổng NCC, menu theo quyền, axios refresh-token interceptor, trang đăng nhập
- [ ] Ma trận phân quyền Role × Permission
- [ ] Seed: 1 Organization, 4 cơ sở, ~15 khoa phòng, 5 kho, tài khoản demo mọi role + 3 NCC; `--reset-demo`
- [ ] Màn hình SystemSetting

## Phase 2 — Lõi tài liệu
- [ ] Template engine DOCX (placeholder, filter `money`/`words`, bảng lặp, `#if`, anchor `{{SIG_*}}`), validate template
- [ ] Đọc số tiền bằng chữ tiếng Việt + unit test kỹ
- [ ] `IPdfConverter` → Gotenberg
- [ ] Document / DocumentVersion (+SHA-256, bất biến sau chữ ký đầu)
- [ ] `IDocumentEditorService` → ONLYOFFICE (mở/sửa/track changes/callback lưu phiên bản)
- [ ] Quản lý mẫu biểu (upload, placeholder hợp lệ, xem trước, versioning) + bộ template `templates/` (mục 10)

## Phase 3 — Workflow + Ký số
- [ ] Workflow engine (template/instance/step, state machine, idempotent, concurrency) + unit test mọi chuyển trạng thái
- [ ] `ISigningProvider`; MockSigningProvider PAdES (self-signed theo user, incremental update) — test 5 chữ ký nối tiếp hợp lệ
- [ ] Ký nháy (Initial), đóng dấu tổ chức (OrgSeal, chỉ CLERK), ủy quyền ký
- [ ] Menu Trình ký: tabs, lọc, ký lẻ/hàng loạt, trả lại (chọn bước), xem PDF + lịch sử
- [ ] Notification in-app + email (Mailpit) + SignalR
- [ ] UI cấu hình luồng ký

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
- [ ] Xác minh chữ ký
- [ ] E2E #4

## Phase 9 — Hoàn thiện
- [ ] Bảo mật: rate limit, lockout, 2FA, upload checks (magic bytes), HTTPS nginx
- [ ] Job kiểm tra hash MinIO ↔ DB
- [ ] Tối ưu index/hiệu năng (100k bản ghi < 500 ms)
- [ ] RemoteSigningProvider (VNPT SmartCA / Viettel MySign / FPT CA) khung + ClientSideTokenProvider
- [ ] DEPLOY.md, USER_GUIDE.md, HIS_INTEGRATION.md, API.md
- [ ] Rà soát UI tiếng Việt; checklist mục 13; coverage Domain+Application ≥ 70%
