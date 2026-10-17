namespace BlueContract.Api.Startup;

/// <summary>
/// AUDIT-1 §13.6: in Production the API refuses to start while development switches are on or a secret still has its
/// development default — every problem is listed, so one start reveals everything to fix.
/// </summary>
public static class ProductionGuard
{
    /// <summary>Markers of the development defaults shipped in docker-compose.yml / .env.example.</summary>
    private static readonly string[] DevMarkers = ["dev-only", "change-me", "change_me", "_dev", "-dev-"];

    public static IReadOnlyList<string> Problems(IConfiguration c)
    {
        var problems = new List<string>();
        void Flag(string key, string message)
        {
            if (c.GetValue<bool>(key))
            {
                problems.Add(message);
            }
        }

        void Secret(string key, string name, int minLength = 16)
        {
            var value = c[key] ?? string.Empty;
            if (value.Length < minLength)
            {
                problems.Add($"{name} chưa đặt hoặc quá ngắn (tối thiểu {minLength} ký tự).");
            }
            else if (DevMarkers.Any(m => value.Contains(m, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"{name} vẫn là giá trị mặc định của môi trường phát triển.");
            }
        }

        Flag("Seed:Demo", "Đang bật dữ liệu demo (SEED_DEMO=true).");
        Flag("Seed:AllowReset", "Đang cho phép xóa dữ liệu demo (SEED_ALLOW_RESET=true).");
        Flag("Signing:EnableMockProviders", "Đang bật chữ ký thử nghiệm không có giá trị pháp lý (SIGNING_ENABLE_MOCK=true).");
        Flag("HisInbound:DevEndpoints", "Đang bật endpoint giả lập HIS (HIS_DEV_ENDPOINTS=true).");
        Flag("Hangfire:DashboardEnabled", "Đang mở trang quản trị tác vụ nền Hangfire (HANGFIRE_DASHBOARD=true).");

        Secret("Auth:SigningKey", "Khóa ký phiên đăng nhập (AUTH_SIGNING_KEY)", 32);
        Secret("OnlyOffice:JwtSecret", "Khóa JWT của ONLYOFFICE (ONLYOFFICE_JWT_SECRET)", 32);
        Secret("Storage:SecretKey", "Khóa bí mật kho tệp S3 (S3_SECRET_KEY)");
        foreach (var client in c.GetSection("HisInbound:Clients").GetChildren())
        {
            Secret($"{client.Path}:Secret", $"Khóa HMAC của HIS \"{client["Name"]}\" (HIS_INBOUND_SECRET)", 32);
            Secret($"{client.Path}:ApiKey", $"API key của HIS \"{client["Name"]}\" (HIS_INBOUND_API_KEY)");
        }

        var connection = c.GetConnectionString("Default") ?? string.Empty;
        var password = connection.Split(';').Select(p => p.Split('=', 2)).FirstOrDefault(p => p[0].Trim().Equals("Password", StringComparison.OrdinalIgnoreCase))?.ElementAtOrDefault(1) ?? string.Empty;
        if (password.Length < 12 || DevMarkers.Any(m => password.Contains(m, StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add("Mật khẩu PostgreSQL (POSTGRES_PASSWORD) chưa đặt, quá ngắn hoặc là giá trị mặc định.");
        }

        if (!Uri.TryCreate(c["App:PublicBaseUrl"], UriKind.Absolute, out var site) || site.Scheme != Uri.UriSchemeHttps || site.IsLoopback)
        {
            problems.Add("Địa chỉ công khai của cổng (PUBLIC_BASE_URL) phải là https với tên miền thật.");
        }

        return problems;
    }
}
