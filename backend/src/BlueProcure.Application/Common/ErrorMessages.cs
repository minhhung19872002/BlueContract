using System.Collections.Frozen;

namespace BlueProcure.Application.Common;

/// <summary>Single place mapping error codes to Vietnamese user-facing messages (spec §15).</summary>
public static class ErrorMessages
{
    private static readonly FrozenDictionary<string, string> Messages = new Dictionary<string, string>
    {
        ["NOT_FOUND"] = "Không tìm thấy dữ liệu yêu cầu.",
        ["FORBIDDEN"] = "Bạn không có quyền thực hiện thao tác này.",
        ["UNAUTHORIZED"] = "Phiên đăng nhập đã hết hạn hoặc không hợp lệ. Vui lòng đăng nhập lại.",
        ["CONFLICT"] = "Dữ liệu đã được người khác thay đổi. Vui lòng tải lại và thử lại.",
        ["VALIDATION_FAILED"] = "Dữ liệu nhập không hợp lệ.",
        ["DOMAIN_ERROR"] = "Thao tác vi phạm quy tắc nghiệp vụ.",
        ["INTERNAL_ERROR"] = "Hệ thống gặp sự cố. Vui lòng thử lại sau hoặc liên hệ quản trị viên.",
        ["SETTING_KEY_REQUIRED"] = "Mã cấu hình không được để trống.",
        ["SETTING_VALUE_INVALID"] = "Giá trị cấu hình không đúng kiểu dữ liệu.",
    }.ToFrozenDictionary();

    public static string Get(string code) =>
        Messages.TryGetValue(code, out var message) ? message : Messages["DOMAIN_ERROR"];

    public static bool IsKnown(string code) => Messages.ContainsKey(code);
}
