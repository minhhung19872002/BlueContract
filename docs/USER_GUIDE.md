# Hướng dẫn sử dụng BlueProcure

Cổng Quản lý & Thanh toán Mua sắm Hàng hóa Trực tuyến số hóa toàn bộ vòng đời hợp đồng mua sắm: **Quyết định thầu → Hợp đồng → Đề xuất / Tổng hợp / Gọi hàng → Nhập kho → Nghiệm thu → Xác định giá trị (08a) & thanh toán → Thanh lý**, với chữ ký số ở mọi bước.

## 1. Bắt đầu

- Truy cập địa chỉ cổng do bệnh viện cung cấp, đăng nhập bằng tài khoản được cấp. Lần đầu (hoặc sau khi được đặt lại mật khẩu) hệ thống yêu cầu đổi mật khẩu: tối thiểu 8 ký tự, có chữ hoa, chữ thường, chữ số và ký tự đặc biệt.
- Nhập sai mật khẩu 5 lần liên tiếp, tài khoản bị khóa tạm 15 phút.
- **Tài khoản & bảo mật** (bấm tên ở góc phải): đổi mật khẩu, bật **xác thực 2 lớp** (ứng dụng Google/Microsoft Authenticator). Thông tin chứng thư số (số serial, tổ chức cấp, hạn dùng) do Quản trị khai báo trong hồ sơ người dùng.
- Người dùng nhà cung cấp (NCC) đăng nhập cùng trang và được chuyển sang **Cổng Nhà cung cấp**.
- Chuông thông báo hiện tài liệu mới chờ xử lý theo thời gian thực; email gửi kèm đường dẫn mở thẳng tài liệu.

Quy ước hiển thị: tiền `1.234.567 ₫` (đơn giá **đã gồm thuế GTGT**), ngày `dd/MM/yyyy`, giờ Việt Nam. Mọi danh sách có tìm kiếm, lọc, sắp xếp, phân trang và nút **Xuất Excel**; bộ lọc được giữ trên địa chỉ trang để gửi cho đồng nghiệp.

## 2. Trình ký (dùng chung mọi tài liệu)

Menu **Trình ký** chứa toàn bộ tài liệu cần ký của bạn, chia tab *Chờ tôi xử lý / Tôi đã xử lý / Tôi đã gửi / Bị trả lại*, lọc theo loại tài liệu, NCC, hợp đồng, ngày.

- Bấm một dòng để xem PDF, luồng ký, ý kiến các bước trước.
- **Ký số** / **Đồng ý** / **Duyệt**: thực hiện bước của bạn. Ký số từ xa (SmartCA, MySign…) cần xác nhận trên điện thoại; USB token cần cắm token và cài plugin ký.
- **Trả lại** (bắt buộc lý do, chọn bước trả về) · **Chuyển tiếp ý kiến** · **Ký hàng loạt** (chọn nhiều tài liệu, ký một lần).
- Khung luồng ký có thể hiện **điều kiện ký**: tài liệu *không hợp lệ* thì không ký được; tài liệu có *cảnh báo* thì phải nhập **lý do chấp nhận** trước khi ký (lý do lưu vào ý kiến của bước).
- Bản đã có chữ ký không sửa được; nội dung thay đổi sau đó tạo phiên bản mới và luồng ký bắt đầu lại.
- Ủy quyền ký: Giám đốc ủy quyền cho Phó Giám đốc trong khoảng thời gian (Quản trị → Ủy quyền ký); chữ ký hiển thị "KT. GIÁM ĐỐC / PHÓ GIÁM ĐỐC".
- Menu **Xác minh chữ ký**: tải một file PDF để kiểm tra các chữ ký số trong đó.

## 3. Theo vai trò

### Tổ Mua sắm (`muasam`)
1. **Quyết định thầu**: tạo quyết định, thêm NCC trúng thầu, **đồng bộ danh mục từ HIS** hoặc **import Excel** (tải biểu mẫu tại màn hình), ban hành.
2. **Hợp đồng → Tạo mới** (4 bước): Nguồn (trong thầu: chọn QĐ; ngoài thầu: chọn hoặc **tạo NCC mới** ngay trong form, hệ thống tự cấp tài khoản và gửi email kích hoạt) → NCC & hàng hóa → Điều khoản → Mẫu & xem trước → **Gửi rà soát**.
3. Theo dõi hợp đồng: tab *Hàng hóa & theo dõi thực hiện* (SL HĐ / đã gọi / đã nhập / đã nghiệm thu / đã thanh toán / còn lại), *Phụ lục*, *Nhập kho*, *Thanh toán*, *Lịch sử*.
4. **Phiếu nhập kho**: phiếu từ HIS tự vào hệ thống; khi cần, **Lập phiếu trên cổng** hoặc **phiếu xác nhận khối lượng** (hợp đồng dịch vụ không qua kho — hoàn thành không cần ký). Phiếu bị trả lại: sửa/bổ sung rồi **Gửi ký lại**.
5. **Thanh toán**:
   - *Nghiệm thu*: hệ thống tự lập bản nháp vào ngày cấu hình hằng tháng (hoặc bấm **Lập nghiệm thu tự động**); có thể **Lập biên bản nghiệm thu** tay (chọn hợp đồng, kỳ, phiếu đã ký đủ) → **Gửi ký**.
   - *Xác định giá trị (08a)*: chọn các biên bản nghiệm thu đã ký → hệ thống tính giá trị kỳ này, lũy kế, % so với HĐ và sinh kèm **Đề nghị thanh toán** → **Gửi ký**. Khi Kho bạc/ngân hàng chuyển tiền: **Thêm UNC** (số, ngày, số tiền, bản scan) — làm được cả sau khi ký.
   - *Thanh lý*: chọn hợp đồng → xem bảng đối chiếu → **Lập biên bản thanh lý**; khoản còn thiếu có thể **Đánh dấu đã thanh toán** kèm chứng từ để chốt công nợ → **Gửi ký**. Ký xong, hợp đồng chuyển **Đã thanh lý**.
