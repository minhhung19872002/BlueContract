# PROMPT CHO CLAUDE CODE — BlueProcure
## Cổng Quản lý & Thanh toán Mua sắm Hàng hóa Trực tuyến cho Bệnh viện

> **Cách dùng:** Đặt file này vào thư mục gốc repo (đổi tên thành `docs/SPEC.md`), rồi trong Claude Code gõ:
> `Đọc toàn bộ docs/SPEC.md. Làm theo đúng mục 0 "Cách làm việc", bắt đầu từ Phase 0. Sau mỗi phase cập nhật PROGRESS.md và commit.`

---

## 0. CÁCH LÀM VIỆC (Claude Code đọc kỹ trước khi code)

Bạn là kỹ sư full-stack senior xây dựng một hệ thống **production-grade** cho bệnh viện công tại Việt Nam. Yêu cầu bắt buộc:

1. **Đọc hết spec trước khi viết dòng code đầu tiên.** Lập kế hoạch, ghi vào `PROGRESS.md` (checklist theo Phase), rồi mới làm.
2. **Làm tuần tự theo Phase (mục 14).** Không nhảy phase. Hết mỗi phase: build sạch không warning nghiêm trọng, test pass, `docker compose up` chạy được, cập nhật `PROGRESS.md`, commit với message dạng `feat(phase-3): contract signing workflow`.
3. **Không để TODO rỗng, không mock giả vờ hoàn thành.** Chỗ nào phụ thuộc hệ thống ngoài (HIS, CA ký số, email) thì làm qua **interface + adapter**, có **Mock adapter chạy được thật** để demo end-to-end, và adapter thật được cấu hình qua `appsettings`.
4. **Ngôn ngữ:** Code, tên biến, tên bảng, comment: tiếng Anh. Toàn bộ **giao diện người dùng, thông báo lỗi, email, biểu mẫu: tiếng Việt có dấu**. Tiền tệ VND, định dạng `1.234.567 ₫`, ngày `dd/MM/yyyy`, múi giờ `Asia/Ho_Chi_Minh` (lưu DB dạng UTC `timestamptz`).
5. **Môi trường dev của chủ dự án là Windows** (PowerShell/CMD + Docker Desktop). Mọi script phải có bản `.ps1` và `.sh`. Không dùng lệnh chỉ chạy trên Linux trong hướng dẫn chính.
6. Khi gặp điểm spec chưa rõ: chọn phương án hợp lý nhất, **ghi quyết định vào `docs/DECISIONS.md`** (ADR ngắn), rồi tiếp tục — không dừng lại hỏi trừ khi ảnh hưởng kiến trúc lớn.
7. Viết test song song với code: unit test cho business rules (Domain/Application), integration test cho API (Testcontainers PostgreSQL), và vài E2E Playwright cho luồng chính.
8. Không commit secret. Dùng `.env.example` + `appsettings.Development.json` với giá trị dev.

---

## 1. BỐI CẢNH NGHIỆP VỤ

Khách hàng mẫu: **Bệnh viện Đa khoa Thành phố Vinh** (4 cơ sở: Cơ sở 1, Cơ sở 2, Cơ sở 3, Khám chữa bệnh theo yêu cầu). Hệ thống phải cấu hình được cho bệnh viện khác (không hard-code tên bệnh viện; lấy từ bảng `Organization`).

Mục tiêu: **Số hóa và ký số toàn bộ vòng đời hợp đồng mua sắm** — Quyết định thầu → Hợp đồng → Đề xuất gọi hàng → Tổng hợp đề xuất → Gọi hàng → Nhập kho → Nghiệm thu → Xác định giá trị khối lượng (mẫu 08a) → Thanh lý hợp đồng. Mọi tài liệu phát sinh được số hóa, ký số, lưu trữ thành **một hồ sơ điện tử hoàn chỉnh cho từng hợp đồng**.

Phạm vi hàng hóa: Thuốc; Vật tư y tế; Hóa chất; Sinh phẩm xét nghiệm; Tài sản/Trang thiết bị; Hàng hóa khác; Dịch vụ; Sửa chữa.

Hai loại mua sắm:
- **Trong thầu** (có Quyết định phê duyệt kết quả lựa chọn nhà thầu, danh mục lấy từ HIS).
- **Ngoài thầu** (có hợp đồng, không có quyết định thầu; hàng hóa nhập trực tiếp trên cổng).

Hai loại hợp đồng theo cách thực hiện:
- **Hợp đồng theo đơn giá cố định, gọi hàng từng đợt** (thuốc, VTYT, hóa chất…) → đi qua luồng Đề xuất → Tổng hợp → Gọi hàng.
- **Hợp đồng trọn gói / giao một lần** (tài sản, dịch vụ, sửa chữa) → bỏ qua luồng đề xuất, đi thẳng Nhập kho (hoặc phiếu nhập kho "ảo" nếu không nhập kho) → Nghiệm thu → Thanh toán.

---

## 2. STACK KỸ THUẬT (bắt buộc)

### Backend
- **.NET 10 (LTS)** — nếu máy chưa có SDK 10 thì dùng .NET 8 LTS và ghi vào DECISIONS.md. ASP.NET Core Web API (Controllers, không dùng Minimal API cho phần nghiệp vụ).
- **Clean Architecture**: `Domain` / `Application` / `Infrastructure` / `Api` (+ `Worker` nếu tách job).
- **EF Core + Npgsql** (PostgreSQL 16). Migrations code-first. Dùng `snake_case` naming convention (`EFCore.NamingConventions`).
- CQRS nhẹ: tự viết dispatcher đơn giản hoặc dùng service classes theo use case. **Không dùng MediatR** (đã chuyển giấy phép thương mại). **Không dùng AutoMapper** (dùng mapping tay hoặc Mapperly).
- **FluentValidation** cho validate request.
- **Hangfire** (storage PostgreSQL) cho background jobs: gửi email, tổng hợp nghiệm thu hàng tháng, đồng bộ HIS định kỳ, sinh PDF.
- **Serilog** (console + file + Seq tùy chọn), correlation id mỗi request.
- **Auth**: JWT access token (15 phút) + refresh token (lưu DB, rotate). Password hash bằng ASP.NET Identity PasswordHasher. Hỗ trợ 2FA TOTP (tùy chọn bật theo user).
- **Lưu file**: MinIO (S3-compatible) qua interface `IFileStorage`. Mỗi file lưu kèm SHA-256.
- **Sinh tài liệu**: template DOCX (DocumentFormat.OpenXml) với placeholder + bảng lặp → convert sang PDF qua **Gotenberg** (container LibreOffice). PDF là bản dùng để ký.
- **Soạn/sửa hợp đồng có theo dõi thay đổi (track changes)**: tích hợp **ONLYOFFICE Docs Community** (container) — mở file DOCX trong trình duyệt, bật chế độ Review cho Nhà cung cấp, callback lưu phiên bản. Ghi chú license (AGPL, giới hạn kết nối đồng thời bản community) vào DECISIONS.md. Phải có interface `IDocumentEditorService` để thay thế được.
- **Ký số**: interface `ISigningProvider` (xem mục 8). Ký PDF chuẩn **PAdES** (signature field + CMS). Thư viện ký PDF đặt trong Infrastructure, sau interface (gợi ý iText 9 — ghi rõ license AGPL/commercial trong DECISIONS.md; hoặc phương án thay thế).
- **Email**: interface `IEmailSender`, dev dùng **Mailpit**.
- API docs: OpenAPI + Scalar UI tại `/docs`.
- Health checks `/health`, `/health/ready` (DB, MinIO, Gotenberg).

