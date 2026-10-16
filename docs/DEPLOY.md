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
```

Sửa `.env`. **Bắt buộc đổi mọi giá trị bí mật** — các giá trị mẫu chỉ để demo:

| Biến | Giá trị production |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` (tắt trang Hangfire, tắt endpoint dev) |
| `POSTGRES_PASSWORD` | mật khẩu mạnh |
| `S3_ACCESS_KEY`, `S3_SECRET_KEY` | khóa riêng |
| `ONLYOFFICE_JWT_SECRET` | chuỗi ngẫu nhiên ≥ 32 ký tự |
| `ONLYOFFICE_PUBLIC_URL` | URL trình duyệt dùng để tải editor, VD `https://office.benhvien.vn` |
| `AUTH_SIGNING_KEY` | chuỗi ngẫu nhiên ≥ 32 byte (`openssl rand -base64 48`) |
| `SEED_DEMO` | `false` |
| `SEED_ALLOW_RESET` | `false` |
| `SEED_ADMIN_PASSWORD` | mật khẩu tạm của tài khoản `admin` đầu tiên (bắt buộc đổi ở lần đăng nhập đầu) |
| `PUBLIC_BASE_URL` | `https://muasam.benhvien.vn` (dùng trong link email) |
| `AUTH_LOGIN_RATE_LIMIT` | `10` (số lần đăng nhập / phút / IP) |
| `SIGNING_DEFAULT_PROVIDER` | nhà cung cấp chữ ký số thật (mục 4) |
| `SIGNING_ENABLE_MOCK` | `false` |
| `HIS_*` | theo mục 5 |
| `HIS_DEV_ENDPOINTS` | `false` |
| `HANGFIRE_DASHBOARD` | `false` |

Email SMTP (biến môi trường của dịch vụ `api` trong `docker-compose.override.yml` hoặc file compose riêng):

```yaml
services:
  api:
    environment:
      Email__Host: smtp.benhvien.vn
      Email__Port: "587"
      Email__UseStartTls: "true"
      Email__UserName: muasam@benhvien.vn
      Email__Password: ${SMTP_PASSWORD}
      Email__FromAddress: muasam@benhvien.vn
      Email__FromName: "Cổng mua sắm — BVĐK TP Vinh"
      Cors__Origins__0: https://muasam.benhvien.vn
```

Bỏ dịch vụ `mailpit` khỏi stack production (chỉ dùng để xem email khi phát triển).

## 3. Khởi động

```bash
docker compose up -d --build postgres storage gotenberg onlyoffice api web
docker compose logs -f api        # chờ "Now listening" — migration và seed chạy tự động khi khởi động
curl -s http://127.0.0.1:47080/health/ready
```

Lần đầu: đăng nhập `admin` bằng `SEED_ADMIN_PASSWORD` → đổi mật khẩu → bật xác thực 2 lớp → khai báo **Thông tin bệnh viện, Cơ sở, Khoa/phòng, Kho (kèm mã kho HIS), Nhà cung cấp, Người dùng và vai trò**. Mẫu biểu, luồng ký và tham số hệ thống có sẵn mặc định và chỉnh được trong menu Quản trị.

Chỉ migrate + seed rồi thoát (VD trong pipeline triển khai): `docker compose run --rm api --seed-only`.

### Reverse proxy (TLS)

Ví dụ nginx của bệnh viện:

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

API chỉ tin `X-Forwarded-For` từ proxy khai báo trong `TRUSTED_PROXY_NETWORK` (mặc định dải bridge Docker `172.16.0.0/12`, là container `web`). Nếu có thêm proxy TLS phía trước, nginx của container `web` chuyển tiếp chuỗi địa chỉ, và giới hạn theo IP (đăng nhập, whitelist HIS) áp lên địa chỉ client mà proxy tin cậy báo lại. Không đặt `0.0.0.0/0`: ai cũng sẽ giả được IP.

**Tải tệp:** link tải là `/api/files/{token}` trên chính tên miền của cổng: ký bằng Data Protection, sống ngắn (5 phút), chỉ cấp sau khi kiểm tra quyền, API tự đọc tệp từ kho. Vì vậy chỉ cần mở cổng web, **không** cần mở cổng kho lưu trữ (47900) ra mạng. Chỉ khi có tên miền S3 công khai (CDN, S3 của nhà cung cấp) thì mới đặt `STORAGE_PUBLIC_ENDPOINT`; khi đó link là presigned URL S3 trỏ tới tên miền đó.

ONLYOFFICE cần một tên miền riêng (VD `office.benhvien.vn` → `127.0.0.1:47082`) vì trình duyệt tải editor trực tiếp từ đó; `ONLYOFFICE_PUBLIC_URL` trỏ tới tên miền này. Container `web` đã tự gắn các header bảo mật (`X-Content-Type-Options`, `X-Frame-Options`, `Content-Security-Policy: frame-ancestors`, `Referrer-Policy`, `Permissions-Policy`); HSTS đặt ở reverse proxy vì chỉ ở đó mới có TLS.

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

