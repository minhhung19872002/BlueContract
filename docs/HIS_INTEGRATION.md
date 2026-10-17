# Tài liệu tích hợp HIS ↔ BlueProcure

Tài liệu dành cho hãng phần mềm HIS (Hệ thống thông tin bệnh viện). Mô tả hai chiều tích hợp (spec §7):

- **HIS → Cổng (inbound):** HIS gửi phiếu nhập kho / phiếu hoàn trả; HIS đọc hợp đồng, tổng hợp đề xuất, danh mục hàng hóa.
- **Cổng → HIS (outbound):** cổng đọc danh mục hàng trúng thầu, Xuất – Nhập – Tồn, và (tùy chọn) **kéo** phiếu nhập kho định kỳ.

Mọi lời gọi đều được ghi vào **Nhật ký tích hợp** (Quản trị → Tích hợp HIS): request, response, kết quả, số lần xử lý. Quản trị viên có thể **gửi lại** một lần nhận phiếu bị lỗi sau khi sửa dữ liệu danh mục.

---

## 1. Xác thực request (HIS → Cổng)

Mỗi request gửi 3 header:

| Header | Nội dung |
|---|---|
| `X-Api-Key` | API key do bệnh viện cấp cho HIS |
| `X-Timestamp` | Thời điểm gửi, **Unix time tính bằng giây (UTC)**, ví dụ `1760600000` |
| `X-Signature` | `hex(HMAC_SHA256(secret, canonical))`, chữ thường, 64 ký tự |

Chuỗi `canonical` gồm 4 dòng nối bằng ký tự xuống dòng `\n` (LF, không có `\n` ở cuối):

```
METHOD                      (chữ hoa, ví dụ POST)
PATH_AND_QUERY              (đúng như trên URL, ví dụ /api/integration/his/contracts?supplierTaxCode=0302345678)
X-Timestamp                 (giống header)
hex(SHA256(body))           (chữ thường; body rỗng → e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855)
```

Quy tắc kiểm tra của cổng (theo thứ tự):

1. IP nguồn phải thuộc danh sách cho phép (`HIS_INBOUND_ALLOWED_IPS`, hỗ trợ CIDR, ví dụ `10.0.5.0/24`). Để trống = mọi IP (chỉ dùng khi thử nghiệm).
2. `X-Api-Key` hợp lệ.
3. `X-Timestamp` lệch giờ máy chủ **không quá ±5 phút** (đồng bộ NTP cho máy chủ HIS).
4. `X-Signature` đúng (so sánh thời gian hằng).
5. **Chống phát lại:** với request ghi (POST/PUT/DELETE), một chữ ký chỉ được chấp nhận **một lần** trong cửa sổ thời gian. Gửi lại cùng nội dung phải ký lại với `X-Timestamp` mới. Request đọc (GET) không bị giới hạn này.
6. Giới hạn tần suất: mặc định **120 request/phút cho mỗi API key** (`HIS_INBOUND_RATE_LIMIT`). Vượt mức → HTTP 429.

### Ví dụ ký request

**PowerShell**

```powershell
$secret = 'his-dev-secret-change-me-0123456789'
$body   = [IO.File]::ReadAllBytes('phieu.json')
$path   = '/api/integration/his/warehouse-receipts'
$ts     = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()
$sha    = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($body)).ToLower()
$canon  = "POST`n$path`n$ts`n$sha"
$hmac   = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($secret))
$sig    = [Convert]::ToHexString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($canon))).ToLower()
Invoke-RestMethod -Method Post -Uri "https://cong.benhvien.vn$path" -ContentType 'application/json' -Body $body `
  -Headers @{ 'X-Api-Key' = 'his-dev-key'; 'X-Timestamp' = $ts; 'X-Signature' = $sig }
```

**bash + openssl**

```bash
SECRET='his-dev-secret-change-me-0123456789'
PATH_Q='/api/integration/his/warehouse-receipts'
TS=$(date +%s)
SHA=$(openssl dgst -sha256 -hex < phieu.json | awk '{print $NF}')
SIG=$(printf 'POST\n%s\n%s\n%s' "$PATH_Q" "$TS" "$SHA" | openssl dgst -sha256 -hmac "$SECRET" -hex | awk '{print $NF}')
curl -sS -X POST "https://cong.benhvien.vn$PATH_Q" -H 'Content-Type: application/json' \
  -H "X-Api-Key: his-dev-key" -H "X-Timestamp: $TS" -H "X-Signature: $SIG" --data-binary @phieu.json
```

