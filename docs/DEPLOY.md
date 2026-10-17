# Triển khai BlueProcure (môi trường thật)

Tài liệu dành cho bộ phận CNTT của bệnh viện. Môi trường phát triển/demo xem `README.md`.

## 1. Kiến trúc và yêu cầu

```
Internet / mạng nội bộ ──HTTPS──► Reverse proxy (TLS) ──► web (nginx, SPA + /api) ──► api (.NET 10)
                                                                                     ├─ PostgreSQL 16
                                                                                     ├─ Kho tệp S3 (SeaweedFS / MinIO / S3 tương thích)
                                                                                     ├─ Gotenberg (DOCX → PDF)
                                                                                     ├─ ONLYOFFICE Docs (soạn thảo trực tuyến)
                                                                                     └─ SMTP (email thông báo)
HIS ──HTTPS + HMAC──► /api/integration/his/*  (xem docs/HIS_INTEGRATION.md)
```

| Thành phần | Khuyến nghị tối thiểu |
|---|---|
| Máy chủ ứng dụng | 4 vCPU, 8 GB RAM (ONLYOFFICE chiếm ~2–3 GB), Docker Engine 24+ và Docker Compose v2 |
| PostgreSQL | 16, ổ SSD, sao lưu hằng ngày (mục 6) |
| Kho tệp | ≥ 200 GB, tăng theo số hồ sơ (mỗi hợp đồng có các bản PDF đã ký) |
| Tên miền + chứng chỉ TLS | VD `muasam.benhvien.vn` |

Toàn bộ dịch vụ trong `docker-compose.yml` chỉ mở cổng trên `127.0.0.1`. Chỉ reverse proxy (nginx/Traefik/IIS ARR của bệnh viện) được phép ra ngoài, và chỉ cổng 443.

## 2. Chuẩn bị cấu hình

```bash
git clone <repo> /opt/blueprocure && cd /opt/blueprocure
cp .env.example .env
mkdir certs   # fullchain.pem + privkey.pem cho cả SERVER_NAME và OFFICE_SERVER_NAME (SAN hoặc wildcard)
```

Production dùng **`docker-compose.prod.yml`**, **không dùng** `docker-compose.override.yml` (file override chỉ dành cho phát triển). Trong file production:
- Mọi bí mật là **bắt buộc**: thiếu biến nào thì compose dừng và nêu tên biến đó.
- API **từ chối khởi động** khi còn cờ dev hoặc bí mật mặc định (`ProductionGuard`, liệt kê mọi vấn đề trong log).

Biến cần đặt trong `.env`:

| Biến | Giá trị production |
|---|---|
| `POSTGRES_PASSWORD` | mật khẩu mạnh (≥ 12 ký tự) |
| `S3_ACCESS_KEY`, `S3_SECRET_KEY` | tài khoản S3 **của ứng dụng**: chỉ đọc / ghi / liệt kê bucket của mình, không tạo hay xóa bucket |
| `S3_ADMIN_ACCESS_KEY`, `S3_ADMIN_SECRET_KEY` | tài khoản S3 quản trị: tạo bucket (container `storage-init`) và sao lưu / khôi phục |
| `ONLYOFFICE_JWT_SECRET` | chuỗi ngẫu nhiên ≥ 32 ký tự |
| `AUTH_SIGNING_KEY` | chuỗi ngẫu nhiên ≥ 32 byte (`openssl rand -base64 48`) |
| `SEED_ADMIN_PASSWORD` | mật khẩu tạm của tài khoản `admin` đầu tiên (bắt buộc đổi ở lần đăng nhập đầu) |
| `SERVER_NAME`, `PUBLIC_BASE_URL` | `muasam.benhvien.vn`, `https://muasam.benhvien.vn` |
| `OFFICE_SERVER_NAME`, `ONLYOFFICE_PUBLIC_URL` | `office.benhvien.vn`, `https://office.benhvien.vn` |
| `TLS_CERT_DIR` | thư mục chứng thư TLS (mặc định `./certs`) |
| `HIS_PROVIDER`, `HIS_*`, `HIS_INBOUND_API_KEY`, `HIS_INBOUND_SECRET` | theo mục 5 |
| `SIGNING_DEFAULT_PROVIDER` | nhà cung cấp chữ ký số thật (mục 4; mặc định `USB_TOKEN`) |
| `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME`, `SMTP_PASSWORD`, `SMTP_FROM` | máy chủ email (cũng sửa được ở **Quản trị → Cấu hình email & ký số**) |
| `DOCKER_SUBNET` | dải mạng nội bộ của compose (mặc định `172.28.200.0/24`; đổi nếu trùng mạng LAN) |

