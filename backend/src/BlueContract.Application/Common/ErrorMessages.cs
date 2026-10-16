using System.Collections.Frozen;

namespace BlueContract.Application.Common;

/// <summary>Single place mapping error codes to Vietnamese user-facing messages (spec §15).</summary>
public static class ErrorMessages
{
    private static readonly FrozenDictionary<string, string> Messages = new Dictionary<string, string>
    {
        // Generic
        ["NOT_FOUND"] = "Không tìm thấy dữ liệu yêu cầu.",
        ["FORBIDDEN"] = "Bạn không có quyền thực hiện thao tác này.",
        ["UNAUTHORIZED"] = "Phiên đăng nhập đã hết hạn hoặc không hợp lệ. Vui lòng đăng nhập lại.",
        ["CONFLICT"] = "Dữ liệu đã được người khác thay đổi. Vui lòng tải lại và thử lại.",
        ["VALIDATION_FAILED"] = "Dữ liệu nhập không hợp lệ.",
        ["DOMAIN_ERROR"] = "Thao tác vi phạm quy tắc nghiệp vụ.",
        ["INTERNAL_ERROR"] = "Hệ thống gặp sự cố. Vui lòng thử lại sau hoặc liên hệ quản trị viên.",
        ["TOO_MANY_REQUESTS"] = "Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút.",
        ["REQUEST_CANCELLED"] = "Yêu cầu đã bị hủy.",

        // Settings
        ["SETTING_KEY_REQUIRED"] = "Mã cấu hình không được để trống.",
        ["SETTING_VALUE_INVALID"] = "Giá trị cấu hình không đúng kiểu dữ liệu.",

        // Auth
        ["INVALID_CREDENTIALS"] = "Tên đăng nhập hoặc mật khẩu không đúng.",
        ["ACCOUNT_LOCKED"] = "Tài khoản tạm thời bị khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau hoặc liên hệ quản trị viên.",
        ["ACCOUNT_INACTIVE"] = "Tài khoản đã bị vô hiệu hóa.",
        ["SESSION_EXPIRED"] = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.",
        ["TWO_FACTOR_INVALID"] = "Mã xác thực không đúng hoặc đã hết hạn.",
        ["TWO_FACTOR_CHALLENGE_EXPIRED"] = "Phiên xác thực 2 lớp đã hết hạn. Vui lòng đăng nhập lại.",
        ["TWO_FACTOR_ALREADY_ENABLED"] = "Xác thực 2 lớp đã được bật.",
        ["TWO_FACTOR_NOT_SETUP"] = "Chưa thiết lập xác thực 2 lớp.",
        ["TWO_FACTOR_SECRET_REQUIRED"] = "Thiếu khóa bí mật xác thực 2 lớp.",
        ["CURRENT_PASSWORD_INVALID"] = "Mật khẩu hiện tại không đúng.",
        ["PASSWORD_TOO_WEAK"] = "Mật khẩu phải có ít nhất 8 ký tự, gồm chữ hoa, chữ thường, chữ số và ký tự đặc biệt.",
        ["PASSWORD_CHANGE_REQUIRED"] = "Bạn cần đổi mật khẩu trước khi tiếp tục.",

        // Organization & master data
        ["ORGANIZATION_NOT_CONFIGURED"] = "Chưa cấu hình thông tin bệnh viện.",
        ["ORG_NAME_REQUIRED"] = "Tên bệnh viện không được để trống.",
        ["ORG_TAX_CODE_REQUIRED"] = "Mã số thuế bệnh viện không được để trống.",
        ["FACILITY_CODE_REQUIRED"] = "Mã cơ sở không được để trống.",
        ["FACILITY_NAME_REQUIRED"] = "Tên cơ sở không được để trống.",
        ["FACILITY_CODE_EXISTS"] = "Mã cơ sở đã tồn tại.",
        ["FACILITY_NOT_FOUND"] = "Cơ sở không tồn tại.",
        ["DEPARTMENT_CODE_REQUIRED"] = "Mã khoa/phòng không được để trống.",
        ["DEPARTMENT_NAME_REQUIRED"] = "Tên khoa/phòng không được để trống.",
        ["DEPARTMENT_CODE_EXISTS"] = "Mã khoa/phòng đã tồn tại.",
        ["WAREHOUSE_CODE_REQUIRED"] = "Mã kho không được để trống.",
        ["WAREHOUSE_NAME_REQUIRED"] = "Tên kho không được để trống.",
        ["WAREHOUSE_CODE_EXISTS"] = "Mã kho đã tồn tại.",

        // Suppliers
        ["SUPPLIER_NAME_REQUIRED"] = "Tên nhà cung cấp không được để trống.",
        ["SUPPLIER_TAX_CODE_REQUIRED"] = "Mã số thuế nhà cung cấp không được để trống.",
        ["SUPPLIER_TAX_CODE_INVALID"] = "Mã số thuế không hợp lệ (10 chữ số hoặc dạng 0123456789-001).",
        ["SUPPLIER_TAX_CODE_EXISTS"] = "Mã số thuế nhà cung cấp đã tồn tại.",
        ["SUPPLIER_NOT_FOUND"] = "Nhà cung cấp không tồn tại.",

        // Users & roles
        ["USER_NAME_REQUIRED"] = "Tên đăng nhập không được để trống.",
        ["USER_NAME_INVALID"] = "Tên đăng nhập 3–64 ký tự, chỉ gồm chữ, số, dấu chấm, gạch dưới, gạch ngang.",
        ["USER_NAME_EXISTS"] = "Tên đăng nhập đã tồn tại.",
        ["USER_FULL_NAME_REQUIRED"] = "Họ tên không được để trống.",
        ["USER_SUPPLIER_REQUIRED"] = "Tài khoản nhà cung cấp phải gắn với một nhà cung cấp.",
        ["USER_PASSWORD_REQUIRED"] = "Mật khẩu không được để trống.",
        ["ROLE_NOT_FOUND"] = "Vai trò không tồn tại.",
        ["ROLE_CODE_REQUIRED"] = "Mã vai trò không được để trống.",
        ["ROLE_NAME_REQUIRED"] = "Tên vai trò không được để trống.",
        ["ROLE_CODE_EXISTS"] = "Mã vai trò đã tồn tại.",
        ["ROLE_SYSTEM_CANNOT_DELETE"] = "Không thể xóa vai trò mặc định của hệ thống.",
        ["ROLE_IN_USE"] = "Vai trò đang được gán cho người dùng, không thể xóa.",
        ["ROLE_USER_TYPE_MISMATCH"] = "Vai trò không phù hợp loại tài khoản (vai trò NCC chỉ dành cho tài khoản nhà cung cấp và ngược lại).",
        ["PERMISSION_UNKNOWN"] = "Quyền không tồn tại trong hệ thống.",
        ["DATA_SCOPE_VALUE_REQUIRED"] = "Giá trị phạm vi dữ liệu không được để trống.",
        ["CANNOT_DEMOTE_SELF"] = "Bạn không thể tự khóa hoặc tự gỡ quyền quản trị của chính mình.",

        // Delegation
        ["DELEGATION_SAME_USER"] = "Người được ủy quyền phải khác người ủy quyền.",
        ["DELEGATION_INVALID_PERIOD"] = "Thời gian kết thúc ủy quyền phải sau thời gian bắt đầu.",
        ["DELEGATION_TITLE_REQUIRED"] = "Chức danh không được để trống.",
        ["DELEGATION_ALREADY_REVOKED"] = "Ủy quyền đã được thu hồi trước đó.",
        ["DELEGATION_NOT_OWNER"] = "Bạn chỉ được ủy quyền ký của chính mình.",
        ["DELEGATION_USER_INVALID"] = "Người ủy quyền và người được ủy quyền phải là tài khoản nội bộ đang hoạt động.",
        ["DELEGATION_OVERLAP"] = "Đã có ủy quyền khác của người này trong khoảng thời gian đã chọn.",

        // Templates & documents
        ["FILE_TOO_LARGE"] = "Tệp vượt quá dung lượng cho phép (20 MB).",
        ["FILE_REQUIRED"] = "Vui lòng chọn tệp tải lên.",
        ["TEMPLATE_CODE_REQUIRED"] = "Mã mẫu biểu không được để trống.",
        ["TEMPLATE_NAME_REQUIRED"] = "Tên mẫu biểu không được để trống.",
        ["TEMPLATE_CODE_EXISTS"] = "Mã mẫu biểu đã tồn tại.",
        ["TEMPLATE_NOT_FOUND"] = "Không tìm thấy mẫu biểu.",
        ["TEMPLATE_VERSION_NOT_FOUND"] = "Không tìm thấy phiên bản mẫu biểu.",
        ["TEMPLATE_NOT_DOCX"] = "Tệp mẫu phải là tài liệu Word (.docx) hợp lệ.",
        ["TEMPLATE_PLACEHOLDERS_INVALID"] = "Mẫu có placeholder không hợp lệ. Vui lòng kiểm tra danh sách lỗi trước khi tải lên.",
        ["DOCUMENT_TITLE_REQUIRED"] = "Tiêu đề tài liệu không được để trống.",
        ["DOCUMENT_OWNER_REQUIRED"] = "Tài liệu phải gắn với một hồ sơ nghiệp vụ.",
        ["DOCUMENT_VERSION_REASON_REQUIRED"] = "Phải nêu lý do tạo phiên bản.",
        ["DOCUMENT_CLOSED"] = "Tài liệu đã hoàn thành hoặc đã hủy, không thể thay đổi.",
        ["DOCUMENT_VERSION_MISMATCH"] = "Phiên bản không thuộc tài liệu này.",
        ["DOCUMENT_VERSION_LOCKED"] = "Phiên bản đã có chữ ký số nên không thể sửa. Hãy tạo phiên bản mới (luồng ký sẽ bắt đầu lại).",
        ["DOCUMENT_PDF_REQUIRED"] = "Phiên bản chưa có bản PDF để ký.",
        ["DOCUMENT_DOCX_REQUIRED"] = "Phiên bản không có tệp Word để chỉnh sửa hoặc chuyển PDF.",
        ["DOCUMENT_FILE_NOT_FOUND"] = "Phiên bản không có tệp ở định dạng yêu cầu.",
        ["DOCUMENT_FILE_TYPE_INVALID"] = "Chỉ chấp nhận tệp Word (.docx) hoặc PDF.",
        ["EDITOR_CALLBACK_INVALID"] = "Yêu cầu lưu từ trình soạn thảo không hợp lệ.",
        ["PDF_CONVERSION_FAILED"] = "Không chuyển được tài liệu sang PDF. Vui lòng thử lại sau.",
    }.ToFrozenDictionary();

    public static string Get(string code) =>
        Messages.TryGetValue(code, out var message) ? message : Messages["DOMAIN_ERROR"];

    public static bool IsKnown(string code) => Messages.ContainsKey(code);
}