**C#**

```csharp
var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
var canonical = string.Join('\n', "POST", path, ts, Convert.ToHexStringLower(SHA256.HashData(body)));
var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(canonical)));
```

> Lưu ý: ký **đúng các byte** gửi đi (không định dạng lại JSON sau khi ký); `PATH_AND_QUERY` phải giống hệt URL gửi đi (cùng thứ tự tham số, cùng mã hóa).

---

## 2. API cổng cung cấp cho HIS

Gốc: `https://<cổng>/api/integration/his`. Dữ liệu JSON UTF-8, tên trường camelCase, enum là chuỗi, ngày `yyyy-MM-dd`, thời điểm ISO 8601 (UTC). Số tiền là số thập phân (VNĐ, đơn giá **đã gồm VAT**), số lượng tối đa 3 chữ số thập phân.

### 2.1 `POST /warehouse-receipts` — đẩy phiếu nhập kho / hoàn trả

Liên kết hợp đồng theo **Số quyết định thầu + MST nhà cung cấp** (có thể gửi thêm `contractNumber` khi một NCC có nhiều hợp đồng trong cùng quyết định). **Idempotent theo `hisReceiptId`.**

```json
{
  "hisReceiptId": "PN-2026-000123",
  "receiptNumber": "PN000123",
  "type": "Receipt",
  "receiptDate": "2026-10-16",
  "warehouseCode": "KV01",
  "bidDecisionNumber": "126/QĐ-BVĐK",
  "supplierTaxCode": "0302345678",
  "supplierName": "Công ty TNHH Thiết bị Y tế Phương Đông",
  "contractNumber": "16/2026/HĐ-BVĐK",
  "callOrderNumber": "GH-2026-0001",
  "delivererName": "Trần Văn Giao",
  "note": "Giao đợt 1",
  "invoice": {
    "symbol": "1C26TAA",
    "number": "0000158",
    "date": "2026-10-16",
    "amountBeforeVat": 809524,
    "vatAmount": 40476,
    "totalAmount": 850000,
    "sellerTaxCode": "0302345678"
  },
  "lines": [
    {
      "hisKey": "K123456",
      "itemCode": "VT001",
      "itemName": "Bơm tiêm 5ml",
      "unit": "Cái",
      "lotNumber": "L2610",
      "expiryDate": "2028-09-30",
      "quantity": 1000,
      "unitPrice": 850,
      "vatRate": 5
    }
  ]
}
```

| Trường | Bắt buộc | Ghi chú |
|---|---|---|
| `hisReceiptId` | ✔ | Khóa duy nhất của phiếu trên HIS (≤ 100 ký tự) |
| `receiptNumber` | | Số phiếu hiển thị; bỏ trống → cổng tự cấp `NK-yyyy-nnnn` / `HT-yyyy-nnnn` |
| `type` | ✔ | `Receipt` (nhập kho) hoặc `Return` (hoàn trả — giá trị âm trong lũy kế) |
| `receiptDate` | ✔ | Ngày phiếu |
| `warehouseCode` | ✔ | Mã kho trên HIS (ánh xạ theo "Mã kho HIS" trong Quản trị → Kho) |
| `bidDecisionNumber` | | Số QĐ thầu; bỏ trống với hợp đồng ngoài thầu |
| `supplierTaxCode` | ✔ | MST nhà cung cấp |
| `contractNumber`, `callOrderNumber` | | Giúp chọn đúng hợp đồng / đơn gọi hàng; nếu thiếu cổng tự xác định |
| `invoice` | | Có thể bổ sung sau: NCC tải XML hóa đơn điện tử trên cổng ở bước "Người giao hàng ký" |
| `lines[].hisKey` | khuyến nghị | Mã key mặt hàng trên HIS (đã đồng bộ khi lập danh mục); nếu thiếu, cổng so theo `itemCode` |
| `lines[].quantity` | ✔ | > 0 |
| `lines[].unitPrice` | ✔ | Đơn giá đã gồm VAT |
| `lines[].vatRate` | | % thuế GTGT (0–100) |