### Frontend
- **React 18 + Vite + TypeScript (strict)**.
- **Ant Design 5** (locale `vi_VN`), **TanStack Query** cho server state, **React Router 6**, **react-hook-form + zod**, **dayjs** (locale vi).
- Axios instance có interceptor refresh token.
- PDF viewer: `react-pdf` (pdf.js). Trình soạn DOCX: `@onlyoffice/document-editor-react`.
- Sinh TypeScript client từ OpenAPI (`openapi-typescript` hoặc `orval`) — không viết tay type DTO.
- Hai layout: **Nội bộ bệnh viện** và **Cổng Nhà cung cấp** (cùng app, layout và menu khác theo loại user).
- Responsive tối thiểu cho màn hình trình ký (lãnh đạo hay ký trên tablet/điện thoại).

### Hạ tầng
- **Docker + docker compose** cho toàn bộ: `postgres`, `api`, `web` (nginx serve build + reverse proxy `/api`), `minio`, `onlyoffice`, `gotenberg`, `mailpit`, `seq` (tùy chọn).
- `docker compose up -d` lần đầu phải: tạo DB, chạy migration, seed dữ liệu demo, tạo bucket MinIO. Đăng nhập được ngay bằng tài khoản demo.
- Dockerfile multi-stage, chạy non-root user.
- GitHub Actions: build + test backend, build + lint + test frontend, build image.

---

## 3. CẤU TRÚC REPO

```
blueprocure/
├─ docs/ (SPEC.md, DECISIONS.md, API.md, DEPLOY.md, USER_GUIDE.md, HIS_INTEGRATION.md)
├─ PROGRESS.md
├─ docker-compose.yml, docker-compose.override.yml, .env.example
├─ scripts/ (dev-up.ps1/.sh, reset-db.ps1/.sh, seed.ps1/.sh, backup.ps1/.sh)
├─ backend/
│  ├─ BlueProcure.sln
│  ├─ src/
│  │  ├─ BlueProcure.Domain/          (Entities, Enums, ValueObjects, Domain events, Business rules)
│  │  ├─ BlueProcure.Application/     (Use cases, DTOs, Validators, Interfaces: IHisClient, ISigningProvider, IFileStorage, IEmailSender, IDocumentGenerator, IDocumentEditorService, IPdfConverter)
│  │  ├─ BlueProcure.Infrastructure/  (EF Core, Repos, Adapters: Mock/Real HIS, Signing, MinIO, Gotenberg, OnlyOffice, SMTP, Hangfire jobs)
│  │  └─ BlueProcure.Api/             (Controllers, Auth, Middleware, Filters, Program.cs)
│  └─ tests/ (Domain.Tests, Application.Tests, Api.IntegrationTests)
├─ frontend/
│  ├─ src/ (api/, components/, features/<module>/, layouts/, pages/, hooks/, utils/, i18n/)
│  └─ e2e/ (Playwright)
└─ templates/ (DOCX templates gốc cho biểu mẫu — xem mục 10)
```

---

## 4. VAI TRÒ NGƯỜI DÙNG & PHÂN QUYỀN

### 4.1 Vai trò (Role) mặc định — seed sẵn
| Mã | Tên | Mô tả |
|---|---|---|
| `SYS_ADMIN` | Quản trị hệ thống | Cấu hình hệ thống, người dùng, phân quyền, luồng ký |
| `PROCUREMENT` | Tổ Mua sắm | Tạo quyết định thầu, hợp đồng, biên bản nghiệm thu / xác định giá trị / thanh lý |
| `REVIEWER` | Tổ Rà soát | Rà soát hợp đồng, biên bản trước khi gửi ký |
| `DEPT_USER` | Khoa/Phòng | Tạo đề xuất gọi hàng |
| `AGGREGATOR` | Người tổng hợp | Tổng hợp đề xuất, gọi hàng (thuộc Khoa Dược / Vật tư / TCHC / VTKT-TTB-CNTT) |
| `DOMAIN_HEAD` | Tổ trưởng mảng | Tổ trưởng Dược / Vật tư / Hành chính / TTB: duyệt tổng hợp đề xuất, được sửa số lượng duyệt |
| `ACCOUNTANT` | Kế toán | Xem thay đổi hợp đồng do NCC sửa, ký nháy hợp đồng, tham gia thanh toán |
| `CHIEF_ACCOUNTANT` | Kế toán trưởng | Ký phiếu nhập kho, hoàn trả |
| `WAREHOUSE_ACCOUNTANT` | Kế toán kho | Ký phiếu nhập kho, hoàn trả |
| `STOREKEEPER` | Thủ kho | Ký nhận hàng tại kho |
| `DEPT_HEAD` | Trưởng khoa | Ký phiếu nhập kho, hoàn trả |
| `DIRECTOR` | Giám đốc | Ký hợp đồng, tổng hợp đề xuất, nghiệm thu, xác định giá trị, thanh lý |
| `CLERK` | Văn thư | Đóng dấu điện tử của Bệnh viện |
| `SUPPLIER_ADMIN` | Quản trị NCC | Quản lý tài khoản nhân viên của NCC |
| `SUPPLIER_USER` | Người dùng NCC | Rà soát/sửa hợp đồng, xác nhận gọi hàng, ký hồ sơ, là "Người giao hàng" trên phiếu nhập kho |
| `VIEWER` | Chỉ xem | Xem hồ sơ lưu trữ, báo cáo |

### 4.2 Phân quyền
- Mô hình **Permission-based**: quyền dạng `contract.create`, `contract.review`, `contract.sign`, `proposal.approve`, … gán vào Role; Role gán cho User; user có thể có nhiều role.
- **Phạm vi dữ liệu (data scope)**: theo Cơ sở (Facility), Khoa/Phòng (Department), Mảng phụ trách (Domain: Thuốc/VTYT/Hóa chất/Tài sản/Hành chính…). Ví dụ: Tổ trưởng Dược chỉ thấy tổng hợp đề xuất mảng Thuốc.
- **User NCC chỉ thấy dữ liệu của NCC mình** — enforce ở tầng query (global query filter EF Core theo `SupplierId`), không chỉ ở UI. Viết integration test chứng minh NCC A không đọc được hồ sơ NCC B (kể cả đoán ID).
- **Ủy quyền ký (delegation)**: Giám đốc ủy quyền cho Phó Giám đốc ký thay trong khoảng thời gian; tài liệu thể hiện "KT. GIÁM ĐỐC / PHÓ GIÁM ĐỐC".
- Màn hình **ma trận phân quyền** (Role × Permission) dạng bảng checkbox.
- Kiểm tra quyền ở backend bằng policy-based authorization `[HasPermission("contract.sign")]`.

---

## 5. MÔ HÌNH DỮ LIỆU (Domain)