`scripts/backup.sh` (Linux) / `scripts\backup.ps1` (Windows): `pg_dump` dạng custom + đồng bộ toàn bộ bucket tệp vào `backups/<thời điểm>/`, giữ 30 bản gần nhất.

```cron
# /etc/cron.d/blueprocure — 01:30 hằng ngày, sau đó chép thư mục backups/ ra máy/kho lưu trữ khác
30 1 * * * root cd /opt/blueprocure && scripts/backup.sh backups 30 >> /var/log/blueprocure-backup.log 2>&1
```

**Khôi phục** (dừng API trước để không có ghi mới):

```bash
docker compose stop api
B=backups/20261016-013000
# 1) Cơ sở dữ liệu
docker compose exec -T postgres dropdb -U blueprocure --if-exists blueprocure
docker compose exec -T postgres createdb -U blueprocure blueprocure
docker compose exec -T postgres pg_restore -U blueprocure -d blueprocure --no-owner < $B/db.dump
# 2) Kho tệp
NET=$(docker inspect $(docker compose ps -q postgres) --format '{{range $k, $v := .NetworkSettings.Networks}}{{$k}}{{end}}')
docker run --rm --network "$NET" -v "$PWD/$B/files:/backup" \
  -e AWS_ACCESS_KEY_ID=$S3_ACCESS_KEY -e AWS_SECRET_ACCESS_KEY=$S3_SECRET_KEY -e AWS_DEFAULT_REGION=us-east-1 \
  amazon/aws-cli:2.27.0 s3 sync /backup "s3://$S3_BUCKET" --endpoint-url http://storage:8333
docker compose start api
```

Sau khi khôi phục, vào **Quản trị → Kiểm tra toàn vẹn → Kiểm tra ngay**: mọi tệp phải "khớp" SHA-256 với cơ sở dữ liệu. Nên diễn tập khôi phục định kỳ (mỗi quý) trên máy thử.

## 7. Vận hành

| Việc | Cách làm |
|---|---|
| Sức khỏe hệ thống | `GET /health/ready` (PostgreSQL, kho tệp, Gotenberg) — gắn vào hệ thống giám sát |
| Nhật ký ứng dụng | `docker compose logs api`; tùy chọn Seq (`--profile seq`) |
| Nhật ký hoạt động người dùng | Quản trị → Nhật ký hoạt động (append-only, có trigger chặn sửa/xóa) |
| Toàn vẹn tài liệu | job hằng ngày 02:00; cảnh báo gửi email/thông báo cho quản trị khi có tệp lệch hoặc mất |
| Nghiệm thu tự động | job 01:00 hằng ngày, chỉ chạy đúng ngày cấu hình trong tham số `acceptance.job_day_of_month` |
| Nhắc việc quá SLA | job mỗi giờ |
| Cập nhật phiên bản | `git pull && docker compose up -d --build api web` (migration chạy tự động; sao lưu trước khi cập nhật) |

## 8. Bảo mật — danh sách kiểm tra

- [ ] Đã đổi mọi bí mật trong `.env`, `SEED_DEMO=false`, `SEED_ALLOW_RESET=false`, `SIGNING_ENABLE_MOCK=false`, `HIS_DEV_ENDPOINTS=false`, `HANGFIRE_DASHBOARD=false`.
- [ ] Chỉ cổng 443 mở ra ngoài; cổng 47xxx chỉ lắng nghe `127.0.0.1`.
- [ ] HTTPS + HSTS ở reverse proxy; `Cors__Origins` chỉ chứa tên miền của cổng.
- [ ] Tài khoản quản trị bật xác thực 2 lớp; chính sách mật khẩu và khóa tài khoản sau 5 lần sai (mặc định) giữ nguyên.
- [ ] Whitelist IP cho API HIS; secret HIS cấp riêng cho từng môi trường.
- [ ] Sao lưu hằng ngày được chép ra ngoài máy chủ; đã diễn tập khôi phục.
- [ ] Theo dõi cảnh báo "Kiểm tra toàn vẹn" và "Nhật ký tích hợp".

## 9. Mở rộng

**Nhiều instance API:** `docker compose up -d --scale api=N api`, rồi `docker compose restart web` để nginx nhận đủ địa chỉ các instance. Redis (container `redis`, biến `REDIS_CONFIGURATION`) giữ trạng thái dùng chung: SignalR và phiên ký. Kiểm tra bằng `scripts/scale-check.ps1` / `.sh`. Nếu đặt `REDIS_CONFIGURATION=` (rỗng) thì chỉ chạy được **một** instance.

API không lưu trạng thái phiên (JWT + refresh token trong cookie) nên chạy được nhiều bản sau load balancer. Khi chạy nhiều bản (ADR-027): chỉ một bản bật `Hangfire__ServerEnabled=true`, cấu hình backplane SignalR (Redis) để thông báo realtime đến mọi bản, và dùng distributed cache chung (Redis) cho trạng thái ký số từ xa / USB token.
