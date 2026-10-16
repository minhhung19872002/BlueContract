# BlueProcure

Cổng Quản lý & Thanh toán Mua sắm Hàng hóa Trực tuyến cho bệnh viện. Hệ thống số hóa và ký số toàn bộ vòng đời hợp đồng mua sắm, từ Quyết định thầu đến Thanh lý hợp đồng.

- Đặc tả: [`docs/SPEC.md`](docs/SPEC.md)
- Tiến độ: [`PROGRESS.md`](PROGRESS.md)
- Quyết định kiến trúc: [`docs/DECISIONS.md`](docs/DECISIONS.md)
- Hướng dẫn sử dụng: [`docs/USER_GUIDE.md`](docs/USER_GUIDE.md)
- Triển khai & vận hành: [`docs/DEPLOY.md`](docs/DEPLOY.md)
- Tích hợp HIS: [`docs/HIS_INTEGRATION.md`](docs/HIS_INTEGRATION.md)

## Chạy nhanh trên Windows

Yêu cầu: **Docker Desktop**, **Git**. PowerShell 5.1 hoặc 7 đều chạy được.

```powershell
git clone <repo-url> BlueContract
cd BlueContract
powershell -ExecutionPolicy Bypass -File scripts\dev-up.ps1
```

Lần đầu chạy sẽ mất vài phút vì phải tải image (riêng ONLYOFFICE khoảng 3–4 GB). Script tự làm các việc sau: tạo `.env`, build image, khởi động toàn bộ service, chạy migration, seed dữ liệu, tạo bucket lưu trữ tệp, rồi chờ đến khi `/health/ready` trả về xanh.

| Dịch vụ | URL |
|---|---|
| Web | http://localhost:47080 |
| API docs (Scalar) | http://localhost:47080/docs |
| Health | http://localhost:47080/health/ready |
| Mailpit (email dev) | http://localhost:47025 |
| Kho tệp S3 — giao diện filer (SeaweedFS) | http://localhost:47901 |
| ONLYOFFICE | http://localhost:47082 |
| Hangfire dashboard | http://localhost:47080/hangfire |
| Seq (tùy chọn) | http://localhost:47341, chạy bằng `scripts\dev-up.ps1 -Seq` |

Có thể đổi các cổng trong `.env` (xem `.env.example`).

### Tài khoản demo
Mật khẩu chung **`Demo@123`**.

| Tài khoản | Vai trò | Tài khoản | Vai trò |
|---|---|---|---|
| `admin` | Quản trị hệ thống | `ketoantruong` | Kế toán trưởng |
| `muasam` | Tổ Mua sắm | `ketoankho` | Kế toán kho |
| `rasoat` | Tổ Rà soát | `thukho` | Thủ kho |
| `khoanoi` | Khoa/Phòng (Khoa Nội) | `truongkhoa` | Trưởng khoa Dược |
| `tonghop` / `tonghopvt` | Người tổng hợp (Dược / Vật tư) | `giamdoc` / `phogiamdoc` | Giám đốc / Phó Giám đốc |
| `totruongduoc` / `totruongvattu` | Tổ trưởng mảng | `vanthu` | Văn thư |
| `ketoan` | Kế toán | `xem` | Chỉ xem |
| `ncc1`, `ncc2`, `ncc3` | Quản trị NCC | `ncc1.user`, `ncc2.user`, `ncc3.user` | Người dùng NCC |

Người dùng NCC đăng nhập cùng trang và được chuyển sang **Cổng Nhà cung cấp** (`/ncc`).

## Scripts

| Việc | PowerShell | Bash |
|---|---|---|
| Khởi động stack | `scripts\dev-up.ps1 [-Seq] [-NoBuild]` | `scripts/dev-up.sh [--seq] [--no-build]` |
| Migrate + seed lại | `scripts\seed.ps1 [-ResetDemo]` | `scripts/seed.sh [--reset-demo]` |
| Xóa sạch DB + kho tệp | `scripts\reset-db.ps1 [-Force]` | `scripts/reset-db.sh [--force]` |
| Sao lưu (giữ 30 bản) | `scripts\backup.ps1` | `scripts/backup.sh` |
| Xuất 16 mẫu DOCX + 2 biểu mẫu import Excel ra `templates/` | `scripts\export-templates.ps1` | `scripts/export-templates.sh` |
| Kiểm thử E2E (Playwright) trên stack đang chạy | `scripts\e2e.ps1` | `scripts/e2e.sh` |
| Cổng coverage Domain + Application (≥ 70%) sau `dotnet test --collect "XPlat Code Coverage"` | `scripts\coverage.ps1 [dir] [min]` | `scripts/coverage.sh [dir] [min]` |

## Phát triển không qua Docker (tùy chọn)

Cần cài **.NET 10 SDK** và **Node.js 22**. Hạ tầng (Postgres, kho tệp S3, Gotenberg…) vẫn chạy bằng Docker.

```powershell
docker compose up -d postgres storage gotenberg onlyoffice mailpit
cd backend;  dotnet run --project src/BlueContract.Api          # http://localhost:5296
cd frontend; npm install; npm run dev                           # http://localhost:5173 (proxy /api → 5296)
```

- Chạy test backend: `cd backend; dotnet test`. Integration test dùng Testcontainers nên cần Docker đang chạy.
- Chạy kiểm tra frontend: `cd frontend; npm run lint; npm test; npm run build`
- Sinh lại TS client sau khi đổi API: `cd backend; dotnet build`, rồi `cd frontend; npm run gen:api`

## Cấu trúc

```
backend/   .NET 10 — Domain / Application / Infrastructure / Api + tests
frontend/  React 18 + Vite + TypeScript + Ant Design 5
templates/ Mẫu DOCX/XLSX
scripts/   dev-up, seed, reset-db, backup, e2e, coverage (.ps1 + .sh)
docs/      SPEC, DECISIONS, DEPLOY, USER_GUIDE, HIS_INTEGRATION (API: Scalar tại /docs)
```