Tất cả entity có: `Id (uuid v7)`, `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`, `IsDeleted` (soft delete), `RowVersion` (xmin của PostgreSQL cho optimistic concurrency). Bảng lịch sử thay đổi cho dữ liệu danh mục (khoa phòng, người dùng, NCC) theo yêu cầu "lưu lịch sử cập nhật".

### 5.1 Danh mục & tổ chức
- `Organization` (tên BV, mã số thuế, địa chỉ, người đại diện, chức vụ, tài khoản ngân hàng, Kho bạc, logo, ảnh con dấu).
- `Facility` (Cơ sở) — seed 4 cơ sở.
- `Department` (Khoa/Phòng): mã, tên, loại (lâm sàng/cận lâm sàng/phòng chức năng), FacilityId, HisDepartmentCode, IsActive. + `DepartmentHistory`.
- `Warehouse` (Kho): mã, tên, FacilityId, loại (Kho thuốc, Kho VTYT, Kho hóa chất, Kho tài sản…), HisWarehouseCode.
- `User`: username, họ tên, email, SĐT, chức danh, DepartmentId, FacilityId, UserType (`Internal`/`Supplier`), SupplierId (nullable), trạng thái, thông tin chứng thư số đăng ký (serial, issuer, hạn), ảnh chữ ký tay. + `UserHistory`.
- `Role`, `Permission`, `UserRole`, `RolePermission`, `UserDataScope`, `SigningDelegation`.
- `Supplier` (NCC): mã số thuế (unique), tên, địa chỉ, người đại diện, chức vụ, giấy ủy quyền, STK ngân hàng, email nhận thông báo, SĐT. + `SupplierHistory`.
- `ItemCategory`: Thuốc, VTYT, Hóa chất, Sinh phẩm XN, Tài sản/TTB, Hàng hóa, Dịch vụ, Sửa chữa.
- `Item` (Hàng hóa/dịch vụ): **HisKey (mã key HIS)**, mã hàng, tên, hoạt chất/thông số kỹ thuật, quy cách, ĐVT, hãng SX, nước SX, số đăng ký/số GPNK, nhóm thầu, CategoryId, SupplierId, BidDecisionId (nullable), đơn giá trúng thầu, FacilityId.

### 5.2 Quyết định thầu
- `BidDecision`: số quyết định (unique theo BV), ngày ký, trích yếu, tên gói thầu, hình thức lựa chọn nhà thầu, nguồn vốn, thời gian thực hiện, file đính kèm (PDF), trạng thái.
- `BidDecisionSupplier`: các NCC trúng thầu trong quyết định + giá trị trúng thầu.
- `BidDecisionItem`: hàng hóa trúng thầu (liên kết Item, số lượng trúng thầu, đơn giá).
- **Khóa liên kết với HIS = (Số quyết định + Mã số thuế NCC)** — đây là nguyên tắc xuyên suốt.

### 5.3 Hợp đồng
- `Contract`: số hợp đồng, tên, `ProcurementType` (`Bidding`/`NonBidding`), `ContractKind` (`UnitPriceCallOff` gọi hàng từng đợt / `LumpSum` trọn gói), `RequiresWarehouseReceipt` (bool), BidDecisionId (nullable), SupplierId, CategoryId, FacilityId(s), ngày ký, ngày hiệu lực, ngày hết hạn, thời gian thực hiện, giá trị trước thuế, VAT, tổng giá trị, điều khoản thanh toán, bảo hành, bảo lãnh thực hiện HĐ (số, ngân hàng, giá trị, hạn), `TemplateId`, `Status`, `CurrentDocumentVersionId`.
- `ContractItem`: ItemId, số lượng HĐ, đơn giá, thành tiền, VAT; **các cột tính toán**: số lượng đã đề xuất, đã duyệt, đã gọi, đã nhập kho, đã nghiệm thu, đã thanh toán, **còn lại** (= HĐ − đã nhập kho, hoặc theo công thức ghi trong DECISIONS.md).
- `ContractAppendix` (Phụ lục): loại (bổ sung số lượng/giảm giá/thay đổi thông tin/gia hạn), nội dung thay đổi có cấu trúc (bảng ItemId + giá trị cũ → mới), đi luồng ký giống hợp đồng; khi hiệu lực thì cập nhật ContractItem và ghi lịch sử.
- `ContractStatus`: `Draft` → `InReview` (Tổ rà soát) → `SupplierReview` → `AccountantReview` (khi NCC có sửa) → `SupplierSigning` → `AccountantInitialing` → `DirectorSigning` → `Stamping` → `Effective` → `Liquidated` | `Cancelled` | `Returned` (trả lại kèm lý do ở bất kỳ bước nào).

### 5.4 Đề xuất / Tổng hợp / Gọi hàng
- `Proposal` (Phiếu đề xuất của khoa phòng): số phiếu tự sinh, DepartmentId, ContractId (hoặc nhiều hợp đồng cùng mảng — ghi DECISIONS), Domain, kỳ đề xuất, trạng thái, người lập.
- `ProposalLine`: ContractItemId, **tồn đầu kỳ**, **nhập trong kỳ / xuất trong kỳ / tồn hiện tại (lấy từ API HIS)**, **số lượng còn lại của hợp đồng**, số lượng đề xuất, ghi chú. Rule: `SL đề xuất ≤ SL còn lại của HĐ`.
- `ProposalSummary` (Tổng hợp đề xuất): số phiếu, Domain, danh sách Proposal gộp, người tổng hợp, trạng thái (`Draft` → `DomainHeadReview` → `DirectorSigning` → `Approved`/`Returned`).
- `ProposalSummaryLine`: ContractItemId, tổng SL đề xuất từ các khoa, X–N–T tổng hợp (HIS), **SL tổ trưởng duyệt** (được sửa), **SL đã gọi**, **SL còn được gọi**.
- `CallOrder` (Lần gọi hàng): số đơn, ProposalSummaryId, SupplierId, ngày gọi, hạn giao, kho nhận, trạng thái (`Sent` → `SupplierConfirmed` / `PartiallyConfirmed` / `Rejected` → `Delivered`).
- `CallOrderLine`: ContractItemId, SL gọi, **SL NCC xác nhận** (≤ SL gọi), lý do nếu xác nhận thiếu. Rule: `Σ SL gọi các lần ≤ SL duyệt`.

### 5.5 Nhập kho
- `WarehouseReceipt` (Phiếu nhập kho / Phiếu hoàn trả): `Type` (`Receipt`/`Return`), `Source` (`HIS` / `Portal` / `VirtualPortal` cho HĐ không nhập kho), HisReceiptId (unique khi Source=HIS), số phiếu, ngày, kho, SupplierId, BidDecisionNumber, SupplierTaxCode, ContractId (map được hay không), CallOrderId (nullable), **thông tin hóa đơn bắt buộc** (ký hiệu, số HĐ, ngày HĐ, tổng tiền, file XML/PDF hóa đơn điện tử), người giao hàng, trạng thái ký, `ValidationStatus` (`Valid`/`Warning`/`Invalid`), danh sách cảnh báo, `IsIncludedInAcceptance`.
- `WarehouseReceiptLine`: ItemId/HisKey, ContractItemId, số lô, hạn dùng, SL, đơn giá, thành tiền, VAT.
- **Cảnh báo phiếu không hợp lệ** (validation engine, mỗi rule là một class): không tìm thấy hợp đồng hiệu lực theo (Số QĐ + MST); mặt hàng không thuộc hợp đồng; đơn giá khác đơn giá HĐ; SL vượt SL còn lại của HĐ; vượt SL đã gọi; hợp đồng hết hạn; thiếu hóa đơn; tổng tiền hóa đơn ≠ tổng tiền phiếu (cho phép sai số làm tròn cấu hình được); hạn dùng < ngưỡng cấu hình; trùng số hóa đơn. Mức `Invalid` chặn ký; `Warning` cho phép ký nhưng bắt nhập lý do.

