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
- **Quyết định:** Khi start, API chạy `DatabaseInitializer` (migrate → seed idempotent → tạo bucket MinIO, có retry khi dependency chưa sẵn sàng). Cờ `Database:InitializeOnStartup` (mặc định `true`). Lệnh `dotnet BlueContract.Api.dll --seed-only` chỉ migrate + seed rồi thoát (dùng trong `scripts/seed.*`).
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

## ADR-014 — Token & phiên đăng nhập
- **Quyết định:** Access token JWT HS256, 15 phút, chứa quyền (`perm`), phạm vi dữ liệu (`dscope`), `supplier_id` và `stamp`. Refresh token là chuỗi ngẫu nhiên 64 byte, DB chỉ lưu SHA-256; mỗi lần refresh sinh token mới cùng `FamilyId`. Nếu token đã xoay vòng bị dùng lại thì thu hồi toàn bộ family (nghi bị đánh cắp). Mỗi request kiểm tra `SecurityStamp` (cache 15 s), nên đổi mật khẩu, vai trò, khóa hoặc vô hiệu hóa tài khoản có hiệu lực gần như ngay.
- **Hệ quả:** Không cần danh sách đen access token. Access token chỉ giữ trong bộ nhớ trình duyệt; F5 khôi phục phiên bằng cookie refresh.

## ADR-015 — Cô lập dữ liệu nhà cung cấp
- **Quyết định:** Dùng named query filter của EF Core 10. Mỗi entity có filter `SoftDelete` và filter `SupplierIsolation` riêng. Entity nghiệp vụ implement `ISupplierScoped` (có `SupplierId`) được gắn filter tự động; `Supplier` (theo `Id`) và `User` (theo `SupplierId`) cấu hình riêng. Filter đọc `ICurrentUser` qua thuộc tính của `AppDbContext`, nên giá trị được tham số hóa theo từng request.
- **Hệ quả:** Truy vấn bỏ qua filter (`IgnoreQueryFilters`) chỉ dùng cho đăng nhập và seed. Code review phải chặn mọi chỗ dùng khác.

## ADR-016 — Lịch sử danh mục & nhật ký
- **Quyết định:** Một interceptor duy nhất làm ba việc theo thứ tự: đóng dấu thời gian, chuyển xóa cứng thành xóa mềm, ghi `audit_logs` (JSONB trước/sau, che `PasswordHash`/`SecurityStamp`/`TwoFactorSecret`), và thêm snapshot vào bảng `*_history` cho entity đánh dấu `IHasHistory` (Department, User, Supplier). `audit_logs` có trigger chặn UPDATE/DELETE. `--reset-demo` dùng TRUNCATE, lệnh này không kích hoạt trigger theo dòng; chỉ được bật khi `Seed:AllowReset=true` (môi trường dev).

## ADR-017 — Cấp tài khoản bằng mật khẩu tạm
- **Bối cảnh:** Spec nói "gửi email kích hoạt".
- **Quyết định:** Hệ thống sinh mật khẩu tạm đạt chính sách mật khẩu, gửi qua email và đặt cờ `MustChangePassword`. Server chặn mọi API (trừ `/api/auth/*`) cho đến khi người dùng đổi mật khẩu. Cách này đơn giản hơn link kích hoạt mà vẫn đảm bảo mật khẩu tạm chỉ dùng được một lần.

## ADR-018 — Tìm kiếm không phân biệt hoa thường
- **Quyết định:** Dùng `EF.Functions.ILike` của Npgsql ngay trong tầng Application (PostgreSQL là yêu cầu bắt buộc). Tham số tìm kiếm được escape `%` và `_`. Khi dữ liệu lớn (Phase 9) sẽ bổ sung index `pg_trgm` cho các cột tìm kiếm chính.

## ADR-019 — TOTP tự viết, Data Protection lưu trong DB
- **Quyết định:** TOTP theo RFC 6238 (SHA-1, 6 số, bước 30 s, chấp nhận lệch ±1 bước) tự viết khoảng 60 dòng, có test bằng vector chuẩn của RFC, không thêm thư viện. Secret TOTP được mã hóa bằng ASP.NET Data Protection; key ring lưu trong bảng `data_protection_keys` để nhiều instance API dùng chung.

## ADR-020 — Enum dạng chuỗi
- **Quyết định:** Enum lưu trong DB dạng chuỗi (convention `HaveConversion<string>`) và serialize JSON dạng chuỗi, cả trong MVC lẫn OpenAPI, để TS client sinh ra kiểu union (`"Clinical" | "Paraclinical" | ...`).