**Kết quả (HTTP 200):**

```json
{
  "receiptId": "01a14417-b2d4-7188-91af-b3a083c63cf4",
  "receiptNumber": "PN000123",
  "outcome": "Created",
  "status": "Signing",
  "validationStatus": "Warning",
  "contractNumber": "16/2026/HĐ-BVĐK",
  "issues": [
    { "code": "EXPIRY_TOO_SOON", "severity": "Warning", "message": "Dòng 1: lô L2610 ... còn dưới 180 ngày.", "lineNo": 1 }
  ]
}
```

- `outcome`: `Created` (phiếu mới), `Updated` (cùng `hisReceiptId`, nội dung khác, **chưa ai ký** → cập nhật phiếu, sinh lại PDF, nếu đang bị trả lại thì tự trình ký lại), `Duplicate` (cùng `hisReceiptId` và cùng nội dung → không làm gì).
- Phiếu đã có chữ ký hoặc đã hoàn tất **không** cập nhật được nữa → HTTP 409 `RECEIPT_LOCKED_SIGNED`; muốn điều chỉnh hãy gửi **phiếu hoàn trả** hoặc phiếu mới.
- Phiếu **không hợp lệ** vẫn được nhận (HTTP 200, `validationStatus = Invalid`) để bệnh viện nhìn thấy, nhưng **không ký được** cho đến khi HIS gửi lại bản đã sửa.

Sau khi nhận, cổng chạy kiểm tra tự động, sinh PDF phiếu theo mẫu và khởi tạo luồng **5 chữ ký**: Thủ kho → Người giao hàng (NCC) → Kế toán kho → Trưởng khoa → Kế toán trưởng.

**Danh sách kiểm tra (validation engine):**

| Mã | Mức | Nội dung |
|---|---|---|
| `SUPPLIER_NOT_FOUND` | Không hợp lệ | MST chưa có trên cổng |
| `CONTRACT_NOT_FOUND` | Không hợp lệ | Không có hợp đồng theo Số QĐ + MST |
| `CONTRACT_NOT_EFFECTIVE`, `CONTRACT_CLOSED` | Không hợp lệ | Hợp đồng chưa hiệu lực / đã thanh lý, hủy |
| `CONTRACT_EXPIRED` | Cảnh báo | Ngày phiếu sau ngày hết hạn hợp đồng |
| `ITEM_NOT_IN_CONTRACT` | Không hợp lệ | Mặt hàng không thuộc hợp đồng |
| `UNIT_PRICE_ABOVE_CONTRACT` | Không hợp lệ | Đơn giá cao hơn đơn giá hợp đồng |
| `UNIT_PRICE_MISMATCH` | Cảnh báo | Đơn giá thấp hơn đơn giá hợp đồng |
| `QTY_EXCEEDS_CONTRACT` | Không hợp lệ | Vượt số lượng còn lại của hợp đồng |
| `RETURN_EXCEEDS_RECEIVED` | Không hợp lệ | Hoàn trả nhiều hơn số đã nhập |
| `QTY_EXCEEDS_CALLED` | Cảnh báo | Vượt số lượng đã gọi hàng (hoặc còn phải giao của đơn gọi hàng) |
| `INVOICE_MISSING` | Cảnh báo | Chưa có hóa đơn — bắt buộc trước bước Người giao hàng ký |
| `INVOICE_TOTAL_MISMATCH` | Cảnh báo | Tổng tiền hóa đơn lệch tổng tiền phiếu quá sai số làm tròn (tham số hệ thống) |
| `INVOICE_SELLER_MISMATCH` | Cảnh báo | MST người bán trên hóa đơn khác MST NCC |
| `ITEM_EXPIRED` | Không hợp lệ | Lô hàng đã hết hạn dùng |
| `EXPIRY_TOO_SOON` | Cảnh báo | Hạn dùng còn ít hơn ngưỡng (tham số hệ thống, mặc định 180 ngày) |
| `INVOICE_DUPLICATE` | Không hợp lệ | Số hóa đơn đã dùng cho phiếu khác của cùng NCC |

