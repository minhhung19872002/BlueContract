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

## Phase 4 — QĐ thầu & Hợp đồng ✅ (16/10/2026)
- [x] Danh mục: 8 hạng mục (seed mọi môi trường), Item (mã key HIS, nguồn HIS / Import / Nhập tay), màn hình Danh mục hàng hóa theo tab hạng mục, nhập tay, xuất Excel
- [x] BidDecision (+ NCC trúng thầu, + hàng trúng thầu, đính kèm PDF QĐ, ban hành/đóng, số QĐ khóa sau ban hành), SL đã vào HĐ / còn lại theo từng mặt hàng
- [x] Import Excel 2 biểu mẫu (.xlsx — thuốc/VTYT/hóa chất/sinh phẩm; hàng hóa/tài sản/dịch vụ, có sheet hướng dẫn + danh sách mã hạng mục + dropdown): xem trước từng dòng, tô đỏ ô lỗi (thiếu cột bắt buộc, số kiểu VN, MST, hạng mục, trùng mã), chỉ import khi hết lỗi; import lại = cập nhật; 	emplates/01_…xlsx,  2_…xlsx
- [x] `IHisClient`: `MockHisClient` tất định (cùng Số QĐ + MST → cùng danh mục; X–N–T cân đối) và `RestHisClient` cấu hình URL + ánh xạ trường (`His:*`), retry/circuit breaker/timeout; đồng bộ danh mục theo (Số QĐ + MST), idempotent
- [x] Hợp đồng: wizard 4 bước (Nguồn → NCC & hàng hóa → Điều khoản → Mẫu & xem trước PDF), lưu nháp tự động; SL ≤ còn lại của QĐ, đơn giá ≤ giá trúng thầu (client + server); sinh DOCX từ mẫu; sửa trực tiếp trong ONLYOFFICE; chi tiết HĐ 6 tab (thông tin, hàng hóa & theo dõi thực hiện, tài liệu, luồng ký, phụ lục, lịch sử)
- [x] Luồng ký HĐ đầy đủ: Tổ rà soát → NCC rà soát (menu Hợp đồng, ONLYOFFICE Review hoặc tải lên DOCX Track Changes) → Kế toán xem từng thay đổi, chấp nhận/từ chối từng thay đổi → gửi lại NCC (vòng lặp) → NCC đồng ý bản cuối → **chốt bản** (chấp nhận thay đổi còn lại → phiên bản mới → PDF) → NCC ký → KT ký nháy → GĐ ký → Văn thư đóng dấu → **Hiệu lực** + email NCC; trạng thái HĐ đồng bộ theo luồng (observer)
- [x] Phụ lục: bổ sung SL / giảm giá / thay đổi thông tin / gia hạn, sinh DOCX từ mẫu PHU_LUC, cùng luồng ký, hiệu lực thì cập nhật ContractItem / ngày hết hạn
- [x] HĐ ngoài thầu: tạo NCC ngay trong form (cấp tài khoản quản trị NCC, gửi mật khẩu tạm), mặt hàng nhập tay
- [x] Dữ liệu demo bằng chính nghiệp vụ (đóng vai người dùng): QĐ 125 (thuốc, NCC1) + QĐ 126 (VTYT, NCC2) đồng bộ từ HIS, HĐ 15 chờ NCC1 rà soát, HĐ 16 đã ký đủ có hiệu lực, HĐ 17 sửa chữa ngoài thầu bản nháp
- [x] E2E #1 (Playwright, `e2e/`, `scripts/e2e.ps1`): QĐ → HĐ → NCC sửa → Kế toán duyệt → ký đủ → hiệu lực, toàn bộ qua UI 6 vai trò; workflow `E2E` trên GitHub Actions (thủ công + hằng đêm)

**Sửa lỗi phát hiện trong phase:** `IgnoreQueryFilters()` trong subquery tắt luôn bộ lọc cô lập NCC của cả truy vấn (NCC thấy HĐ của NCC khác) → bỏ khỏi mọi subquery + test hồi quy; NCC không còn thấy HĐ bản nháp nội bộ; job email Hangfire không deserialize được danh sách người nhận (kiểu do collection expression sinh ra) → chuẩn hóa thành mảng + test; placeholder DatePicker tiếng Anh (lỗi locale antd) → ghi đè tiếng Việt; mẫu biểu từng bị xuất nhầm vào `backend/src/templates` → chuyển về `templates/`.