## ADR-021 — Lưu trữ tệp: SeaweedFS (S3) thay cho container MinIO (thay thế một phần ADR-013)
- **Bối cảnh:** Image MinIO trên Docker Hub và quay.io không còn pull được nữa (CI báo `pull access denied for minio/minio`). Máy dev vẫn chạy được chỉ vì image đã có sẵn trong cache. Chainguard có image MinIO nhưng chỉ có tag `latest`, trái với yêu cầu ghim phiên bản.
- **Quyết định:** Dùng `chrislusf/seaweedfs:3.97` (Apache-2.0) bật S3 gateway, credential lấy từ `.env`. Code vẫn đi qua `IFileStorage` → `S3FileStorage` (client S3 của MinIO .NET SDK), nên không phụ thuộc server cụ thể. Production có thể trỏ sang bất kỳ dịch vụ S3 nào (MinIO thương mại, Ceph RGW, AWS S3, Viettel/VNPT Cloud S3) chỉ bằng cấu hình `Storage:*`. Integration test chạy với cùng image (Testcontainers generic container), có test presigned URL. Backup dùng `amazon/aws-cli:2.27.0 s3 sync` thay cho `mc mirror`.
- **Hệ quả:** Giao diện quản trị MinIO được thay bằng giao diện filer của SeaweedFS (cổng 47901). Health check đổi tên `minio` → `storage`.

## ADR-022 — Template engine DOCX tự viết
- **Bối cảnh:** Các thư viện template DOCX mã nguồn mở hoặc chưa hỗ trợ lặp dòng bảng kèm định dạng tiền/số kiểu Việt Nam, hoặc có license thương mại.
- **Quyết định:** Viết engine riêng trên DocumentFormat.OpenXml (khoảng 450 dòng, có test đầy đủ). Cú pháp kiểu mustache như spec §10. Trong đoạn chứa thẻ, các run chỉ có chữ được gộp lại nên đoạn đó lấy định dạng của run đầu tiên. Người soạn mẫu cần đặt thẻ trong đoạn có định dạng thống nhất.
- **Hệ quả:** Mẫu do bệnh viện tải lên phải qua validator; mẫu chuẩn được sinh từ code (`BuiltInTemplates`) để chữ, placeholder và anchor luôn khớp với `TemplateFieldCatalog`.

## ADR-023 — Tích hợp ONLYOFFICE
- **Quyết định:** API cấp cho document server URL tải file và callback dạng `{ApiBaseUrlForEditor}/api/onlyoffice/...?token=`, token do Data Protection ký, hạn 12 giờ. Callback phải có JWT hợp lệ (dùng chung `ONLYOFFICE_JWT_SECRET`). Link tải bản đã sửa do document server trả về được viết lại sang `OnlyOffice:InternalUrl`, vì document server dựng link theo host mà trình duyệt dùng. Mỗi lần mở trình soạn thảo tạo một `DocumentEditSession`; session id được dùng làm document key.
- **Hệ quả:** Khi chạy API bằng `dotnet run` ngoài Docker, document server phải gọi ngược được API (cấu hình `OnlyOffice:ApiBaseUrlForEditor`, mặc định `http://host.docker.internal:5296`).

## ADR-024 — Kiểu số trong JSON / OpenAPI
- **Quyết định:** `JsonNumberHandling.Strict` cho cả MVC và OpenAPI. Mặc định của .NET cho phép đọc số từ chuỗi, khiến schema sinh kiểu `number | string` cho mọi số nguyên trong TS client.