### 5.6 Thanh toán
- `AcceptanceRecord` (Biên bản nghiệm thu): số BB, ContractId, kỳ (tháng/năm hoặc tùy chọn khoảng ngày), danh sách WarehouseReceipt (đã ký xong, chưa nghiệm thu), tổng giá trị, trạng thái, `IsIncludedInValuation`.
- `ValuationRecord` (Biên bản xác định giá trị khối lượng hoàn thành — **Mẫu 08a**): số BB, ContractId, danh sách AcceptanceRecord (đã ký, chưa đưa vào thanh toán), giá trị kỳ này, **lũy kế đến kỳ trước, lũy kế đến kỳ này**, số tiền tạm ứng đã thu hồi, số tiền đề nghị thanh toán, **Ủy nhiệm chi đính kèm** (số UNC, ngày, số tiền, file), trạng thái, `PaymentStatus` (`Unpaid`/`Paid`).
- `PaymentRequest` (Đề nghị thanh toán) — sinh kèm ValuationRecord theo mẫu.
- `LiquidationRecord` (Biên bản thanh lý HĐ): ContractId, danh sách toàn bộ ValuationRecord đã ký, giá trị HĐ, giá trị thực hiện, đã thanh toán, **còn phải thanh toán**, danh sách khoản chưa thanh toán được **đánh dấu "đã thanh toán" để chốt công nợ** (lưu ai đánh dấu, khi nào, chứng từ), giá trị giảm trừ/không thực hiện, trạng thái. Khi hiệu lực → Contract.Status = `Liquidated`, khóa mọi thao tác phát sinh mới.

### 5.7 Tài liệu, ký số, luồng duyệt (lõi kỹ thuật)
- `Document`: DocumentType (`Contract`, `Appendix`, `Proposal`, `ProposalSummary`, `WarehouseReceipt`, `Acceptance`, `Valuation`, `PaymentRequest`, `Liquidation`), OwnerEntityType + OwnerId, ContractId (để gom hồ sơ), BidDecisionId, trạng thái, phiên bản hiện tại.
- `DocumentVersion`: số phiên bản, file DOCX (nếu có), file PDF, SHA-256, người tạo, lý do, `IsFinal`. **Bản đã có chữ ký đầu tiên thì bất biến** (immutable) — mọi chỉnh sửa sau đó phải tạo phiên bản mới và reset luồng ký (cảnh báo rõ trên UI).
- `WorkflowTemplate`: cấu hình luồng duyệt/ký theo DocumentType (+ điều kiện: loại HĐ, mảng, cơ sở). Gồm các `WorkflowStepTemplate`: thứ tự, tên bước, ActionType (`Review` rà soát / `Approve` duyệt / `Sign` ký số cá nhân / `Initial` ký nháy / `OrgSeal` đóng dấu tổ chức / `SupplierReview` / `SupplierSign` / `Confirm`), người thực hiện xác định bởi (Role + data scope | user cụ thể | NCC của hợp đồng | người tạo), cho phép trả lại về bước nào, cho phép sửa nội dung không, vị trí chữ ký trên PDF (anchor text như `{{SIG_DIRECTOR}}` hoặc tọa độ), SLA (giờ) để nhắc việc.
- `WorkflowInstance` + `WorkflowStepInstance`: trạng thái từng bước, người xử lý thực tế, thời điểm, ý kiến, chữ ký (SignatureId).
- `Signature`: DocumentVersionId, signer, loại (`Personal`/`Organization`/`Initial`), provider, serial chứng thư, thời điểm ký (timestamp), hash trước/sau, trạng thái xác minh.
- `AuditLog`: ai, khi nào, IP, user agent, hành động, entity, id, dữ liệu trước/sau (JSONB). Ghi tự động qua EF Core interceptor + ghi tay cho hành động nghiệp vụ (ký, trả lại, gửi, xem tài liệu nhạy cảm, tải hồ sơ). **Bảng append-only** (không cho update/delete ở tầng DB bằng trigger).
- `Notification`: in-app + email; template email theo sự kiện.

> **Workflow engine là trái tim của hệ thống.** Viết thành module riêng trong Application (`Workflows/`), có state machine rõ ràng, unit test cho từng chuyển trạng thái, idempotent (bấm "Ký" 2 lần không ký 2 lần), xử lý concurrency (2 người cùng thao tác 1 bước).

---

## 6. CÁC LUỒNG NGHIỆP VỤ CHI TIẾT

### 6.1 Luồng 1 — Quyết định thầu, Danh mục, Hợp đồng (trong thầu)
**B1. Tạo Quyết định thầu** (Tổ Mua sắm): nhập thông tin QĐ, đính kèm PDF QĐ, chọn NCC trúng thầu. Danh mục hàng hóa:
- Cách A: **Kho nhập danh mục trên HIS** (có đủ Mã key, Số QĐ, MST NCC) → cổng gọi `IHisClient.GetItemsByBidDecision(soQD, mst)` để đồng bộ.
- Cách B: **Import Excel** theo biểu mẫu (mục 10) — có màn hình preview, highlight dòng lỗi, chỉ import khi hết lỗi.
- HĐ không nhập kho: nhập danh mục trực tiếp trên cổng, không qua HIS.

**B2. Tạo Hợp đồng từ Quyết định thầu**: từ QĐ bấm "Tạo hợp đồng" → chọn NCC → hệ thống tự lấy danh sách hàng hóa (từ HIS theo số QĐ + MST, hoặc từ danh mục đã import) → Tổ Mua sắm điền SL, đơn giá (mặc định giá trúng thầu, không được vượt), điều khoản, thời gian thực hiện → chọn **mẫu hợp đồng** phù hợp (mục 10) → **Sinh file DOCX** từ template → Lưu → có thể **chỉnh sửa trực tiếp file DOCX** trong ONLYOFFICE.

**B3. Luồng ký duyệt hợp đồng** (workflow template mặc định):
1. Tổ Mua sắm soạn thảo → **Gửi rà soát**.
2. Tổ Rà soát kiểm tra → Trả lại (kèm ý kiến) hoặc **Gửi NCC**.
3. NCC rà soát trong **menu Hợp đồng** (không phải menu trình ký): mở DOCX ở chế độ **Review/Track changes bắt buộc** — xóa hiển thị gạch ngang, thêm hiển thị bôi đỏ. NCC chọn: (a) **Đồng ý ký** nếu không sửa, (b) **Gửi lại** nếu có sửa.
4. Nếu NCC sửa → **Kế toán** xem các thay đổi (màn hình liệt kê từng thay đổi + xem trong ONLYOFFICE), chấp nhận/từ chối từng thay đổi → gửi lại NCC. Vòng lặp tới khi NCC đồng ý bản cuối. Lưu toàn bộ các vòng (phiên bản).
5. Khi NCC đồng ý bản cuối → hệ thống **chốt bản (accept all changes đã được chấp nhận) → convert PDF → hash** → chuyển sang **menu Trình ký** của NCC → NCC **Ký số**.
6. **Kế toán ký nháy** (Initial — xác nhận nội bộ, đóng dấu nháy/ảnh chữ ký nháy ở lề hoặc cuối trang, ghi nhận trong audit, không bắt buộc chứng thư CA — cấu hình được) → chuyển Giám đốc.
7. **Giám đốc ký số** → hệ thống **tự động chuyển Văn thư**.
8. **Văn thư đóng dấu điện tử** (ký bằng chứng thư số tổ chức của Bệnh viện + hình con dấu) → **Hợp đồng có hiệu lực**. Gửi email cho NCC và các bên liên quan.