**Kiểm chứng:** `dotnet test` 254/254 (115 Domain, 25 Application, 114 Integration — gồm luồng HĐ đầy đủ qua API có vòng NCC↔Kế toán + 4 chữ ký hợp lệ + phụ lục áp dụng, HĐ ngoài thầu tạo NCC + tài khoản, import Excel có lỗi/hết lỗi, dữ liệu demo đủ trạng thái, DocxRevisions, HIS mock/REST, email job) · `npm run lint/test/build` pass · **E2E #1 Playwright pass (29 s)** · trình duyệt: danh sách/chi tiết QĐ, wizard xem trước PDF 4 trang, tạo HĐ, panel rà soát của NCC1.

## Phase 5 — Đề xuất – Tổng hợp – Gọi hàng ✅ (16/10/2026)
- [x] Phiếu đề xuất (số tự sinh DX-yyyy-nnnn): khoa chọn mảng + kho + kỳ → lưới mặt hàng của các HĐ gọi hàng còn hiệu lực trong mảng với **X–N–T từ HIS** (`GetStockMovement`), SL còn lại HĐ, ô nhập SL đề xuất tô đỏ khi vượt; rule ≤ còn lại kiểm tra cả client và server; server tự lấy lại số liệu (không tin client); phạm vi khoa theo data scope; ký số người lập (WF_PROPOSAL) → gửi
- [x] Tổng hợp đề xuất (TH-…): chọn phiếu đã gửi theo mảng → gộp theo mặt hàng HĐ, X–N–T cộng theo các kho (HIS) → PDF → người tổng hợp ký → **tổ trưởng sửa SL duyệt** (≤ còn lại HĐ; sửa thì tạo phiên bản mới, người tổng hợp ký lại) → Giám đốc ký → Đã duyệt; xuất Excel; hủy thì trả các phiếu đề xuất về chờ tổng hợp
- [x] Gọi hàng (GH-…): từ tổng hợp đã duyệt, SL gọi ≤ SL duyệt còn lại, **tự tách đơn theo NCC**, kho nhận, hạn giao, PDF đơn gọi hàng; email + thông báo NCC (link sâu `/ncc/goi-hang/{id}`); NCC xác nhận từng dòng (≤ SL gọi, thiếu phải có lý do) → Đã xác nhận / Xác nhận một phần / Từ chối, phần không giao trả lại SL được gọi; bệnh viện nhận thông báo; theo dõi đơn quá hạn chưa xác nhận; hủy đơn chưa xác nhận
- [x] Cột theo dõi HĐ cập nhật tự động: đã đề xuất / đã duyệt / đã gọi (tính từ nguồn, chạy sau commit trong scope riêng)
- [x] Cổng NCC: menu Gọi hàng (danh sách + xác nhận); cô lập NCC cho đơn gọi hàng và tài liệu
- [x] Dữ liệu demo: 2 phiếu đề xuất của Khoa Nội, 1 tổng hợp chờ tổ trưởng duyệt, 1 tổng hợp đã duyệt có đơn gọi hàng chờ NCC2 xác nhận
- [x] E2E #2 (Playwright): đề xuất → tổng hợp → tổ trưởng duyệt → GĐ ký → gọi hàng → NCC xác nhận thiếu có lý do → bệnh viện thấy trạng thái

**Sửa lỗi phát hiện trong phase:** quyền chỉnh sửa tài liệu loại Đề xuất / Tổng hợp / Gọi hàng; trạng thái nghiệp vụ không tự làm mới sau khi ký (frontend làm mới mọi query đang hiển thị sau thao tác luồng ký và khi có push realtime); giới hạn đăng nhập theo IP cấu hình được (`AUTH_LOGIN_RATE_LIMIT`) để chạy E2E.

**Kiểm chứng:** `dotnet test` 264/264 (Domain 123, Application 25, Integration 116 — gồm luồng đề xuất → NCC xác nhận qua API với mọi rule số lượng, cô lập NCC, cột theo dõi HĐ, dữ liệu demo) · `npm run lint/test/build` pass · **E2E #1 + #2 Playwright pass (50 s)**.