### 2.2 `GET /contracts?bidDecisionNumber=&supplierTaxCode=`

Hợp đồng **đang hiệu lực** kèm từng mặt hàng: số lượng HĐ, đơn giá, đã gọi, đã nhập, **còn lại** (= SL HĐ − đã nhập kho, sau trừ hoàn trả).

### 2.3 `GET /proposal-summaries?status=Approved&approvedSince=`

Tổng hợp đề xuất (mặc định đã được Giám đốc ký duyệt): theo từng mặt hàng — hợp đồng, MST NCC, `hisKey`, SL duyệt, đã gọi, còn được gọi.

### 2.4 `GET /items?updatedSince=`

Danh mục hàng hóa thay đổi từ thời điểm `updatedSince` (tối đa 5.000 dòng mỗi lần; gọi lại với mốc mới nhất để lấy tiếp).

### 2.5 Lỗi

Mọi lỗi trả về `application/problem+json` với `code` ổn định và `title` tiếng Việt:

```json
{ "status": 401, "title": "Chữ ký HMAC của request không hợp lệ.", "code": "HIS_AUTH_SIGNATURE_INVALID", "correlationId": "..." }
```

| HTTP | `code` | Ý nghĩa |
|---|---|---|
| 400 | `VALIDATION_FAILED` (+ `errors`) | Thiếu/sai trường dữ liệu |
| 401 | `HIS_AUTH_API_KEY_INVALID`, `HIS_AUTH_TIMESTAMP_INVALID`, `HIS_AUTH_SIGNATURE_INVALID`, `HIS_AUTH_REPLAY` | Xác thực |
| 403 | `HIS_AUTH_IP_NOT_ALLOWED` | IP không được phép |
| 409 | `RECEIPT_LOCKED_SIGNED`, `RECEIPT_TYPE_LOCKED` | Phiếu đã ký / đổi loại phiếu |
| 413 | `FILE_TOO_LARGE` | Body vượt 5 MB |
| 429 | `TOO_MANY_REQUESTS` | Vượt giới hạn tần suất |
| 503 | `HIS_INBOUND_DISABLED` | Cổng chưa cấu hình API key |

Khi nhận lỗi 5xx hoặc mất kết nối, HIS nên gửi lại (ký lại với timestamp mới) — nhờ idempotent theo `hisReceiptId` nên không sinh phiếu trùng.

---

## 3. API HIS cung cấp cho cổng (outbound — `IHisClient`)

Cấu hình trong `appsettings` mục `His` (biến môi trường `His__...`): `Provider` = `Rest`, `BaseUrl`, `TimeoutSeconds`. Mỗi lời gọi có retry, circuit breaker và timeout.

Xác thực (`AuthMode`):
- `ApiKey` (mặc định): gửi `ApiKey` trong header `ApiKeyHeader`.
- `Login`: đăng nhập bằng tài khoản dịch vụ `Username` / `Password` (`HIS_AUTH_MODE`, `HIS_USERNAME`, `HIS_PASSWORD`), gửi `Authorization: Bearer <token>`.
  - `LoginPath` (mặc định `api/auth/login`, body `{"username","password"}`) và `TokenPath` (mặc định `data.token`).
  - Token dùng chung cho cả instance, đăng nhập lại sau `TokenLifetimeMinutes` (mặc định 25) hoặc khi HIS trả 401.
  - Tài khoản dịch vụ không được bật xác thực 2 lớp.

| Nghiệp vụ | Cấu hình đường dẫn (mặc định) | Ánh xạ trường |
|---|---|---|
| Danh mục trúng thầu theo QĐ + MST | `ItemsPath` = `api/bid-items?bidNumber={bidNumber}&taxCode={taxCode}&facility={facilityCode}` | `ItemFields` |
| Xuất – Nhập – Tồn | `StockPath` = `api/stock-movements?warehouse={warehouseCode}&from={from}&to={to}&keys={hisKeys}` | `StockFields` |
| Phiếu nhập kho (pull) | `ReceiptsPath` = `api/warehouse-receipts?from={from}&to={to}` | trả về đúng schema mục 2.1 |