Sau khi hiệu lực: mở khóa các luồng Đề xuất, Nhập kho, Thanh toán cho hợp đồng.

### 6.2 Luồng 2 — Đề xuất & Tổng hợp (chỉ HĐ `UnitPriceCallOff`)
**B1. Khoa/Phòng hoặc Khoa Dược/Vật tư tạo đề xuất**: chọn mảng + hợp đồng (hoặc chọn mặt hàng, hệ thống gợi ý HĐ còn hiệu lực/còn SL) → hệ thống tự hiện **tồn đầu kỳ** (nếu có) và gọi **API HIS** lấy **Xuất/Nhập/Tồn** của kho tương ứng → user nhập SL đề xuất (**không vượt SL còn lại của HĐ** — validate cả client và server) → **Ký số** → Gửi.
**B2. Tổng hợp đề xuất** (Người tổng hợp): chọn nhiều đề xuất đã gửi (lọc theo mảng, kỳ, cơ sở) → gộp theo mặt hàng → hệ thống tính **X–N–T từng mặt hàng (API HIS)** → tạo file tổng hợp (PDF) → gửi ký duyệt.
**B3. Ký duyệt tổng hợp**: Người tổng hợp → **Tổ trưởng Dược/Vật tư/Hành chính** (được **sửa SL duyệt**; nếu sửa thì có thể trả về người tổng hợp ký lại — theo cấu hình) → **Giám đốc ký số** → Tổng hợp có hiệu lực, làm cơ sở gọi hàng.

### 6.3 Luồng 3 — Gọi hàng
Menu **Quản lý gọi hàng** → chọn tổng hợp đã được GĐ ký → xem chi tiết → nhập SL gọi cho từng mặt hàng (≤ SL duyệt còn lại) → chọn kho nhận, hạn giao → **Gọi hàng**. Một tổng hợp có thể gọi nhiều lần, tách theo NCC tự động.
NCC nhận **email** (link sâu) → vào **Cổng NCC → menu Gọi hàng** → xác nhận SL hoặc nhập SL nhỏ hơn (bắt buộc lý do) → bệnh viện nhận thông báo. Có màn hình theo dõi tình trạng giao hàng của từng lần gọi.

### 6.4 Luồng 4 — Phiếu nhập kho
**B1 (HIS):** Thủ kho làm Nhập/Hoàn trả trên HIS như hiện tại → HIS gửi sang cổng qua **API inbound** (`POST /api/integration/his/warehouse-receipts`) hoặc cổng **pull định kỳ** (Hangfire, cấu hình) — liên kết theo **Số QĐ + MST NCC**. Idempotent theo `HisReceiptId`.
**B2 (Cổng):** nhận dữ liệu → map hợp đồng/mặt hàng → chạy **validation engine** → tự sinh **PDF phiếu nhập kho** theo mẫu → khởi tạo luồng **5 chữ ký**: **Thủ kho → Người giao hàng (NCC) → Kế toán kho → Trưởng khoa → Kế toán trưởng**. Phiếu hoàn trả: cùng luồng, giá trị âm trong lũy kế.
Bắt buộc có thông tin hóa đơn trước khi ký (NCC có thể bổ sung hóa đơn ở bước Người giao hàng: upload XML hóa đơn điện tử → **tự parse XML** điền số/ký hiệu/ngày/tổng tiền).
**Phiếu nhập kho "ảo"** cho HĐ không nhập kho nhưng cần nghiệm thu: Tổ Mua sắm tạo phiếu trên cổng, đánh dấu `VirtualPortal`, **hoàn thành không cần ký** (hiển thị nhãn "Phiếu xác nhận khối lượng — không qua kho").

### 6.5 Luồng 5 — Thanh toán
**Biên bản nghiệm thu**: Hangfire job chạy **ngày 1 hằng tháng** (cấu hình được) tự tổng hợp các phiếu nhập kho đã ký xong, chưa nghiệm thu, **theo từng hợp đồng trong kỳ** → tạo bản nháp cho Tổ Mua sắm. Tổ Mua sắm cũng có thể tạo tay (chọn phiếu). → **Tổ Rà soát** kiểm tra → **NCC ký** + **Giám đốc ký** (thứ tự cấu hình được) → Văn thư đóng dấu (tùy cấu hình).
**Biên bản xác định giá trị khối lượng (08a)**: chọn một hoặc nhiều BB nghiệm thu đã ký, chưa đưa vào thanh toán → hệ thống tính **giá trị kỳ này, lũy kế đến kỳ trước, lũy kế đến kỳ này, % so với giá trị HĐ** → đính kèm **Ủy nhiệm chi** (có thể bổ sung sau khi ký) → sinh **Đề nghị thanh toán** → Rà soát → NCC + Giám đốc ký. Khi ký xong: đánh dấu các BB nghiệm thu "đã đưa vào thanh toán".
**Biên bản thanh lý**: tổng hợp toàn bộ BB xác định giá trị đã ký → hiển thị bảng đối chiếu: Giá trị HĐ / Giá trị thực hiện / Đã thanh toán / Còn lại → cho phép **đánh dấu khoản chưa thanh toán thành "đã thanh toán"** (nhập chứng từ) để chốt công nợ → Rà soát → NCC + Giám đốc ký → Văn thư → HĐ chuyển `Liquidated`.

### 6.6 Mua sắm ngoài thầu
Tạo hợp đồng ngoài thầu: chọn NCC có sẵn hoặc **tạo mới NCC ngay trong form** (tự cấp tài khoản, gửi email kích hoạt) → thêm vật tư/dịch vụ/sửa chữa (nhập tay hoặc import Excel) → chọn mẫu HĐ không thầu → các bước sau giống trong thầu.

### 6.7 Menu Trình ký (dùng chung)
**Toàn bộ file cần ký** của mọi loại tài liệu đều nằm ở **menu Trình ký** (cả nội bộ và NCC). Riêng hợp đồng: NCC rà soát trước ở menu Hợp đồng; đồng ý xong mới sang Trình ký.
Màn hình Trình ký: tab "Chờ tôi xử lý / Tôi đã xử lý / Tôi đã gửi / Bị trả lại", lọc theo loại tài liệu, NCC, hợp đồng, ngày; **ký hàng loạt** (chọn nhiều → ký 1 lần với remote signing; với USB token thì ký tuần tự có progress); xem PDF + lịch sử luồng + ý kiến các bước trước bên cạnh; nút **Ký / Trả lại (bắt buộc lý do, chọn bước trả về) / Chuyển tiếp ý kiến**.