## Phase 6 — Nhập kho ✅ (16/10/2026)
- [x] API inbound HIS `POST /api/integration/his/warehouse-receipts` + `GET contracts / proposal-summaries / items`: API key + chữ ký HMAC-SHA256 (method, path, timestamp, hash body), timestamp ±5 phút, chống replay cho request ghi, whitelist IP (CIDR), rate limit theo key; idempotent theo `hisReceiptId` (trùng → Duplicate, sửa khi chưa ký → Updated, đã ký → 409)
- [x] `IntegrationLog` ghi mọi lời gọi (kể cả bị từ chối) với request/response, số lần xử lý; màn hình **Quản trị → Tích hợp HIS**: cấu hình, nhật ký, chi tiết, **Gửi lại**, **Kéo phiếu từ HIS ngay**
- [x] Pull job Hangfire `his-receipt-pull` (cron cấu hình); `IHisClient.GetWarehouseReceipts` cho Mock + REST; endpoint dev `POST /api/dev/his/simulate-receipt` (push/pull, kịch bản Hợp lệ / Cảnh báo / Không hợp lệ) + nút "Giả lập HIS gửi phiếu"
- [x] Map NCC theo MST, hợp đồng theo Số QĐ + MST, mặt hàng theo HIS key/mã hàng, tự gắn đơn gọi hàng còn mở; kho theo mã kho HIS
- [x] Validation engine 10 rule (mỗi rule một class): không tìm thấy HĐ/NCC, HĐ hết hạn/chưa hiệu lực/đã thanh lý, mặt hàng ngoài HĐ, đơn giá khác HĐ, vượt SL còn lại HĐ, hoàn trả vượt đã nhập, vượt SL đã gọi, thiếu hóa đơn, tổng tiền hóa đơn lệch (sai số làm tròn cấu hình), hạn dùng hết/gần hết (ngưỡng cấu hình), trùng số hóa đơn
- [x] `IWorkflowGuard` trong workflow engine: **Không hợp lệ chặn ký**, **Cảnh báo bắt nhập lý do** (lưu vào ý kiến bước), **thiếu hóa đơn chặn từ bước NCC**; UI hiển thị lý do chặn và ô lý do ngay trong khung luồng ký
- [x] Hóa đơn: NCC/bệnh viện tải **XML hóa đơn điện tử (tự đọc ký hiệu, số, ngày, tiền, MST — chuẩn NĐ123/TT78, chống XXE)**, PDF bản thể hiện, hoặc nhập tay; kiểm tra lại tự động
- [x] Luồng **5 chữ ký** (Thủ kho → Người giao hàng NCC → Kế toán kho → Trưởng khoa → Kế toán trưởng) do tài khoản dịch vụ HIS khởi tạo; phiếu bị trả lại chờ HIS gửi bản sửa hoặc "Gửi ký lại"; PDF phiếu theo mẫu `PHIEU_NHAP_KHO`
- [x] Phiếu hoàn trả (trừ lũy kế), phiếu lập trên cổng, **phiếu ảo** xác nhận khối lượng (hoàn thành không cần ký)
- [x] "Đã nhập kho" của hợp đồng và "đã giao" của đơn gọi hàng tính lại từ phiếu (đơn gọi tự chuyển Đã giao)
- [x] Màn hình Phiếu nhập kho (lọc trạng thái ký, kiểm tra, loại, nguồn, NCC, kho, ngày, chưa có hóa đơn; xuất Excel), chi tiết (cảnh báo theo dòng, hóa đơn, PDF, luồng ký), lập/sửa phiếu; tab **Nhập kho** trong chi tiết hợp đồng; Cổng NCC: menu Phiếu nhập kho
- [x] Dữ liệu demo: 1 phiếu từ HIS ký đủ 5 chữ ký, 1 phiếu có cảnh báo chờ thủ kho, 1 phiếu không hợp lệ
- [x] `docs/HIS_INTEGRATION.md` (xác thực, ví dụ ký bằng PowerShell/bash/C#, schema, mã lỗi, outbound, pull, môi trường thử)

**Kiểm chứng:** `dotnet test` 301/301 (Domain 133, Application 46 — 10 rule validation, parser XML, HMAC; Integration 122 — HMAC/timestamp/replay/idempotent, 5 chữ ký nối tiếp cập nhật HĐ, Invalid chặn ký, cảnh báo cần lý do, NCC tải XML hóa đơn, cô lập NCC, nhật ký + gửi lại, giả lập push/pull) · `npm run lint/build` pass · trình duyệt: thủ kho ký phiếu cảnh báo có lý do, NCC bị chặn khi chưa có hóa đơn → tải XML tự điền → ký được; ví dụ ký request trong tài liệu gọi thành công API docker · E2E #1 + #2 vẫn pass.

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