`ItemsArrayPath`, `StockArrayPath`, `ReceiptsArrayPath` chỉ vị trí mảng trong JSON trả về (mặc định `data`). `ItemFields` / `StockFields` ánh xạ tên trường của hãng sang trường của cổng (cho phép đường dẫn dạng `a.b.c`), nhờ đó một bộ chuyển đổi dùng được cho nhiều hãng HIS.

### Kéo phiếu định kỳ (pull)

Bật bằng `HIS_PULL_ENABLED=true`, lịch `HIS_PULL_CRON` (mặc định 15 phút/lần, giờ UTC), mỗi lần hỏi phiếu của `PullLookbackDays` ngày gần nhất (mặc định 3). Mỗi phiếu đi qua cùng quy trình như push (idempotent), nên push và pull có thể dùng song song. Quản trị viên có nút **"Kéo phiếu từ HIS ngay"**.

---

## 4. Môi trường thử nghiệm

- HIS giả lập (`HIS_PROVIDER=Mock`) trả dữ liệu ổn định theo Số QĐ + MST.
- `POST /api/dev/his/simulate-receipt` (bật bằng `HIS_DEV_ENDPOINTS=true`, cần đăng nhập với quyền lập phiếu nhập kho) tạo phiếu cho một hợp đồng hiệu lực theo kịch bản `Valid` / `Warning` / `Invalid`, gửi ngay (`Push`) hoặc xếp vào HIS giả lập chờ job kéo về (`Pull`). Trên giao diện: **Phiếu nhập kho → "Giả lập HIS gửi phiếu"**.
- Khóa thử nghiệm mặc định trong `.env.example`: `X-Api-Key = his-dev-key`, secret `his-dev-secret-change-me-0123456789`. **Bắt buộc thay khi triển khai thật.**

## 5. HIS BlueStar (`his.bluestar.com.vn`)

HIS của BlueStar (repo HIS, migration 227) đã có sẵn API theo đúng các đường dẫn mặc định ở mục 3, dưới tiền tố `api/integration/bluecontract/`:

| Cổng gọi | HIS trả |
|---|---|
| `bid-items` | luôn rỗng: HIS chưa lưu kết quả trúng thầu theo mặt hàng. Danh mục trúng thầu nhập bằng Excel, **mã key HIS = mã thuốc trên HIS** |
| `stock-movements?warehouse=<mã kho>&from&to&keys=<mã thuốc,…>` | XNT theo mã kho (`hisKey`, `opening`, `received`, `issued`, `closing`) |
| `warehouse-receipts?from&to` | phiếu nhập NCC **được duyệt** trong khoảng ngày, theo đúng schema mục 2.1 (tối đa 31 ngày) |

- **Đăng nhập:** tài khoản dịch vụ role `BLUECONTRACT_SERVICE`. Role này không có quyền nào ngoài các route trên và được miễn hạn đổi mật khẩu.
- **HIS đẩy phiếu:** khi duyệt một phiếu nhập NCC, HIS gửi ngay `POST /api/integration/his/warehouse-receipts` có ký HMAC. Cấu hình phía HIS là `BlueContract__BaseUrl`, `BlueContract__ApiKey`, `BlueContract__Secret` (= `HIS_INBOUND_API_KEY` / `HIS_INBOUND_SECRET` của cổng). Nếu đẩy lỗi, cổng vẫn kéo về được nhờ `HIS_PULL_ENABLED=true`.
- **Ghép hợp đồng:** theo MST của NCC trên HIS, cộng với **Số QĐ trúng thầu** và **Số hợp đồng** nhập trên phiếu "Nhập từ NCC" của HIS. Đơn giá gửi sang = đơn giá HIS × (1 + VAT%).
- **Hóa đơn:** không gửi kèm, vì HIS không lưu ký hiệu hóa đơn. NCC tải XML hóa đơn điện tử lên cổng ở bước "Người giao hàng ký".
- **Khai báo trên cổng:**
  - Quản trị → Kho: "Mã kho HIS" = mã kho trên HIS (VD `KT01`).
  - Nhà cung cấp: MST trùng với MST trên HIS.