---

## 7. TÍCH HỢP HIS

Viết chi tiết trong `docs/HIS_INTEGRATION.md` để gửi cho hãng HIS.

### 7.1 Outbound (cổng gọi HIS) — `IHisClient`
- `GetItemsByBidDecision(bidDecisionNumber, supplierTaxCode, facilityCode)` → danh mục hàng hóa (HisKey, tên, ĐVT, đơn giá, SL trúng thầu…).
- `GetStockMovement(warehouseCode, hisKeys[], fromDate, toDate)` → tồn đầu kỳ, nhập, xuất, tồn cuối theo mặt hàng.
- `GetWarehouseReceipts(fromDate, toDate)` → dùng cho cơ chế pull.

### 7.2 Inbound (HIS gọi cổng) — cung cấp cho HIS
- `POST /api/integration/his/warehouse-receipts` — đẩy phiếu nhập/hoàn trả.
- `GET  /api/integration/his/contracts?bidDecisionNumber=&supplierTaxCode=` — danh sách hợp đồng hiệu lực + SL còn lại.
- `GET  /api/integration/his/proposal-summaries?status=Approved` — tổng hợp đề xuất đã duyệt.
- `GET  /api/integration/his/items?updatedSince=` — danh mục hàng hóa.
- Xác thực: **API Key + HMAC-SHA256 chữ ký request** (header `X-Api-Key`, `X-Timestamp`, `X-Signature`), chống replay (timestamp ±5 phút), whitelist IP cấu hình được, rate limit.
- Bảng `IntegrationLog`: lưu request/response, trạng thái, số lần retry; màn hình xem log + **gửi lại** cho admin.

### 7.3 Adapter
- `MockHisClient`: dữ liệu giả lập ổn định (seed) để demo toàn bộ luồng, có endpoint dev `POST /api/dev/his/simulate-receipt` để giả lập HIS đẩy phiếu.
- `RestHisClient`: cấu hình base URL, auth, mapping field qua `appsettings` (để thích ứng nhiều hãng HIS). Polly retry + circuit breaker + timeout.

---

## 8. KÝ SỐ

### 8.1 Interface
```csharp
public interface ISigningProvider {
    string ProviderCode { get; }               // "MOCK", "USB_TOKEN", "VNPT_SMARTCA", "VIETTEL_MYSIGN", ...
    SigningMode Mode { get; }                  // ServerSide | ClientSide (hash ký ở client) | RemoteAsync (chờ xác nhận trên app điện thoại)
    Task<SigningSession> BeginAsync(SignRequest req, CancellationToken ct); // chuẩn bị PDF + signature field, trả hash cần ký
    Task<SignResult> CompleteAsync(string sessionId, SignCompletion completion, CancellationToken ct); // nhúng CMS vào PDF
    Task<SigningStatus> GetStatusAsync(string sessionId, CancellationToken ct); // cho remote signing
}
```
- Luồng **hash-based**: server chuẩn bị PDF với chỗ trống chữ ký (ByteRange) → tính hash → provider ký hash → server nhúng CMS → lưu `DocumentVersion` mới + `Signature`.
- Chữ ký hiển thị (visible signature): ảnh chữ ký + "Ký bởi: …, Thời gian: …" tại vị trí anchor của bước.
- **Nhiều chữ ký nối tiếp trên cùng PDF** phải dùng **incremental update** để không làm hỏng chữ ký trước. Viết test: ký 5 lần liên tiếp, xác minh cả 5 chữ ký còn hợp lệ.
- Hỗ trợ timestamp (TSA) cấu hình được.
- Màn hình **Xác minh chữ ký**: upload PDF hoặc xem tài liệu trong hệ thống → liệt kê chữ ký, người ký, thời điểm, toàn vẹn.

### 8.2 Adapter cần làm
- `MockSigningProvider` (**bắt buộc, làm đầy đủ**): tự sinh chứng thư self-signed cho từng user dev (lưu trong DB/MinIO dev), ký PDF thật (PAdES) để demo chữ ký hợp lệ về mặt cấu trúc.
- `RemoteSigningProvider` khung cho **VNPT SmartCA / Viettel MySign / FPT CA**: interface + cấu hình + luồng async (gửi yêu cầu → user xác nhận trên app → poll trạng thái); để `NotImplemented` có thông báo rõ và tài liệu hướng dẫn nếu chưa có API key — nhưng toàn bộ phần UI/flow polling phải chạy với mock.
- `ClientSideTokenProvider` khung cho USB token qua plugin ký số của CA (frontend gọi plugin local ký hash, gửi về server).
- **Ký nháy (Initial)**: không cần CA — đóng ảnh ký nháy + ghi audit; cấu hình được để bắt buộc CA.
- **Đóng dấu tổ chức (OrgSeal)**: dùng chứng thư số tổ chức của BV (cấu hình ở Organization) + ảnh con dấu; chỉ role `CLERK` được thực hiện.

---

## 9. MÀN HÌNH & MENU (Frontend)

### 9.1 Nội bộ bệnh viện
1. **Tổng quan (Dashboard)**: số tài liệu chờ tôi xử lý (theo loại), HĐ sắp hết hạn (≤ 30 ngày), HĐ sắp hết SL (≤ 10%), phiếu nhập kho cảnh báo, tổng giá trị đã/chưa thanh toán theo tháng (biểu đồ), tình trạng gọi hàng chưa NCC xác nhận quá hạn.
2. **Trình ký** (mục 6.7) — badge số lượng ở menu.
3. **Quyết định thầu**: danh sách (lọc năm, gói thầu, NCC), chi tiết (thông tin, NCC trúng, danh mục hàng, các HĐ đã tạo), đồng bộ HIS, import Excel.
4. **Danh mục hàng hóa**: tab theo hạng mục (Thuốc/VTYT/Hàng hóa/Dịch vụ…), tìm kiếm, xem nguồn (HIS/Import/Nhập tay).
5. **Hợp đồng**: danh sách (tìm theo NCC, hạng mục, tên HĐ, số HĐ, trạng thái, còn hiệu lực), tạo mới (wizard 4 bước: Nguồn → NCC & hàng hóa → Điều khoản → Mẫu & xem trước), chi tiết HĐ với các tab: *Thông tin | Hàng hóa & theo dõi thực hiện (bảng SL HĐ/đã gọi/đã nhập/đã nghiệm thu/đã thanh toán/còn lại + progress) | Tài liệu & phiên bản | Luồng ký | Phụ lục | Đề xuất & gọi hàng | Nhập kho | Thanh toán | Lịch sử*.
6. **Rà soát**: danh sách tài liệu chờ Tổ rà soát (HĐ, BB nghiệm thu, BB XĐGT, BB thanh lý), so sánh phiên bản.
7. **Đề xuất**: danh sách đề xuất của khoa mình, tạo đề xuất (bảng lưới có cột X–N–T từ HIS, SL còn lại HĐ, ô nhập SL đề xuất, highlight vượt mức).
8. **Tổng hợp đề xuất**: chọn đề xuất để gộp, bảng tổng hợp, cột SL duyệt (tổ trưởng sửa), xuất Excel/PDF.
9. **Gọi hàng**: danh sách tổng hợp đã duyệt, tạo lần gọi, lịch sử các lần gọi + trạng thái xác nhận của NCC.
10. **Phiếu nhập kho**: danh sách (lọc trạng thái ký, cảnh báo, NCC, kho, ngày), chi tiết + cảnh báo + hóa đơn + luồng ký, tạo phiếu trên cổng / phiếu ảo.
11. **Thanh toán**: 3 tab Nghiệm thu / Xác định giá trị (08a) / Thanh lý — danh sách, tạo mới, chi tiết, bảng lũy kế.
12. **Hồ sơ lưu trữ**: tìm theo **hợp đồng hoặc số quyết định** → cây tài liệu: QĐ → HĐ (+phụ lục) → Đề xuất/Tổng hợp/Gọi hàng → Phiếu nhập kho → Nghiệm thu → 08a + UNC → Thanh lý; xem từng file; **tải toàn bộ hồ sơ dạng ZIP** (kèm file `muc_luc.pdf` liệt kê tài liệu + hash). Đánh dấu hồ sơ "đã đầy đủ".
13. **Báo cáo**: tình hình thực hiện HĐ; công nợ theo NCC; giá trị thanh toán theo tháng/quý/năm, theo mảng, theo cơ sở; tiến độ ký (thời gian trung bình mỗi bước, tài liệu tồn đọng theo người); tất cả **xuất Excel**.
14. **Quản trị**: Cơ sở, Khoa phòng (+lịch sử), Kho, Người dùng (+lịch sử, gán khoa phòng, gán NCC), NCC (+cấp tài khoản), Vai trò & ma trận quyền, Ủy quyền ký, **Cấu hình luồng ký** (UI kéo thả thứ tự bước, chọn role/người, vị trí chữ ký), **Quản lý mẫu biểu** (upload DOCX template, danh sách placeholder hợp lệ, xem trước với dữ liệu mẫu, versioning template), Cấu hình email, Cấu hình tích hợp HIS + log, Cấu hình ký số, **Nhật ký hoạt động** (lọc theo user, đối tượng, hành động, thời gian; xem diff trước/sau).