6. **Hồ sơ lưu trữ**: tìm theo số hợp đồng / số quyết định / NCC → cây tài liệu → xem từng file → **Tải toàn bộ hồ sơ (ZIP)** kèm `muc_luc.pdf` (danh sách tệp + mã SHA-256) → bật **Đã đầy đủ** khi hồ sơ hoàn chỉnh.

### Tổ Rà soát (`rasoat`)
Menu **Rà soát** liệt kê hợp đồng, phụ lục, biên bản nghiệm thu / 08a / thanh lý đang chờ Tổ rà soát (tab *Chờ rà soát / Đã rà soát*; các tài liệu này cũng có trong **Trình ký**). Bấm một dòng để xem PDF và luồng ký → kiểm tra (được sửa trực tiếp trên ONLYOFFICE với chế độ theo dõi thay đổi) → **Đồng ý** hoặc **Trả lại**. Nút **So sánh phiên bản** (cũng có ở trang Tài liệu khi tài liệu có từ 2 phiên bản) cho xem đoạn văn thêm (xanh) / bỏ (đỏ) giữa hai phiên bản bất kỳ, hoặc hai bản PDF đặt song song.

### Khoa/Phòng (`khoanoi`)
**Đề xuất → Lập phiếu đề xuất**: chọn mảng, kho, kỳ → lưới mặt hàng của các hợp đồng còn hiệu lực có cột **Xuất – Nhập – Tồn** lấy từ HIS và **SL còn lại của HĐ**; nhập SL đề xuất (vượt còn lại sẽ tô đỏ) → **Lưu phiếu** → **Ký và gửi**.

### Người tổng hợp (`tonghop`, `tonghopvt`)
1. **Tổng hợp đề xuất → Lập phiếu tổng hợp**: chọn mảng, tích các phiếu đề xuất → hệ thống gộp theo mặt hàng → **Gửi ký duyệt** → ký số. Tổ trưởng sửa SL duyệt thì bạn ký lại.
2. **Gọi hàng**: chọn tổng hợp đã được Giám đốc ký → nhập SL gọi (≤ SL duyệt còn lại; nút *Gọi toàn bộ số còn lại*), kho nhận, hạn giao → **Gọi hàng**: hệ thống tự tách đơn theo NCC và gửi email cho NCC. Theo dõi trạng thái xác nhận và giao hàng của từng đơn.

### Tổ trưởng mảng (`totruongduoc`, `totruongvattu`)
Mở phiếu tổng hợp được giao → sửa cột **SL duyệt** nếu cần (**Lưu SL duyệt** → người tổng hợp ký lại) → **Duyệt**.

### Thủ kho, Kế toán kho, Trưởng khoa, Kế toán trưởng
Ký **phiếu nhập kho / hoàn trả** theo thứ tự: Thủ kho → Người giao hàng (NCC) → Kế toán kho → Trưởng khoa → Kế toán trưởng. Trang chi tiết phiếu hiện **cảnh báo** của hệ thống kiểm tra tự động (mặt hàng ngoài HĐ, đơn giá khác HĐ, vượt SL còn lại / đã gọi, hạn dùng gần, hóa đơn thiếu hoặc lệch tiền, trùng số hóa đơn…), tô vàng dòng có vấn đề. Phiếu **không hợp lệ** chỉ có thể **Trả lại** để HIS gửi bản sửa.

### Kế toán (`ketoan`)
Xem thay đổi NCC đề xuất trên hợp đồng (chấp nhận / từ chối từng thay đổi), **ký nháy** hợp đồng, tham gia lập chứng từ thanh toán.

### Giám đốc / Phó Giám đốc (`giamdoc`, `phogiamdoc`) và Văn thư (`vanthu`)
Ký hợp đồng, tổng hợp đề xuất, biên bản nghiệm thu, 08a, đề nghị thanh toán, thanh lý trong **Trình ký** (có thể ký hàng loạt, trên máy tính bảng / điện thoại). Văn thư **đóng dấu điện tử** của bệnh viện ở bước cuối.