## ADR-025 — Ký số PAdES bằng iText 9 (AGPL)
- **Bối cảnh:** Cần ký PAdES nhiều chữ ký nối tiếp (incremental update), ký hash từ xa / USB token và xác minh. Thư viện .NET khác hoặc không hỗ trợ ký ngoài (external signature), hoặc không xác minh được nhiều revision.
- **Quyết định:** Dùng itext 9.x + itext.bouncy-castle-adapter. Luồng: Prepare (thêm trường chữ ký + hình hiển thị tại neo {{SIG_<MÃ BƯỚC>}}, tính digest) → provider trả CMS → Embed → tự Verify lại trước khi lưu. CMS (CAdES detached, signed attributes) do CmsBuilder dựng để provider chỉ cần ký đúng chuỗi DataToSign (RSA-SHA256 PKCS#1 v1.5).
- **Hệ quả:** iText là AGPL-3.0: hệ thống phải công bố mã nguồn cho người dùng qua mạng, hoặc bệnh viện mua license thương mại iText trước khi vận hành chính thức (ghi trong DEPLOY.md). PdfSignatureService là adapter duy nhất phụ thuộc iText, nên có thể thay thế.

## ADR-026 — Chứng thư thử nghiệm và provider ký
- **Quyết định:** MOCK tự cấp chứng thư self-signed RSA-2048 cho mỗi user (và một chứng thư cho tổ chức), PFX mã hóa bằng Data Protection, lưu DB. Màn hình xác minh gắn nhãn "Tự cấp (thử nghiệm)". Provider CA thật (VNPT SmartCA, Viettel MySign, FPT CA) dùng chung cấu hình Signing:<Ca>:{BaseUrl,ClientId,ClientSecret}. Khi chưa có thông tin này, provider ẩn khỏi danh sách và báo SIGNING_PROVIDER_NOT_CONFIGURED. Toàn bộ UI ký từ xa (poll) chạy với MOCK_REMOTE. Ký nháy mặc định không cần CA (INITIAL_STAMP dùng chứng thư nội bộ của user); bật Signing:InitialRequiresCa để bắt buộc dùng CA.
- **Hệ quả:** Production: Signing:EnableMockProviders=false và cấu hình ít nhất một CA. USB token dùng plugin local của CA qua adapter VITE_TOKEN_PLUGIN_URL (2 endpoint /certificate, /sign). Nếu chưa có plugin, người dùng có thể ký thủ công: tải .cer, ký dữ liệu được cấp, rồi dán chữ ký.

## ADR-027 — Thông báo: Hangfire + SignalR + outbox trong unit of work
- **Quyết định:** Email gửi qua Hangfire (PostgreSQL storage, schema hangfire, retry 5 lần). Job định kỳ workflow-sla-reminders chạy mỗi giờ. Realtime dùng SignalR hub /hubs/notifications (JWT qua query `access_token`). Push và email được xếp vào NotificationOutbox (scoped), và NotificationDispatchInterceptor chỉ gửi sau khi SaveChanges thành công. Nhờ vậy trình duyệt refetch thấy ngay dữ liệu mới, và không có email cho thay đổi đã rollback. Trạng thái ký từ xa / USB token nằm trong IDistributedCache (bộ nhớ trong 1 instance).
- **Hệ quả:** Chạy nhiều instance API cần thêm SignalR backplane (Redis) và distributed cache dùng chung. Chỉ bật Hangfire server ở một instance (Hangfire:ServerEnabled). Dashboard /hangfire chỉ mở khi môi trường là Development.

## ADR-028 — Đơn giá hợp đồng đã gồm thuế GTGT
- **Bối cảnh:** Với thuốc, VTYT, đơn giá trúng thầu được công bố là giá đã gồm VAT; hóa đơn và nghiệm thu đối chiếu theo giá này.
- **Quyết định:** `ContractItem.UnitPrice` là đơn giá đã gồm VAT. Thành tiền = SL × đơn giá. VAT được tách ra từ thành tiền: `VAT = round(thành tiền × t/(100+t))`. Giá trị trước thuế = tổng − VAT. Mọi số tiền làm tròn đến đồng (`MidpointRounding.AwayFromZero`).
- **Hệ quả:** Biểu mẫu Excel ghi rõ "đơn giá đã gồm thuế GTGT". Thuế suất mặc định là 5% (y tế) hoặc 10% (hàng hóa/dịch vụ), sửa được theo từng dòng.

## ADR-029 — Công thức "còn lại" của hợp đồng và của quyết định thầu
- **Quyết định:** Hợp đồng: `Còn lại = SL hợp đồng − SL đã nhập kho`. Các cột đã đề xuất / duyệt / gọi / nghiệm thu / thanh toán được các module Phase 5–7 cập nhật qua `ContractItem.Track`. QĐ thầu: `Còn lại = SL trúng thầu − Σ SL trong các HĐ chưa hủy` (tính khi truy vấn, không lưu). Không cho tạo HĐ vượt phần còn lại. Phụ lục không được giảm SL xuống dưới SL đã nhập kho.

## ADR-030 — Vòng rà soát NCC ↔ Kế toán bằng Track Changes
- **Quyết định:** NCC sửa trong ONLYOFFICE ở chế độ Review (bắt buộc theo dõi thay đổi) hoặc tải lên DOCX có Track Changes (tệp không có thay đổi được theo dõi sẽ bị từ chối). "Đồng ý ký" chỉ được phép khi không còn thay đổi chưa xử lý, và bước Kế toán (tùy chọn) được bỏ qua. "Gửi lại" chuyển sang Kế toán. Kế toán chấp nhận/từ chối từng thay đổi (`IDocxRevisions` trên OpenXML, theo vị trí R1..Rn của đúng phiên bản đang xem; phiên bản đã đổi thì trả 409). Sau đó Kế toán có thể "Gửi lại NCC" (trả về bước NCC rà soát, vòng lặp) hoặc "Chấp nhận & chuyển ký" (coi như bản cuối). Trước mọi bước ký, `DocumentService.PrepareForSigningAsync` chốt bản: chấp nhận các thay đổi còn lại thành phiên bản mới, rồi render PDF.
- **Hệ quả / giới hạn:** Thay đổi ngắt đoạn được xử lý bằng cách bỏ dấu đánh dấu (không gộp đoạn). Thay đổi định dạng khi từ chối thì khôi phục thuộc tính cũ. Thêm/xóa dòng bảng được hỗ trợ.

## ADR-031 — Cô lập dữ liệu NCC và `IgnoreQueryFilters`
- **Bối cảnh:** Lỗi phát hiện trong Phase 4: dùng `db.Suppliers.IgnoreQueryFilters()` trong subquery (để lấy tên NCC) đã tắt bộ lọc của **toàn bộ** truy vấn. Kết quả là NCC thấy được hợp đồng của NCC khác trong danh sách.
- **Quyết định:** Không dùng `IgnoreQueryFilters()` trong subquery của truy vấn có dữ liệu thuộc NCC. Nếu cần dữ liệu ngoài phạm vi thì tải bằng một truy vấn riêng. Có test hồi quy. NCC chỉ thấy hợp đồng từ khi bệnh viện gửi rà soát (ẩn các trạng thái `Draft`, `InReview`, `Returned`).

## ADR-032 — Tích hợp HIS outbound: mock tất định + REST cấu hình được
- **Quyết định:** `His:Provider=Mock` (mặc định) sinh danh mục tất định theo (Số QĐ + MST) từ bộ dữ liệu mẫu (thuốc / VTYT / hàng hóa – dịch vụ) và số liệu X–N–T cân đối. `His:Provider=Rest` dùng `RestHisClient`: URL có placeholder, đường dẫn mảng kết quả và ánh xạ từng trường (hỗ trợ đường dẫn có dấu chấm) đều cấu hình trong `appsettings`; retry, circuit breaker, timeout qua `Microsoft.Extensions.Http.Resilience`. Lỗi kết nối hiển thị cho người dùng là `HIS_UNAVAILABLE`, kèm gợi ý import Excel.
- **Hệ quả:** Khi tích hợp hãng HIS thật chỉ cần cấu hình, không phải sửa code (Phase 6 bổ sung chiều inbound).

## ADR-033 — Dữ liệu demo sinh bằng chính use case (đóng vai người dùng)
- **Quyết định:** `DemoProcurementSeeder` chạy các service nghiệp vụ thật dưới danh tính từng người dùng demo (`UserContextOverride` dùng AsyncLocal; ICurrentUser được đăng ký dạng factory: lấy override nếu có, không thì lấy JWT). Nhờ vậy tài liệu, luồng ký, chữ ký và nhật ký demo giống hệt dữ liệu người dùng tạo ra. Seeder chờ Gotenberg sẵn sàng trước khi chạy để không bao giờ dừng giữa chừng. Seeder là idempotent: bỏ qua khi QĐ demo đầu tiên đã tồn tại.

## ADR-034 — E2E Playwright
- **Quyết định:** Thư mục `e2e/` (Playwright 1.49, Chromium) chạy trên stack docker compose. Mỗi vai trò dùng một browser context riêng. Thao tác nghiệp vụ đi qua UI; chỉ dùng API để tải tệp DOCX về (mô phỏng NCC sửa bằng Word). CI có workflow riêng `E2E` (chạy thủ công và hằng đêm), khởi động stack không gồm ONLYOFFICE để giảm thời gian.

## ADR-035 — Phiếu đề xuất theo mảng, nhiều hợp đồng
- **Quyết định:** Mỗi phiếu đề xuất thuộc một khoa, một mảng (Thuốc / VTYT / Hóa chất…) và một kho (để lấy X–N–T). Các dòng có thể lấy từ nhiều hợp đồng gọi hàng (`UnitPriceCallOff`) còn hiệu lực trong mảng; mỗi dòng gắn với một `ContractItem`. "SL còn lại" lấy theo ADR-029 tại thời điểm lập phiếu. Khi lưu, server tự tính lại danh sách ứng viên (số lượng còn lại, X–N–T); client chỉ gửi số lượng.

## ADR-036 — Tổ trưởng sửa SL duyệt và việc ký lại
- **Bối cảnh:** Người tổng hợp ký trước tổ trưởng. Mọi tài liệu đã có chữ ký là bất biến (ADR về DocumentVersion).
- **Quyết định:** Khi tổ trưởng sửa SL duyệt, hệ thống sinh phiên bản mới và luồng ký khởi động lại, nên người tổng hợp phải ký lại (đúng spec: "có thể trả về người tổng hợp ký lại"). Bệnh viện nào không muốn ký lại thì vào "Cấu hình luồng ký", đưa bước tổ trưởng lên trước bước người tổng hợp ký. Đây là cách "theo cấu hình" của spec, không cần thêm cờ riêng.

## ADR-037 — Cột theo dõi thực hiện hợp đồng tính lại sau commit
- **Quyết định:** `ContractTracking` tính lại các cột "đã đề xuất / đã duyệt / đã gọi" từ dữ liệu nguồn (phiếu đã gửi, tổng hợp đã duyệt, đơn gọi chưa hủy/từ chối, theo SL NCC xác nhận). Observer của workflow chạy *trước* SaveChanges, nên việc tính lại được đưa vào `PostCommitWork`: interceptor thực hiện ngay sau commit trong một DI scope mới (DbContext mới, đọc dữ liệu đã commit). Các cột đã nhập kho / nghiệm thu / thanh toán do Phase 6–7 cập nhật theo cùng cơ chế.

## ADR-038 — Đơn gọi hàng
- **Quyết định:** Đơn gọi hàng không đi luồng ký số (spec chỉ yêu cầu NCC xác nhận). Mỗi đơn có PDF tạo từ mẫu `DON_GOI_HANG`. Đơn được tự tách theo NCC; một tổng hợp có thể gọi nhiều lần. Phần NCC không xác nhận (giao thiếu, từ chối, hủy đơn) được trả lại vào "còn được gọi" của tổng hợp. Email và thông báo NCC dẫn link sâu tới Cổng NCC.

## ADR-039 — Giới hạn đăng nhập và E2E
- **Quyết định:** Mặc định vẫn là 10 lần đăng nhập/phút/IP (spec §11), cấu hình qua `AUTH_LOGIN_RATE_LIMIT`. Bộ E2E đăng nhập nhiều tài khoản từ cùng một IP nên stack chạy E2E (CI và máy dev) đặt mức 1000.

## ADR-040 — "Đã nhập kho" tính từ phiếu nhập kho
- **Quyết định:** Cột đã nhập kho của `ContractItem` (và "đã giao" của đơn gọi hàng) luôn được **tính lại từ nguồn**: tổng SL các phiếu nhập trừ phiếu hoàn trả, chỉ gồm phiếu kho đã thực nhận (đang ký, bị trả lại, đã ký đủ, hoặc phiếu HIS chưa trình ký) và **không** gồm phiếu Không hợp lệ hay đã hủy. Hàng đã vào kho từ khi HIS lập phiếu nên không chờ ký đủ mới trừ "còn lại"; nghiệm thu (Phase 7) chỉ lấy phiếu đã ký đủ. Tính lại thay vì cộng dồn nên gửi trùng/hủy phiếu không làm sai số liệu.

## ADR-041 — Validation engine và điều kiện ký (IWorkflowGuard)
- **Quyết định:** Mỗi rule là một class `IReceiptRule` thuần (không truy cập DB); `ReceiptValidationLoader` nạp dữ liệu (hợp đồng, SL đã nhập của phiếu khác, đơn gọi hàng, hóa đơn trùng, tham số) rồi chạy cả 10 rule. Kết quả lưu trên phiếu (JSONB), mức tổng = mức nặng nhất. Workflow engine có thêm điểm mở rộng `IWorkflowGuard`, được hỏi trước khi duyệt/ký và trả về trong `WorkflowDto.gate` để UI hiển thị:
  - **Không hợp lệ** → chặn ký ở mọi bước (chỉ có thể Trả lại).
  - **Thiếu hóa đơn** → chặn từ bước Người giao hàng (NCC) trở đi (spec: NCC bổ sung hóa đơn ở bước này); các bước trước không bắt lý do vì thiếu hóa đơn.
  - **Cảnh báo khác** → mỗi người ký phải nhập lý do; lý do lưu vào ý kiến của bước (hiện trong lịch sử luồng ký). Chọn "mỗi người ký" thay vì "một lần" để mỗi chữ ký đều có xác nhận chấp nhận rủi ro.
  Ký hàng loạt không kèm lý do nên phiếu có cảnh báo phải ký từ màn hình chi tiết.

## ADR-042 — API HIS inbound: HMAC, chống replay, nhật ký, tài khoản dịch vụ
- **Quyết định:** Chuỗi ký `METHOD\nPATH_AND_QUERY\nX-Timestamp\nhex(SHA256(body))`, HMAC-SHA256 bằng secret riêng của từng client (`HisInbound:Clients`), timestamp ±5 phút. Chống phát lại bằng cache chữ ký đã dùng cho request **ghi**; GET là chỉ đọc nên không chặn (tránh từ chối hai lần đọc giống nhau trong cùng giây). Whitelist IP hỗ trợ CIDR; rate limit theo API key. Một resource filter ghi `IntegrationLog` cho **mọi** lời gọi, kể cả bị từ chối, trong unit of work riêng (log không commit nửa nghiệp vụ lỗi, nghiệp vụ lỗi vẫn có log). "Gửi lại" xử lý lại body đã lưu của lần nhận phiếu lỗi.
- **Người gửi luồng ký** của phiếu từ HIS là tài khoản dịch vụ `his.integration` (tự tạo, trạng thái ngừng hoạt động nên không đăng nhập được). Workflow engine có `StartForSystemAsync` / `ResubmitForModuleAsync` cho module khởi động luồng không qua người dùng.
- **Idempotent:** theo `HisReceiptId` (unique index) + SHA-256 nội dung: trùng nội dung → `Duplicate`; khác nội dung và chưa ai ký → cập nhật, sinh phiên bản PDF mới (phiếu bị trả lại thì tự trình ký lại); đã có chữ ký → 409, điều chỉnh bằng phiếu hoàn trả.

## ADR-043 — Hóa đơn bổ sung sau chữ ký đầu tiên
- **Bối cảnh:** Thủ kho ký trước, NCC thường bổ sung hóa đơn ở bước sau; bản PDF đã có chữ ký là bất biến (spec §5.7).
- **Quyết định:** Thông tin hóa đơn (ký hiệu, số, ngày, tiền) lưu trên phiếu, file XML/PDF hóa đơn lưu kèm phiếu (S3, SHA-256) và đi vào hồ sơ lưu trữ. PDF phiếu chỉ sinh lại khi chưa ai ký; sau đó giữ nguyên PDF và không bắt ký lại cả luồng. Parser XML theo NĐ 123/2020 + TT 78/2021 (`KHMSHDon` + `KHHDon`, `SHDon`, `NLap`, `TToan/TgTTTBSo`…), bỏ qua namespace, cấm DTD (chống XXE).

## ADR-044 — Kéo phiếu định kỳ và HIS giả lập
- **Quyết định:** `IHisClient.GetWarehouseReceiptsAsync` + job Hangfire `his-receipt-pull` (bật/tắt và cron cấu hình, mặc định 15 phút, hỏi 3 ngày gần nhất). Kéo trùng không sao vì cùng quy trình idempotent với push. Bộ chuyển đổi REST yêu cầu hãng trả đúng schema phiếu của cổng (không ánh xạ từng trường như danh mục vì cấu trúc lồng nhiều cấp). HIS giả lập giữ phiếu trong hàng đợi bộ nhớ; endpoint dev `simulate-receipt` sinh phiếu thực tế cho hợp đồng hiệu lực (SL theo đơn gọi hàng còn mở) với 3 kịch bản Hợp lệ / Cảnh báo / Không hợp lệ, chỉ bật khi `HisInbound:DevEndpoints=true`. Dữ liệu demo cũng sinh bằng chính bộ giả lập này.

## ADR-045 — Phiếu lập trên cổng và phiếu ảo
- **Quyết định:** Tổ Mua sắm (quyền `receipt.manage`) lập phiếu nhập/hoàn trả trên cổng khi HIS không gửi được: chọn hợp đồng hiệu lực, mặt hàng lấy từ hợp đồng (ánh xạ chính xác), chạy cùng validation engine; phiếu Không hợp lệ không gửi ký được. Phiếu **ảo** (`VirtualPortal`) cho hợp đồng không nhập kho: có PDF "Phiếu xác nhận khối lượng — không qua kho", bấm "Xác nhận khối lượng" là hoàn thành, không qua luồng ký, rồi được dùng cho nghiệm thu như phiếu thường.

## ADR-046 — Nghiệm thu: giữ chỗ phiếu, job theo ngày cấu hình
- **Quyết định:** Phiếu nhập kho được "giữ" cho biên bản nghiệm thu ngay khi lập (cả bản nháp) để hai biên bản không lấy trùng phiếu; hủy biên bản thì nhả lại. Job `monthly-acceptance` chạy mỗi ngày 01:00 giờ Việt Nam và chỉ làm việc khi hôm nay đúng ngày cấu hình (`acceptance.job_day_of_month`, giới hạn 1–28), nên đổi ngày trên màn hình tham số có hiệu lực ngay, không phải sửa lịch Hangfire. Job lập bản nháp cho các phiếu đã ký đến hết tháng trước (kể cả phiếu tháng cũ còn sót), theo từng hợp đồng còn hiệu lực. Giá trị nghiệm thu = tổng giá trị phiếu (đã gồm VAT), phiếu hoàn trả mang dấu âm.

## ADR-047 — 08a, lũy kế và đề nghị thanh toán
- **Quyết định:** Mỗi 08a có "lần thanh toán thứ n" theo hợp đồng. Lũy kế đến kỳ trước = tổng giá trị kỳ này của các 08a trước đó (chưa hủy) của cùng hợp đồng, tính theo cả giá trị lẫn khối lượng từng mặt hàng (bảng khối lượng của Mẫu 08a). Giá trị đề nghị thanh toán = giá trị kỳ này − thu hồi tạm ứng. Đề nghị thanh toán là một tài liệu riêng (mẫu `DE_NGHI_THANH_TOAN`, luồng `WF_PAYMENT_REQUEST` NCC → Giám đốc) sinh cùng lúc và gửi ký cùng lúc với 08a, nhưng gắn vào cùng bản ghi 08a để đối chiếu một chỗ. BB nghiệm thu được giữ chỗ khi lập 08a và đánh dấu "đã đưa vào thanh toán" khi 08a ký xong (spec §6.5).
- **Ủy nhiệm chi** không phải tài liệu ký số (chứng từ ngân hàng/Kho bạc): lưu số, ngày, số tiền, file scan; được thêm sau khi ký; tổng UNC không vượt số đề nghị; đủ tiền thì 08a "Đã thanh toán" và số lượng của các phiếu thuộc nó tính vào cột "đã thanh toán" của hợp đồng.

## ADR-048 — Thanh lý và chốt công nợ
- **Quyết định:** Chỉ lập thanh lý khi hợp đồng còn hiệu lực, có ít nhất một 08a đã ký và không còn 08a đang lập/trình ký; phiếu hoặc nghiệm thu chưa xử lý chỉ là cảnh báo (người dùng quyết định thanh lý phần đã thực hiện). Giá trị thực hiện = tổng giá trị kỳ này các 08a đã ký; đã thanh toán = UNC + thu hồi tạm ứng. Khoản còn lại được "đánh dấu đã thanh toán" bằng một dòng chốt công nợ (nội dung, số tiền, chứng từ, file, người đánh dấu, thời điểm) — không sửa số liệu 08a. Khi biên bản thanh lý ký xong: hợp đồng `Liquidated` (`Contract.Liquidate`), các 08a có dòng chốt công nợ chuyển "Đã thanh toán", và mọi phiếu nhập mới của hợp đồng bị đánh giá Không hợp lệ (`CONTRACT_CLOSED`).

## ADR-049 — Hồ sơ lưu trữ dựng từ dữ liệu nghiệp vụ, ZIP kèm mục lục và đối chiếu hash
- **Quyết định:** Cây hồ sơ không lưu riêng mà dựng lại mỗi lần từ hợp đồng: tài liệu có `ContractId`, cộng phiếu đề xuất / tổng hợp / đơn gọi hàng có dòng thuộc hợp đồng (tài liệu dùng chung nhiều hợp đồng), phiếu nhập kho, biên bản thanh toán và các tệp đính kèm (hóa đơn XML/PDF, UNC, chứng từ chốt công nợ, văn bản QĐ thầu). Mỗi tài liệu đưa vào bản PDF hiện hành (bản đã ký nếu có). Mã tệp gửi ra trình duyệt là token mờ, và API chỉ trả link cho token thuộc đúng cây của hợp đồng (không thể dùng để đọc tệp khác).
- ZIP được tạo theo yêu cầu (đủ cho quy mô một hợp đồng); khi chép từng tệp, SHA-256 được tính lại và so với giá trị ghi lúc tải lên — kết quả "Khớp/LỆCH" in trong `muc_luc.pdf` (mẫu dựng bằng DocxBuilder + template engine + Gotenberg). Việc xem/tải hồ sơ được ghi nhật ký (spec §5.7). Hồ sơ lưu trữ chỉ dành cho bệnh viện (quyền `archive.*`).

## ADR-050 — Biểu đồ dashboard không dùng thư viện
- **Quyết định:** Spec không chỉ định thư viện biểu đồ; dashboard chỉ cần biểu đồ cột theo tháng nên dùng component SVG nhỏ `BarChart` (tooltip số tiền định dạng VN) thay vì thêm một thư viện biểu đồ vài trăm KB. Có thể thay bằng thư viện khác sau mà không đổi API.

## ADR-051 — Định nghĩa số liệu báo cáo
- **Quyết định:** Thực hiện HĐ tính theo các cột theo dõi của mặt hàng × đơn giá (đã gọi / đã nhập / đã nghiệm thu / đã thanh toán); % thực hiện = đã nhập kho / giá trị HĐ. Công nợ NCC: còn phải trả = đã nghiệm thu (biên bản đã ký) − đã thanh toán (UNC + chốt công nợ khi thanh lý) − thu hồi tạm ứng. Giá trị thanh toán theo kỳ tính theo ngày ủy nhiệm chi. Tiến độ ký: thời gian một bước = lúc xử lý − lúc bước được kích hoạt; tồn đọng nhóm theo đối tượng được giao (vai trò, người dùng, NCC, người lập).

## ADR-052 — Hoàn thiện: index tìm kiếm, kiểm tra toàn vẹn, header bảo mật, cổng coverage
- **Index hiệu năng:** mọi bảng danh sách có index `created_at` (sắp xếp mặc định mới nhất trước). Ô tìm kiếm dùng `ILIKE '%…%'`, nên cài extension `pg_trgm` và tạo index GIN trigram cho các cột tìm nhiều nhất: số/tên hợp đồng, tên/mã hàng hóa, tên NCC, tiêu đề tài liệu, hành động nhật ký. Không đổi sang full-text search, vì người dùng tìm theo một đoạn số hợp đồng hoặc mã hàng, mà full-text không khớp được kiểu đó. Đo trên máy dev với 100.000 mặt hàng: trang đầu ~0,26 s, trang 500 ~0,35 s, tìm kiếm ~0,45 s, đều < 500 ms (spec §12).
- **Kiểm tra toàn vẹn:** job Hangfire `integrity-check` chạy lúc 02:00 giờ Việt Nam. Job tải lại mọi tệp có SHA-256 đã lưu (phiên bản tài liệu DOCX/PDF, phiên bản mẫu biểu, đính kèm QĐ thầu), băm lại, ghi kết quả vào `IntegrityCheckRun` kèm danh sách tệp lệch hoặc không đọc được. Nếu có lỗi, hệ thống gửi thông báo `IntegrityAlert` tới admin. Mỗi lần kiểm tra là một bản ghi riêng nên còn lại lịch sử. Admin bấm "Kiểm tra ngay" được, nhưng không chạy được hai lần cùng lúc. Tệp nghiệp vụ khác (hóa đơn, UNC, chứng từ) được đối chiếu khi đóng gói hồ sơ ZIP (ADR-049).
- **Header bảo mật:** gom vào `nginx-security-headers.conf` và include ở cả cấp server lẫn từng `location`, vì nginx bỏ `add_header` kế thừa khi location có `add_header` riêng. Gồm `X-Content-Type-Options`, `X-Frame-Options SAMEORIGIN`, `Referrer-Policy`, `Permissions-Policy`, CSP `frame-ancestors 'self'; object-src 'none'; base-uri 'self'`, COOP. Chưa dùng CSP `script-src` chặt vì ONLYOFFICE nhúng script từ origin riêng. HTTPS/HSTS đặt ở reverse proxy biên (DEPLOY.md).
- **Cổng coverage:** CI thu Cobertura của cả 3 project test, gộp theo dòng (lấy số hit lớn nhất) và chỉ tính Domain + Application, fail khi < 70% (`scripts/coverage.sh|.ps1`). Hiện tại khoảng 75,7%.

## ADR-053 — Tranh chấp và luật "một … một": khóa DB + chỉ mục duy nhất
- **Bối cảnh:** AUDIT-1 §10.4/§10.6. Kiểm bằng `AnyAsync` rồi `Insert`, hoặc cộng dồn số lượng trên dòng con, sẽ vỡ khi hai yêu cầu đến cùng lúc; xmin của gốc aggregate không được kiểm khi chỉ dòng con thay đổi.
- **Quyết định:** (1) Thao tác đọc–kiểm–ghi số lượng (gọi hàng, xác nhận/hủy đơn, lập phụ lục, nhận phiếu HIS, đổi hóa đơn) chạy trong `IAppDbContext.SerializedAsync(keys)`: một transaction giữ `pg_advisory_xact_lock` theo khóa nghiệp vụ (phiếu tổng hợp, mặt hàng HĐ, HĐ, mã phiếu HIS, MST NCC), khóa sắp thứ tự để tránh deadlock; dữ liệu đọc **sau** khi giữ khóa. (2) Mọi luật "một … một" có chỉ mục duy nhất (có điều kiện) làm chốt chặn cuối; `UniqueConstraints` ánh xạ tên chỉ mục → mã lỗi tiếng Việt, test quét bắt buộc mọi chỉ mục duy nhất trong DB có câu báo. (3) Thông báo/email chỉ gửi khi transaction commit (interceptor bắt `TransactionCommitted`). (4) Số chứng từ cấp bằng một câu `INSERT … ON CONFLICT DO UPDATE … RETURNING` — không còn xung đột khi lập song song; đánh đổi: yêu cầu lỗi sau khi đã cấp số để lại khoảng trống trong dãy số (không bao giờ trùng).
- **Không chọn:** Serializable isolation cho toàn bộ (retry khắp nơi), hay hàng đợi đơn luồng (đắt khi chạy nhiều instance).

## ADR-054 — Phạm vi dữ liệu tập trung (`DataScope`)
- **Quyết định:** Phạm vi Cơ sở (gồm cơ sở của phạm vi Khoa/Phòng) và Mảng (theo nhóm hàng của HĐ) tính ở một service. Mọi thứ gắn với HĐ lọc qua `ContractIdsAsync()` (sub-query), nên đếm và lấy dòng dùng cùng một truy vấn. HĐ không gắn cơ sở là của mọi cơ sở. Phiếu chưa ghép HĐ chỉ hiện với người không giới hạn Mảng. Chi tiết ngoài phạm vi trả 404, trừ người tham gia luồng ký của tài liệu. Người ký thay hành động trong phạm vi **và** quyền của người ủy quyền.

## ADR-055 — Quyền theo hành động ký, X-Forwarded-For, chống replay
- **Quyền ký:** `DocumentPermissions.Act(loại tài liệu, hành động)`: HĐ/PL ký số `contract.sign`, ký nháy `contract.initial`, rà soát `contract.review|accountant_review`, NCC rà soát `contract.supplier_review`; đóng dấu mọi tài liệu `contract.seal` (quyền đóng dấu duy nhất). Tài liệu khác chỉ cần `signing.sign` + được giao bước.
- **Proxy:** chỉ tin X-Forwarded-* từ loopback và `ForwardedHeaders:KnownProxies/KnownNetworks` (compose: `TRUSTED_PROXY_NETWORK`, mặc định 172.16.0.0/12).
- **Replay HMAC:** bảng `used_request_signatures` (khóa chính = chữ ký), giữ 2× độ lệch giờ cho phép.
- **Tệp tải lên:** lưu với media type/đuôi suy từ nội dung (magic bytes), không theo khai báo của client.