### 9.2 Cổng Nhà cung cấp
1. Tổng quan: tài liệu chờ ký, đơn gọi hàng chờ xác nhận, HĐ hiệu lực, công nợ bệnh viện còn phải trả.
2. **Hợp đồng**: danh sách HĐ của NCC; HĐ đang chờ rà soát → mở ONLYOFFICE chế độ Review → Gửi lại / Đồng ý ký; xem theo dõi thực hiện.
3. **Gọi hàng**: xác nhận SL.
4. **Trình ký**.
5. **Phiếu nhập kho**: xem, bổ sung hóa đơn (upload XML/PDF).
6. **Thanh toán**: xem BB nghiệm thu, 08a, UNC, thanh lý.
7. **Tài khoản công ty**: thông tin NCC, nhân viên NCC (SUPPLIER_ADMIN quản lý), đăng ký chứng thư số.

### 9.3 Yêu cầu UX
- Mọi bảng: phân trang server-side, sort, lọc, lưu bộ lọc trên URL, xuất Excel.
- Form dài có lưu nháp tự động.
- Trạng thái hiển thị bằng Tag màu thống nhất (định nghĩa một chỗ).
- Timeline luồng ký trực quan (Ant Design Steps/Timeline) ở mọi tài liệu.
- Thông báo realtime (SignalR) khi có tài liệu mới chờ ký.
- Không bao giờ để người dùng thấy lỗi kỹ thuật thô; lỗi nghiệp vụ trả về dạng `ProblemDetails` với `code` + thông điệp tiếng Việt.

---

## 10. BIỂU MẪU (templates/)

Tạo sẵn template DOCX (nội dung pháp lý chuẩn hợp đồng mua sắm công VN, có placeholder) cho:
1. Biểu mẫu import thuốc, vật tư, hóa chất, sinh phẩm xét nghiệm (.xlsx)
2. Biểu mẫu import hàng hóa, tài sản, dịch vụ (.xlsx)
3. Mẫu hợp đồng thanh toán mua thuốc
4. Mẫu hợp đồng mua VTYT, hóa chất, sinh phẩm xét nghiệm
5. Mẫu HĐ mua sắm hàng hóa, tài sản (có thầu)
6. Mẫu HĐ mua sắm hàng hóa, tài sản (không thầu)
7. HĐ sửa chữa, dịch vụ (có thầu)
8. HĐ sửa chữa, dịch vụ (không thầu)
9. Mẫu HĐ trong thầu có **Điều kiện chung + Điều kiện cụ thể**
10. Phiếu nhập kho (+ phiếu hoàn trả)
11. Đề nghị thanh toán
12. Biên bản nghiệm thu
13. Biên bản xác định giá trị khối lượng hoàn thành (**Mẫu 08a**)
14. Biên bản thanh lý hợp đồng
15. Phụ lục bổ sung / giảm giá / thay đổi thông tin
16. Phiếu đề xuất, Phiếu tổng hợp đề xuất, Đơn gọi hàng

Cơ chế template:
- Placeholder dạng `{{Contract.Number}}`, `{{Supplier.Name}}`, `{{Amount | money}}`, `{{Amount | words}}` (**đọc số tiền bằng chữ tiếng Việt** — viết hàm + unit test kỹ: "Một tỷ hai trăm linh năm triệu không trăm ba mươi nghìn đồng"), bảng lặp `{{#Items}}…{{/Items}}`, điều kiện `{{#if}}`.
- Anchor chữ ký `{{SIG_<STEP_CODE>}}` để định vị chữ ký trên PDF.
- Validate template khi upload: báo placeholder không hợp lệ.

---

## 11. YÊU CẦU PHI CHỨC NĂNG

- **Bảo mật**: OWASP Top 10; HTTPS (nginx); CORS chặt; CSRF không cần vì dùng Bearer nhưng refresh token để trong httpOnly cookie SameSite=Strict; rate limit đăng nhập; khóa tài khoản sau 5 lần sai; chính sách mật khẩu; phiên đăng nhập hết hạn; upload chỉ cho phép PDF/DOCX/XLSX/XML/JPG/PNG, giới hạn dung lượng, kiểm tra magic bytes; link tải file là **presigned URL ngắn hạn** sau khi kiểm tra quyền; không lộ ID tuần tự.
- Tuân thủ tinh thần **cấp độ 2–3 an toàn HTTT** (Nghị định 85/2016, TCVN 11930): log đầy đủ, phân quyền tối thiểu, sao lưu.
- **Toàn vẹn tài liệu**: lưu SHA-256 mọi phiên bản; job định kỳ kiểm tra hash file trên MinIO khớp DB, cảnh báo nếu lệch.
- **Hiệu năng**: danh sách 100k bản ghi phân trang < 500 ms; index đầy đủ (ghi rõ trong migration); tránh N+1 (dùng projection).
- **Sao lưu**: script `pg_dump` + mirror MinIO hằng ngày, giữ 30 bản; hướng dẫn restore trong DEPLOY.md.
- **Khả năng mở rộng**: API stateless, chạy nhiều instance được (SignalR backplane qua PostgreSQL/Redis — ghi DECISIONS).
- **Cấu hình**: mọi ngưỡng (ngày chạy job nghiệm thu, sai số làm tròn, SLA nhắc việc, số ngày cảnh báo hết hạn) để trong bảng `SystemSetting` sửa được trên UI.