### Nhà cung cấp (`ncc1`, `ncc1.user`…)
- **Hợp đồng**: hợp đồng chờ rà soát mở trên ONLYOFFICE ở chế độ theo dõi thay đổi → **Gửi lại** (kèm ý kiến) hoặc **Đồng ý ký** → ký số trong **Trình ký**.
- **Gọi hàng**: xác nhận SL từng mặt hàng (ít hơn SL gọi phải ghi lý do) → **Gửi xác nhận**.
- **Phiếu nhập kho**: ở bước *Người giao hàng*, tải **XML hóa đơn điện tử** (hệ thống tự đọc ký hiệu, số, ngày, tổng tiền) hoặc PDF / nhập tay, rồi ký.
- **Thanh toán**: xem và ký biên bản nghiệm thu, 08a, đề nghị thanh toán, thanh lý; xem ủy nhiệm chi.
- **Tài khoản công ty**: thông tin NCC; Quản trị NCC quản lý tài khoản nhân viên.
- **Tổng quan**: tài liệu chờ ký, đơn gọi hàng chờ xác nhận, phiếu cần bổ sung hóa đơn, công nợ bệnh viện còn phải trả.

### Ảnh chữ ký và con dấu

Mỗi người tải **ảnh chữ ký tay** (và nếu muốn, **chữ ký nháy**) ở **Tài khoản → Chữ ký hiển thị khi ký số**; Quản trị tải **ảnh con dấu** ở **Quản trị → Thông tin bệnh viện → Con dấu điện tử**. Ảnh được đặt vào ô chữ ký trên PDF cùng họ tên, chức danh và thời điểm ký (PNG nền trong suốt hoặc JPEG, tối đa 2 MB). Không có ảnh thì ô chữ ký chỉ có chữ như trước.

Đóng dấu điện tử ký bằng **chứng thư số tổ chức** của Bệnh viện: Quản trị cài tệp `.p12`/`.pfx` (kèm mật khẩu) ở **Thông tin bệnh viện → Chứng thư số tổ chức**. Khi chưa cài, hệ thống dùng chứng thư thử nghiệm (không có giá trị pháp lý) — chỉ dùng khi chạy thử.

## 4. Tổng quan và báo cáo

- **Tổng quan** (bệnh viện): tài liệu chờ xử lý theo loại, hợp đồng sắp hết hạn / sắp hết số lượng, phiếu nhập kho cảnh báo, gọi hàng chưa xác nhận quá hạn, biểu đồ thanh toán 12 tháng.
- **Báo cáo** (đều xuất Excel): tình hình thực hiện hợp đồng, công nợ theo NCC, giá trị thanh toán theo tháng / quý / năm (theo mảng, cơ sở, NCC), tiến độ ký (thời gian xử lý trung bình mỗi bước, tài liệu tồn đọng theo người).

## 5. Quản trị (`admin`)

| Chức năng | Ghi chú |
|---|---|
| Thông tin bệnh viện, Cơ sở, Khoa/phòng, Kho | Kho có **mã kho HIS** để ghép phiếu nhập kho |
| Người dùng, Vai trò & phân quyền | khai báo chứng thư số của từng người; ma trận vai trò × quyền; phạm vi dữ liệu theo cơ sở / khoa / mảng |
| Nhà cung cấp | cấp tài khoản NCC (email kích hoạt) |
| Ủy quyền ký | khoảng thời gian, vai trò được ủy quyền |
| Mẫu biểu | tải DOCX, kiểm tra placeholder, xem trước với dữ liệu mẫu, quản lý phiên bản |
| Cấu hình luồng ký | kéo thả thứ tự bước, chọn vai trò / người / NCC, vị trí chữ ký, SLA nhắc việc |
| Tham số hệ thống | ngày chạy nghiệm thu tự động, sai số làm tròn hóa đơn, ngưỡng hạn dùng, cảnh báo hết hạn / hết SL hợp đồng, SLA mặc định |
| Tích hợp HIS | cấu hình, nhật ký request/response, **Gửi lại**, **Kéo phiếu từ HIS ngay** |
| Kiểm tra toàn vẹn | lịch sử kiểm tra SHA-256 hằng ngày của mọi tệp, **Kiểm tra ngay** |
| Nhật ký hoạt động | lọc theo người, đối tượng, hành động, thời gian; xem dữ liệu trước/sau |

## 6. Câu hỏi thường gặp

- **Không thấy nút Ký?** Bước hiện tại không giao cho bạn (xem khung Luồng ký), hoặc tài liệu đang bị chặn (phiếu không hợp lệ / thiếu hóa đơn) — đọc thông báo màu đỏ/vàng phía trên nút.
- **Sửa hợp đồng đã ký?** Lập **phụ lục** (bổ sung, giảm giá, thay đổi thông tin) — hợp đồng gốc giữ nguyên.
- **Phiếu nhập kho sai trên HIS?** Thủ kho **Trả lại**; HIS sửa và gửi lại cùng mã phiếu, hệ thống cập nhật và trình ký lại. Phiếu đã có chữ ký thì điều chỉnh bằng **phiếu hoàn trả**.
- **Biểu mẫu Excel import?** Tải tại màn hình import (Quyết định thầu / Danh mục hàng hóa) hoặc thư mục `templates/`.
