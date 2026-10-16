# Quyết định kiến trúc (ADR)

Mỗi mục là một ADR ngắn: **Bối cảnh → Quyết định → Hệ quả**. Thêm mục mới ở cuối, không sửa mục cũ (nếu thay đổi thì tạo ADR mới "thay thế ADR-xxx").

---

## ADR-001 — .NET 10 LTS, SDK cài cục bộ cho máy dev
- **Bối cảnh:** Spec yêu cầu .NET 10 (LTS); máy dev chỉ có SDK 9. .NET 8 hết hỗ trợ ngày 10/11/2026, nên fallback sang .NET 8 không hợp lý cho hệ thống production.
- **Quyết định:** Dùng `net10.0`. `backend/global.json` khóa SDK `10.0.100` + `rollForward: latestFeature`. Trên máy dev, SDK 10.0.402 được cài vào `%LOCALAPPDATA%\Microsoft\dotnet10` (không cần quyền admin, không ảnh hưởng SDK 8/9 hiện có). Docker build dùng image `mcr.microsoft.com/dotnet/sdk:10.0`.
- **Hệ quả:** Muốn build/test ngoài Docker thì cần cài .NET 10 SDK (xem README). Chạy toàn bộ hệ thống qua `docker compose` thì không cần SDK.

## ADR-002 — Cấu trúc repo: thư mục gốc chính là `blueprocure/`
- **Quyết định:** Thư mục `D:\Source\BlueContract` đóng vai trò thư mục gốc `blueprocure/` trong spec §3. Tên solution và namespace vẫn là `BlueProcure`.

## ADR-003 — Định danh & cột chung của entity
- **Quyết định:** `Id = Guid.CreateVersion7()` sinh ở tầng Domain (`ValueGeneratedNever`). `RowVersion` (`uint`) ánh xạ cột hệ thống `xmin` của PostgreSQL làm concurrency token. Soft delete bằng global query filter `!IsDeleted`. Interceptor chuyển lệnh `Remove()` thành soft delete và đóng dấu `CreatedAt`/`UpdatedAt` (UTC). `CreatedBy`/`UpdatedBy` sẽ được gán khi có user context (Phase 1).
- **Hệ quả:** Muốn xóa cứng thì phải dùng SQL hoặc `ExecuteDelete` một cách có chủ đích. ID không tuần tự nên không lộ số lượng bản ghi.

## ADR-004 — Quy ước CSDL
- **Quyết định:** `snake_case` (EFCore.NamingConventions). Bảng lịch sử migration tên `__ef_migrations_history`. Kiểu `decimal` mặc định `numeric(18,2)` (tiền); cột số lượng sẽ được cấu hình riêng `numeric(18,3)`. Thời gian là `timestamptz` (UTC); ngày nghiệp vụ hiển thị theo `Asia/Ho_Chi_Minh` (`IClock.TodayLocal`).

## ADR-005 — Khởi tạo khi chạy lần đầu
- **Quyết định:** Khi start, API chạy `DatabaseInitializer` (migrate → seed idempotent → tạo bucket MinIO, có retry khi dependency chưa sẵn sàng). Cờ `Database:InitializeOnStartup` (mặc định `true`). Lệnh `dotnet BlueProcure.Api.dll --seed-only` chỉ migrate + seed rồi thoát (dùng trong `scripts/seed.*`).
- **Hệ quả:** Khi chạy nhiều instance API, nên đặt `InitializeOnStartup=false` cho các instance phụ hoặc chạy migrate như một bước deploy riêng (ghi trong DEPLOY.md ở Phase 9).

## ADR-006 — Sinh TypeScript client từ OpenAPI
- **Quyết định:** Dùng `Microsoft.AspNetCore.OpenApi` (document) + `Microsoft.Extensions.ApiDescription.Server`: build **Debug** của Api ghi `frontend/openapi/blueprocure.json`; `npm run gen:api` sinh `frontend/src/api/schema.d.ts` bằng `openapi-typescript`. Cả hai file đều commit để build frontend/CI không phụ thuộc backend. Gọi API qua axios, kiểu dữ liệu lấy từ `components['schemas'][...]`. ESLint chặn import `axios` ngoài `src/api/`.
- **Hệ quả:** Sau khi đổi DTO/endpoint phải chạy `dotnet build` (Debug) rồi `npm run gen:api`.

## ADR-007 — Cổng trên máy dev
- **Bối cảnh:** Máy dev đang chạy nhiều stack khác chiếm 5432/8080/9000/8025/1025.
- **Quyết định:** Mọi cổng host bind `127.0.0.1` trong dải `47xxx`, cấu hình được qua `.env` (web 47080, api 47081, postgres 47432, minio 47900/47901, onlyoffice 47082, gotenberg 47300, mailpit 47025/47125, seq 47341). Cổng của Postgres/API/Gotenberg/SMTP chỉ mở trong `docker-compose.override.yml` (dev).

## ADR-008 — Thư viện kiểm thử
- **Quyết định:** xUnit + **Shouldly** (FluentAssertions v8 đã đổi sang license thương mại). Integration test dùng Testcontainers (PostgreSQL 16 + MinIO) với `WebApplicationFactory<Program>`.

## ADR-009 — ONLYOFFICE Docs Community (ghi chú license)
- **Quyết định:** Dùng image `onlyoffice/documentserver:9.0` (Community Edition, **AGPL v3**, giới hạn **20 kết nối soạn thảo đồng thời**). Bật JWT (`ONLYOFFICE_JWT_SECRET`). Tích hợp sau interface `IDocumentEditorService` (Phase 2) để có thể chuyển sang bản Enterprise/Developer hoặc trình soạn thảo khác.
- **Hệ quả:** Nếu bệnh viện cần nhiều hơn 20 người sửa đồng thời, hoặc không chấp nhận điều kiện AGPL khi phân phối, thì phải mua license ONLYOFFICE Docs Enterprise.

## ADR-010 — Phiên bản frontend
- **Quyết định:** React **18.3.1** (theo spec, không dùng 19), Ant Design 5.29, React Router 6.30, TanStack Query 5, Zod 3, Vite 8, **TypeScript 5.9** (TS 7 chưa được typescript-eslint hỗ trợ). Cài dependency với `--save-exact`.

## ADR-011 — Logging
- **Quyết định:** Serilog cấu hình qua `appsettings.json` (Console + File cuộn theo ngày, giữ 30 file). Trong Docker, sink Seq được bật qua biến môi trường (`SEQ_URL`). Seq là tùy chọn (`--profile seq`), sink tự bỏ qua khi Seq không chạy. Mỗi request có `X-Correlation-Id`: nhận từ client nếu hợp lệ, nếu không thì tự sinh; giá trị được trả lại trong response header, ProblemDetails (`correlationId`) và từng dòng log.

## ADR-012 — Lỗi trả về client
- **Quyết định:** Mọi lỗi trả về `application/problem+json` với `code` ổn định (`CONTRACT_QTY_EXCEEDED`…) và `title` tiếng Việt, ánh xạ tập trung tại `Application/Common/ErrorMessages.cs`. Lỗi 500 không lộ stack trace. `DbUpdateConcurrencyException` → 409 `CONFLICT`.

## ADR-013 — Sao lưu MinIO
- **Quyết định:** `scripts/backup.*` dùng `pg_dump -Fc` + `mc mirror`, chạy `mc` có sẵn trong image `minio/minio` (cùng phiên bản server) thay vì image `minio/mc` riêng. Giữ 30 bản gần nhất.