---

## 12. DỮ LIỆU SEED (demo)

- 1 Organization (BVĐK TP Vinh), 4 Facility, ~15 khoa phòng, 5 kho.
- Tài khoản demo cho **mỗi role** (`muasam`, `rasoat`, `khoanoi`, `tonghop`, `totruongduoc`, `ketoan`, `ketoantruong`, `ketoankho`, `thukho`, `truongkhoa`, `giamdoc`, `vanthu`, `admin`), mật khẩu `Demo@123`; 3 NCC (mỗi NCC 1 admin + 1 user: `ncc1`, `ncc1.user`…).
- 2 Quyết định thầu (thuốc, VTYT) với ~30 mặt hàng; 1 HĐ ngoài thầu sửa chữa.
- Dữ liệu ở **đủ các trạng thái** để demo: HĐ đang chờ NCC rà soát, HĐ hiệu lực, đề xuất chờ duyệt, gọi hàng chờ xác nhận, phiếu nhập kho có cảnh báo, 1 HĐ đã đi hết đến thanh lý.
- Seed chạy idempotent; có lệnh `--reset-demo`.

---

## 13. KIỂM THỬ (Definition of Done chung)

- Unit test: workflow state machine (mọi chuyển trạng thái hợp lệ/không hợp lệ), các rule số lượng (đề xuất ≤ còn lại HĐ; gọi ≤ duyệt; NCC xác nhận ≤ gọi; nhập kho vs HĐ), tính lũy kế 08a, đọc số thành chữ, validation engine phiếu nhập kho, template engine.
- Integration test (Testcontainers PostgreSQL + MinIO): phân quyền & cô lập dữ liệu NCC, API HIS inbound (HMAC, idempotent), ký nhiều chữ ký nối tiếp, concurrency khi 2 người ký cùng bước.
- E2E Playwright (chạy trên docker compose): (1) Tạo QĐ → HĐ → NCC sửa → Kế toán duyệt → ký đủ → hiệu lực; (2) Đề xuất → tổng hợp → GĐ ký → gọi hàng → NCC xác nhận; (3) HIS đẩy phiếu → 5 chữ ký → nghiệm thu → 08a → thanh lý; (4) Tải hồ sơ ZIP.
- Coverage Domain + Application ≥ 70%.

---

## 14. KẾ HOẠCH THEO PHASE

Mỗi phase kết thúc phải **chạy được** và demo được phần đã làm.

| Phase | Nội dung | Kết quả kiểm chứng |
|---|---|---|
| **0. Khởi tạo** | Solution Clean Architecture, Vite app, docker-compose đủ service, CI, health checks, Serilog, OpenAPI, sinh TS client, PROGRESS.md, DECISIONS.md | `docker compose up` → web mở được, `/health/ready` xanh |
| **1. Nền tảng** | Auth JWT/refresh, User/Role/Permission/DataScope, global query filter NCC, AuditLog interceptor, Organization/Facility/Department/Warehouse/Supplier CRUD + lịch sử, layout Nội bộ & NCC, seed users | Đăng nhập từng role, menu đúng quyền, test cô lập NCC pass |
| **2. Lõi tài liệu** | IFileStorage (MinIO), template engine DOCX + đọc số thành chữ, Gotenberg PDF, DocumentVersion + hash, ONLYOFFICE tích hợp (mở/sửa/track changes/callback lưu), quản lý mẫu biểu | Sinh HĐ từ template → sửa trong trình duyệt → lưu phiên bản → PDF |
| **3. Workflow + Ký số** | Workflow engine cấu hình được, MockSigningProvider PAdES nhiều chữ ký, ký nháy, đóng dấu tổ chức, menu Trình ký (ký lẻ/hàng loạt/trả lại), notification in-app + email + SignalR, UI cấu hình luồng ký | Ký 5 chữ ký nối tiếp hợp lệ; trả lại đúng bước |
| **4. QĐ thầu & Hợp đồng** | BidDecision, Item, import Excel, MockHisClient + đồng bộ danh mục, Contract wizard, luồng ký HĐ đầy đủ (gồm vòng NCC sửa ↔ Kế toán), phụ lục, HĐ ngoài thầu | E2E #1 pass |
| **5. Đề xuất – Tổng hợp – Gọi hàng** | Proposal (X–N–T từ HIS), ProposalSummary (tổ trưởng sửa SL), CallOrder, cổng NCC xác nhận, email | E2E #2 pass |
| **6. Nhập kho** | API inbound HIS (HMAC, idempotent, log, gửi lại), pull job, validation engine, parse XML hóa đơn điện tử, luồng 5 chữ ký, phiếu hoàn trả, phiếu ảo | Giả lập HIS đẩy phiếu → ký đủ |
| **7. Thanh toán** | Job nghiệm thu hằng tháng, BB nghiệm thu, 08a + lũy kế + UNC + đề nghị thanh toán, thanh lý + chốt công nợ, cập nhật cột theo dõi HĐ | E2E #3 pass |
| **8. Hồ sơ lưu trữ & Báo cáo** | Cây hồ sơ, tải ZIP + mục lục, dashboard, báo cáo + xuất Excel, xác minh chữ ký | E2E #4 pass |
| **9. Hoàn thiện** | Bảo mật (rate limit, lockout, 2FA, upload checks), kiểm tra hash định kỳ, backup scripts, tối ưu index/hiệu năng, RemoteSigningProvider khung, tài liệu DEPLOY.md/USER_GUIDE.md/HIS_INTEGRATION.md, rà soát toàn bộ UI tiếng Việt | Checklist mục 13 đạt hết |

---

## 15. QUY ƯỚC CODE

- Backend: nullable enable, warnings-as-errors cho project chính, `async` suốt chiều, `CancellationToken` mọi nơi, không business logic trong Controller, Domain entity có method nghiệp vụ (không anemic), giá trị tiền dùng `decimal(18,2)`, số lượng `decimal(18,3)`.
- Kết quả use case dùng `Result<T>` / lỗi nghiệp vụ có mã (`CONTRACT_QTY_EXCEEDED`…) map sang tiếng Việt ở một chỗ.
- Frontend: feature-folder, không gọi axios trực tiếp trong component (qua hooks TanStack Query), component dùng lại cho: `MoneyInput`, `QuantityInput`, `StatusTag`, `WorkflowTimeline`, `PdfViewer`, `DocEditor`, `SignButton`, `ReturnDialog`, `SupplierSelect`, `ItemPicker`, `ExportExcelButton`.
- ESLint + Prettier; `dotnet format`; pre-commit hook.
- README.md: chạy dev trên **Windows** trong ≤ 5 lệnh, danh sách tài khoản demo, URL các service (web, api docs, mailpit, minio console, onlyoffice, hangfire dashboard).

---

**Bắt đầu ngay với Phase 0.** Trước tiên tạo `PROGRESS.md` liệt kê toàn bộ Phase và hạng mục con dưới dạng checklist, ghi các quyết định ban đầu vào `docs/DECISIONS.md`, rồi triển khai.