File production tự cố định các giá trị sau, không cần đặt: `ASPNETCORE_ENVIRONMENT=Production`, tắt dữ liệu demo, tắt reset, tắt chữ ký thử nghiệm, tắt endpoint giả lập HIS, tắt trang Hangfire. `mailpit` không chạy. Kho tệp và ONLYOFFICE không mở cổng ra ngoài.

## 3. Khởi động

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
docker compose -f docker-compose.yml -f docker-compose.prod.yml ps     # chờ mọi dịch vụ (healthy)
curl -s https://muasam.benhvien.vn/health/ready
```

Thứ tự khởi động:
1. postgres, storage, redis, gotenberg;
2. `storage-init` tạo bucket bằng tài khoản quản trị rồi thoát;
3. api (migrate, seed, kiểm tra cấu hình Production);
4. web;
5. `tls`.

Container `tls` (nginx) là cửa vào duy nhất (cổng 80/443):
- HTTP chuyển sang HTTPS;
- TLS 1.2/1.3;
- HSTS 1 năm;
- `SERVER_NAME` → cổng web; `OFFICE_SERVER_NAME` → ONLYOFFICE (kể cả websocket).

Lần đầu: đăng nhập `admin` bằng `SEED_ADMIN_PASSWORD` → đổi mật khẩu → bật xác thực 2 lớp → khai báo **Thông tin bệnh viện, Cơ sở, Khoa/phòng, Kho (kèm mã kho HIS), Nhà cung cấp, Người dùng và vai trò**. Mẫu biểu, luồng ký và tham số hệ thống có sẵn mặc định và chỉnh được trong menu Quản trị.

Chỉ migrate + seed rồi thoát (VD trong pipeline triển khai): `docker compose -f docker-compose.yml -f docker-compose.prod.yml run --rm api --seed-only`.

Diễn tập 17/10/2026 (stack production dưới tên project riêng, chứng thư tự ký):
- Với cấu hình dev, API ở Production dừng và liệt kê 12 vấn đề.
- Với `.env` đầy đủ, mọi dịch vụ đều `(healthy)`.
- HTTP trả 301 sang HTTPS; HTTPS có HSTS.
- Cookie phiên có `secure`; IP trong nhật ký là client, không phải proxy.
- Không có dữ liệu demo; admin bị buộc đổi mật khẩu.
- `/hangfire` và endpoint giả lập HIS trả 404.
- Tài khoản S3 của ứng dụng không tạo / liệt kê / xóa được bucket.

### Reverse proxy riêng của bệnh viện

Nếu bệnh viện đã có reverse proxy / cân bằng tải phía trước thì bỏ container `tls` (hoặc đổi `TLS_HTTP_PORT` / `TLS_HTTPS_PORT`). Proxy đó chuyển tiếp tới cổng 443 của `tls` hoặc thẳng tới container `web`, kèm `X-Forwarded-For` và `X-Forwarded-Proto`. Đặt `TRUSTED_PROXY_NETWORK` là dải mạng của proxy (không bao giờ `0.0.0.0/0`). Ví dụ cấu hình nginx tương đương:

```nginx
server {
    listen 443 ssl http2;
    server_name muasam.benhvien.vn;
    ssl_certificate     /etc/ssl/muasam.crt;
    ssl_certificate_key /etc/ssl/muasam.key;
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
    client_max_body_size 50m;

    location / {
        proxy_pass http://127.0.0.1:47080;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto https;
        proxy_set_header Upgrade $http_upgrade;      # SignalR (thông báo realtime)
        proxy_set_header Connection "upgrade";
        proxy_read_timeout 300s;
    }
}
```

API chỉ tin `X-Forwarded-*` từ các proxy trong `TRUSTED_PROXY_NETWORK`; mặc định là `DOCKER_SUBNET`, tức các container `tls` và `web`. API đi lần theo cả chuỗi proxy tin cậy, nên IP dùng cho giới hạn đăng nhập, whitelist HIS và nhật ký là IP của client.

**Tải tệp:** link tải là `/api/files/{token}` trên chính tên miền của cổng: ký bằng Data Protection, sống ngắn (5 phút), chỉ cấp sau khi kiểm tra quyền, API tự đọc tệp từ kho. Vì vậy **không** cần mở cổng kho lưu trữ ra mạng. Chỉ khi có tên miền S3 công khai (CDN, S3 của nhà cung cấp) thì mới đặt `STORAGE_PUBLIC_ENDPOINT`; khi đó link là presigned URL S3 trỏ tới tên miền đó.

Container `web` tự gắn các header bảo mật: `X-Content-Type-Options`, `X-Frame-Options`, `Content-Security-Policy: frame-ancestors`, `Referrer-Policy`, `Permissions-Policy`. HSTS do `tls` (hoặc proxy của bệnh viện) gắn, vì chỉ ở đó mới có TLS.

## 4. Chữ ký số

| Nhà cung cấp | `SIGNING_DEFAULT_PROVIDER` | Cấu hình |
|---|---|---|
| USB token (plugin ký trên máy người dùng) | `USB_TOKEN` | cài plugin ký số của CA trên máy trạm |
| VNPT SmartCA | `VNPT_SMARTCA` | `Signing__VnptSmartCa__BaseUrl`, `__ClientId`, `__ClientSecret` |
| Viettel MySign | `VIETTEL_MYSIGN` | `Signing__ViettelMySign__…` |
| FPT CA | `FPT_CA` | `Signing__FptCa__…` |
| Dấu thời gian (TSA, khuyến nghị) | — | `SIGNING_TSA_URL` → `Signing__TsaUrl` |

**Đăng ký ký số từ xa:** bệnh viện ký hợp đồng tích hợp với nhà cung cấp (VNPT SmartCA / Viettel MySign / FPT CA) để được cấp môi trường thử (UAT) và thật. Nhà cung cấp cấp `BaseUrl`, `ClientId`, `ClientSecret` của ứng dụng và yêu cầu khai báo địa chỉ IP ra của máy chủ. Nên thử trên UAT trước, rồi đổi sang thông tin môi trường thật. Từng người ký phải có thuê bao chứng thư số từ xa của chính nhà cung cấp đó, cài ứng dụng xác nhận trên điện thoại; Quản trị khai báo số serial / tổ chức cấp / hạn chứng thư trong **Quản trị → Người dùng**. Khi ký, cổng tạo yêu cầu, người ký xác nhận trên điện thoại, giao diện tự kiểm tra trạng thái đến khi có kết quả. Lớp `RemoteCaSigningProvider` (`backend/src/BlueContract.Infrastructure/Signing/SigningProviders.cs`) cần được chỉnh theo đặc tả API chính thức mà nhà cung cấp gửi khi ký hợp đồng.

**Dấu thời gian:** khi đặt `SIGNING_TSA_URL` (máy chủ cấp dấu thời gian RFC 3161 của CA hoặc Bộ TT&TT), mọi chữ ký được gắn dấu thời gian đáng tin cậy (PAdES B-T); trang Xác minh hiện "Dấu thời gian: Có". Nếu TSA không trả lời trong 20 giây, việc ký bị từ chối với thông báo rõ ràng (không ký thiếu dấu thời gian). Kiểm tra bằng cách ký thử một tài liệu sau khi cấu hình.

Ký nháy (`Initial`) mặc định dùng dấu nháy nội bộ không cần CA (`Signing__InitialRequiresCa=false`). Tắt các nhà cung cấp giả lập bằng `SIGNING_ENABLE_MOCK=false`. Chứng thư số của từng người được khai báo trong **Quản trị → Người dùng**.

## 5. Tích hợp HIS

- **HIS gọi cổng:** cấp cho hãng HIS `HIS_INBOUND_API_KEY` + `HIS_INBOUND_SECRET` (không gửi secret qua email thường), giới hạn IP bằng `HIS_INBOUND_ALLOWED_IPS` (hỗ trợ CIDR), gửi kèm `docs/HIS_INTEGRATION.md`. Đồng bộ giờ máy chủ (NTP) vì chữ ký chỉ hợp lệ ±5 phút.
- **Cổng gọi HIS:** `HIS_PROVIDER=Rest`, `HIS_BASE_URL`, `HIS_API_KEY`; đường dẫn và ánh xạ trường trong `His__ItemsPath`, `His__StockPath`, `His__ReceiptsPath`, `His__ItemFields__*`… (xem `appsettings.json`).
- **Kéo phiếu định kỳ:** `HIS_PULL_ENABLED=true`, `HIS_PULL_CRON` (giờ UTC).
- Theo dõi tại **Quản trị → Tích hợp HIS** (nhật ký, gửi lại, kéo phiếu ngay).

## 6. Sao lưu và khôi phục

**Tự động:** container `backup` (cùng `docker compose up`) sao lưu **hằng ngày lúc `BACKUP_TIME`** (giờ Việt Nam, mặc định 01:30). Mỗi lần ghi vào `BACKUP_DIR/<yyyymmdd-HHMMSS>/` gồm `db.dump` (`pg_dump` dạng custom) và `files/` (toàn bộ bucket tệp), giữ `BACKUP_KEEP` bản gần nhất (mặc định 30). Một bản chỉ hiện ra khi đã sao lưu xong (thư mục `.partial` được đổi tên ở bước cuối). Thời điểm thành công gần nhất ghi trong `BACKUP_DIR/last-success`; nhật ký xem bằng `docker compose logs backup`. Đặt `BACKUP_TIME=` (rỗng) để tắt. Thư mục `BACKUP_DIR` cần được chép ra máy / kho lưu trữ khác (rsync, ổ mạng) theo quy trình của bệnh viện.

**Sao lưu ngay:** `scripts/backup.sh` / `scripts\backup.ps1`.

**Khôi phục:** `scripts/restore.sh <yyyymmdd-HHMMSS>` / `scripts\restore.ps1 -Stamp <yyyymmdd-HHMMSS>`. Script hỏi xác nhận (gõ `KHOI PHUC`, hoặc thêm `--yes` / `-Yes`), rồi:
1. dừng API;
2. ngắt kết nối và tạo lại cơ sở dữ liệu từ `db.dump`;
3. làm bucket **giống hệt** bản sao lưu (`sync --delete`: tệp tạo sau thời điểm sao lưu bị xóa);
4. chạy lại API.

Sau đó vào **Quản trị → Kiểm tra toàn vẹn → Kiểm tra ngay**: mọi tệp phải "khớp" SHA-256.

Diễn tập ngày 17/10/2026 trên bộ dữ liệu demo:
- Sao lưu 635 MB (83 HĐ, 509 phiên bản tài liệu, 3.455 tệp) mất khoảng 30 giây.
- Sau thời điểm sao lưu, thêm một cơ sở và một tệp lạ.
- Khôi phục mất khoảng 2 phút. Kết quả: số liệu trùng khớp, cơ sở và tệp thêm sau đều mất, kiểm tra toàn vẹn 993 tệp không sai, không thiếu.

Nên diễn tập lại mỗi quý trên máy thử.

### Bản cài cũ dùng MinIO (trước ADR-021)
Kho tệp đã chuyển từ container MinIO sang SeaweedFS (ADR-021). Định dạng dữ liệu trên đĩa của hai bên khác nhau, nên **không** gắn lại volume `minio-data` cũ cho `storage` được. Dữ liệu chuyển qua bản sao lưu:
1. Trên bản cũ (vẫn còn container `minio`), chạy `scripts/backup.sh` / `scripts\backup.ps1` của bản cũ. Kết quả là `backups/<yyyymmdd-HHMMSS>/` gồm `db.dump` và thư mục `minio/` (các tệp thường, chép bằng `mc mirror`).
2. Đổi tên thư mục `minio/` thành `files/`, là tên mà `restore-once.sh` của bản mới đọc.
3. Cập nhật mã nguồn, thêm vào `.env` các biến `S3_*` (xem `.env.example`; các biến `MINIO_*` không còn dùng), rồi `docker compose up -d`.
4. Chạy `scripts/restore.sh <yyyymmdd-HHMMSS>` / `scripts\restore.ps1 -Stamp <yyyymmdd-HHMMSS>`, rồi **Kiểm tra toàn vẹn → Kiểm tra ngay**: mọi tệp phải khớp SHA-256.
5. Khi đã chắc chắn, xóa container và volume MinIO cũ: `docker volume rm <project>_minio-data`.

Nếu bệnh viện muốn dùng tiếp một máy chủ MinIO (hoặc dịch vụ S3 khác) có sẵn thay cho SeaweedFS: giữ nguyên dữ liệu ở đó. Trong một tệp compose bổ sung, đặt `Storage__Endpoint` (dạng `host:port`), `Storage__UseSsl`, `Storage__AccessKey`, `Storage__SecretKey`, `Storage__Bucket` cho `api`, đặt `S3_ENDPOINT` và tài khoản đọc toàn bucket cho `backup`, rồi bỏ phụ thuộc vào `storage` / `storage-init` của hai service này. Bucket phải được tạo sẵn. Mã chỉ đi qua giao diện S3, không phụ thuộc máy chủ cụ thể.

## 7. Vận hành

| Việc | Cách làm |
|---|---|
| Sức khỏe hệ thống | `GET /health/ready` (PostgreSQL, kho tệp, Gotenberg) — gắn vào hệ thống giám sát. Mọi container có healthcheck: `docker compose ps` hiện `(healthy)`. api dùng `dotnet BlueContract.Api.dll --health-check` (image không có curl); web dùng `/nginx-health`. api chờ gotenberg, web chờ api; seed demo tự chờ Gotenberg |
| Nhật ký ứng dụng | `docker compose logs api`; tùy chọn Seq (`--profile seq`) |
| Nhật ký hoạt động người dùng | Quản trị → Nhật ký hoạt động (chỉ ghi thêm, có trigger chặn sửa/xóa) |
| Toàn vẹn tài liệu | job hằng ngày lúc `job.integrity_check_hour` (mặc định 02:00, sửa ở Tham số hệ thống); cảnh báo gửi email/thông báo cho quản trị khi có tệp lệch hoặc mất |
| Nghiệm thu tự động | job hằng ngày lúc `job.acceptance_hour` (mặc định 01:00), chỉ lập biên bản đúng ngày `acceptance.job_day_of_month` |
| Nhắc việc quá SLA | job mỗi `job.workflow_reminder_minutes` phút (mặc định 60) |
| Cập nhật phiên bản | `git pull && docker compose up -d --build api web` (migration chạy tự động; sao lưu trước khi cập nhật) |

## 8. Bảo mật — danh sách kiểm tra

- [ ] Chạy bằng `docker-compose.prod.yml` (không có override phát triển); API khởi động được, tức `ProductionGuard` không còn báo vấn đề nào.
- [ ] Chỉ cổng 80/443 của `tls` mở ra ngoài (các container khác không publish cổng).
- [ ] HTTPS + HSTS (container `tls` hoặc proxy của bệnh viện); `Cors__Origins` chỉ chứa tên miền của cổng.
- [ ] Tài khoản S3 của ứng dụng tách khỏi tài khoản quản trị S3.
- [ ] Tài khoản quản trị bật xác thực 2 lớp; chính sách mật khẩu và khóa tài khoản sau 5 lần sai (mặc định) giữ nguyên.
- [ ] Whitelist IP cho API HIS; secret HIS cấp riêng cho từng môi trường.
- [ ] Sao lưu hằng ngày được chép ra ngoài máy chủ; đã diễn tập khôi phục.
- [ ] Theo dõi cảnh báo "Kiểm tra toàn vẹn" và "Nhật ký tích hợp".

## 9. Mở rộng

**Nhiều instance API:** `docker compose up -d --scale api=N api`, rồi `docker compose restart web` để nginx nhận đủ địa chỉ các instance. Redis (container `redis`, biến `REDIS_CONFIGURATION`) giữ trạng thái dùng chung: SignalR và phiên ký. Kiểm tra bằng `scripts/scale-check.ps1` / `.sh`. Nếu đặt `REDIS_CONFIGURATION=` (rỗng) thì chỉ chạy được **một** instance.

API không lưu trạng thái phiên (JWT + refresh token trong cookie) nên chạy được nhiều bản sau load balancer. Khi chạy nhiều bản (ADR-027): chỉ một bản bật `Hangfire__ServerEnabled=true`, cấu hình backplane SignalR (Redis) để thông báo realtime đến mọi bản, và dùng distributed cache chung (Redis) cho trạng thái ký số từ xa / USB token.
