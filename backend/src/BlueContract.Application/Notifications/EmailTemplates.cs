using System.Text.RegularExpressions;
using BlueContract.Application.Common;
using BlueContract.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Notifications;

public sealed record EmailPlaceholder(string Key, string Description);

/// <summary>One event that sends email, with its built-in subject/body and the placeholders it fills.</summary>
public sealed record EmailEventDefinition(string EventType, string Name, string Group, string Subject, string BodyHtml, IReadOnlyList<EmailPlaceholder> Placeholders);

public sealed record EmailTemplateDto(
    string EventType,
    string Name,
    string Group,
    string Subject,
    string BodyHtml,
    bool IsCustomized,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<EmailPlaceholder> Placeholders);

public sealed record SaveEmailTemplateRequest(string Subject, string BodyHtml);

public sealed record EmailPreviewDto(string Subject, string Html);

/// <summary>Events that send email and their built-in templates (spec §3 "template email theo sự kiện").</summary>
public static class EmailTemplateCatalog
{
    public const string AccountCreated = "account.created";
    public const string PasswordReset = "account.password_reset";
    public const string SupplierAccount = "supplier.account";

    private const string NotificationGroup = "Thông báo nghiệp vụ";
    private const string AccountGroup = "Tài khoản";

    private static readonly EmailPlaceholder RecipientName = new("RecipientName", "Họ tên người nhận");
    private static readonly EmailPlaceholder OrganizationName = new("OrganizationName", "Tên bệnh viện");
    private static readonly EmailPlaceholder SentAt = new("SentAt", "Thời điểm gửi (giờ Việt Nam, dd/MM/yyyy HH:mm)");

    private static readonly IReadOnlyList<EmailPlaceholder> NotificationPlaceholders =
    [
        RecipientName,
        new("Title", "Tiêu đề thông báo (VD: \"Chờ ký: Hợp đồng số …\")"),
        new("Body", "Nội dung thông báo (có thể trống)"),
        new("Link", "Đường dẫn mở tài liệu trên cổng (có thể trống)"),
        OrganizationName,
        SentAt,
    ];

    private static readonly IReadOnlyList<EmailPlaceholder> AccountPlaceholders =
    [
        RecipientName,
        new("UserName", "Tên đăng nhập"),
        new("Password", "Mật khẩu tạm thời"),
        new("LoginUrl", "Địa chỉ trang đăng nhập"),
        OrganizationName,
        SentAt,
    ];

    private const string NotificationSubject = "{{Title}}";

    private const string NotificationBody = """
        <p>Xin chào {{RecipientName}},</p>
        <p><b>{{Title}}</b></p>
        {{#Body}}<p>{{Body}}</p>{{/Body}}
        {{#Link}}<p><a href="{{Link}}">Mở trên Cổng mua sắm trực tuyến</a></p>{{/Link}}
        <p style="color:#888">Email tự động gửi lúc {{SentAt}}, vui lòng không trả lời.</p>
        """;

    public static readonly IReadOnlyList<EmailEventDefinition> Events =
    [
        Notification(NotificationTypes.SigningRequired, "Có tài liệu chờ ký / xử lý"),
        Notification(NotificationTypes.WorkflowReturned, "Tài liệu bị trả lại"),
        Notification(NotificationTypes.WorkflowCompleted, "Tài liệu đã ký đủ"),
        Notification(NotificationTypes.WorkflowOverdue, "Nhắc việc quá hạn xử lý"),
        Notification(NotificationTypes.WorkflowComment, "Ý kiến mới trên tài liệu"),
        Notification(NotificationTypes.ContractEffective, "Hợp đồng có hiệu lực"),
        Notification(NotificationTypes.ProposalSubmitted, "Khoa gửi phiếu đề xuất"),
        Notification(NotificationTypes.SummaryApproved, "Phiếu tổng hợp được duyệt"),
        Notification(NotificationTypes.CallOrderSent, "Đơn gọi hàng gửi nhà cung cấp"),
        Notification(NotificationTypes.CallOrderConfirmed, "Nhà cung cấp xác nhận gọi hàng"),
        Notification(NotificationTypes.ReceiptCompleted, "Phiếu nhập kho ký đủ"),
        Notification(NotificationTypes.ReceiptReturned, "Phiếu nhập kho bị trả lại"),
        Notification(NotificationTypes.ReceiptInvoice, "Cảnh báo hóa đơn phiếu nhập kho"),
        Notification(NotificationTypes.AcceptanceDrafted, "Lập biên bản nghiệm thu tự động"),
        Notification(NotificationTypes.PaymentCompleted, "Hoàn tất thanh toán"),
        Notification(NotificationTypes.ContractLiquidated, "Hợp đồng đã thanh lý"),
        Notification(NotificationTypes.IntegrityAlert, "Cảnh báo toàn vẹn tài liệu"),
        new(AccountCreated, "Cấp tài khoản người dùng", AccountGroup, "Tài khoản Cổng mua sắm trực tuyến", """
            <p>Xin chào {{RecipientName}},</p>
            <p>Tài khoản của bạn trên Cổng mua sắm trực tuyến của {{OrganizationName}} đã được tạo.</p>
            <ul>
              <li>Tên đăng nhập: <b>{{UserName}}</b></li>
              <li>Mật khẩu tạm thời: <b>{{Password}}</b></li>
            </ul>
            <p>Vui lòng đăng nhập tại <a href="{{LoginUrl}}">{{LoginUrl}}</a> và đổi mật khẩu ngay.</p>
            """, AccountPlaceholders),
        new(PasswordReset, "Đặt lại mật khẩu", AccountGroup, "Đặt lại mật khẩu", """
            <p>Xin chào {{RecipientName}},</p>
            <p>Mật khẩu tài khoản của bạn đã được đặt lại lúc {{SentAt}}.</p>
            <ul>
              <li>Tên đăng nhập: <b>{{UserName}}</b></li>
              <li>Mật khẩu tạm thời: <b>{{Password}}</b></li>
            </ul>
            <p>Vui lòng đăng nhập tại <a href="{{LoginUrl}}">{{LoginUrl}}</a> và đổi mật khẩu ngay.</p>
            """, AccountPlaceholders),
        new(SupplierAccount, "Cấp tài khoản nhà cung cấp", AccountGroup, "Thông tin tài khoản Cổng mua sắm trực tuyến", """
            <p>Kính gửi {{RecipientName}} ({{SupplierName}}),</p>
            <p>{{OrganizationName}} đã cấp tài khoản truy cập <b>Cổng Quản lý &amp; Thanh toán Mua sắm Hàng hóa Trực tuyến</b> cho Quý công ty.</p>
            <ul>
              <li>Tên đăng nhập: <b>{{UserName}}</b></li>
              <li>Mật khẩu tạm thời: <b>{{Password}}</b></li>
            </ul>
            <p>Vui lòng đăng nhập tại <a href="{{LoginUrl}}">{{LoginUrl}}</a> và đổi mật khẩu ngay trong lần đăng nhập đầu tiên.</p>
            <p>Trân trọng.</p>
            """, [.. AccountPlaceholders, new("SupplierName", "Tên nhà cung cấp")]),
    ];

    /// <summary>Template of an event; unknown notification types fall back to the generic notification email.</summary>
    public static EmailEventDefinition Find(string eventType) =>
        Events.FirstOrDefault(e => e.EventType == eventType) ?? Notification(eventType, "Thông báo");

    public static bool Exists(string eventType) => Events.Any(e => e.EventType == eventType);

    private static EmailEventDefinition Notification(string type, string name) =>
        new(type, name, NotificationGroup, NotificationSubject, NotificationBody, NotificationPlaceholders);
}

/// <summary>
/// Fills <c>{{Key}}</c> placeholders: HTML-encoded in the body, as is in the subject. <c>{{#Key}}…{{/Key}}</c> keeps its
/// content only when the value is not empty.
/// </summary>
public static partial class EmailTemplateText
{
    [GeneratedRegex(@"\{\{\s*([#/]?)\s*([A-Za-z][A-Za-z0-9]*)\s*\}\}")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\{\{#([A-Za-z][A-Za-z0-9]*)\}\}(.*?)\{\{/\1\}\}", RegexOptions.Singleline)]
    private static partial Regex Section();

    /// <summary>Placeholders used in the text that the event does not provide (sections included).</summary>
    public static IReadOnlyList<string> UnknownPlaceholders(string text, IEnumerable<string> allowed)
    {
        var known = allowed.ToHashSet(StringComparer.Ordinal);
        return [.. Tag().Matches(text).Select(m => m.Groups[2].Value).Where(k => !known.Contains(k)).Distinct()];
    }

    public static string Render(string text, IReadOnlyDictionary<string, string?> values, bool html)
    {
        var normalized = Tag().Replace(text, m => $"{{{{{m.Groups[1].Value}{m.Groups[2].Value}}}}}");
        var withSections = Section().Replace(normalized, m =>
            string.IsNullOrEmpty(values.GetValueOrDefault(m.Groups[1].Value)) ? string.Empty : m.Groups[2].Value);
        return Tag().Replace(withSections, m =>
        {
            if (m.Groups[1].Value.Length > 0)
            {
                return string.Empty;
            }

            var value = values.GetValueOrDefault(m.Groups[2].Value) ?? string.Empty;
            return html ? Encode(value) : value;
        });
    }

    /// <summary>Escapes markup only, so Vietnamese letters stay readable in the HTML source.</summary>
    private static string Encode(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&#39;", StringComparison.Ordinal);
}

/// <summary>Renders the email of an event from the admin's template or the built-in one.</summary>
public sealed class EmailTemplateRenderer(IAppDbContext db, IClock clock)
{
    public async Task<EmailMessage> RenderAsync(string eventType, IEnumerable<string> to, IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        var custom = await db.EmailTemplates.AsNoTracking().Where(t => t.EventType == eventType)
            .Select(t => new { t.Subject, t.BodyHtml }).FirstOrDefaultAsync(ct);
        var builtIn = EmailTemplateCatalog.Find(eventType);
        var all = await WithCommonValuesAsync(values, ct);
        return new EmailMessage([.. to],
            EmailTemplateText.Render(custom?.Subject ?? builtIn.Subject, all, html: false).Trim(),
            EmailTemplateText.Render(custom?.BodyHtml ?? builtIn.BodyHtml, all, html: true));
    }

    /// <summary>Adds OrganizationName and SentAt (Vietnam time) unless the caller set them.</summary>
    public async Task<Dictionary<string, string?>> WithCommonValuesAsync(IReadOnlyDictionary<string, string?> values, CancellationToken ct)
    {
        var all = new Dictionary<string, string?>(values, StringComparer.Ordinal);
        if (!all.ContainsKey("OrganizationName"))
        {
            all["OrganizationName"] = await db.Organizations.AsNoTracking().Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "Bệnh viện";
        }

        all.TryAdd("SentAt", VietnamTime.Format(clock.UtcNow));
        return all;
    }
}

/// <summary>Admin screen "Mẫu email": list, edit, preview, reset to the built-in template.</summary>
public sealed class EmailTemplateService(IAppDbContext db, EmailTemplateRenderer renderer, IAppLinkBuilder links, IAuditLogger audit)
{
    public async Task<List<EmailTemplateDto>> ListAsync(CancellationToken ct)
    {
        var custom = await db.EmailTemplates.AsNoTracking().ToDictionaryAsync(t => t.EventType, ct);
        return [.. EmailTemplateCatalog.Events.Select(e => custom.TryGetValue(e.EventType, out var t)
            ? new EmailTemplateDto(e.EventType, e.Name, e.Group, t.Subject, t.BodyHtml, true, t.UpdatedAt ?? t.CreatedAt, e.Placeholders)
            : new EmailTemplateDto(e.EventType, e.Name, e.Group, e.Subject, e.BodyHtml, false, null, e.Placeholders))];
    }

    public async Task<Result<EmailTemplateDto>> SaveAsync(string eventType, SaveEmailTemplateRequest r, CancellationToken ct)
    {
        if (!EmailTemplateCatalog.Exists(eventType))
        {
            return Error.NotFound();
        }

        var invalid = Validate(eventType, r);
        if (invalid is not null)
        {
            return invalid;
        }

        var template = await db.EmailTemplates.FirstOrDefaultAsync(t => t.EventType == eventType, ct);
        if (template is null)
        {
            template = new EmailTemplate(eventType, r.Subject, r.BodyHtml);
            db.EmailTemplates.Add(template);
        }
        else
        {
            template.Update(r.Subject, r.BodyHtml);
        }

        audit.Log("EmailTemplateUpdate", nameof(EmailTemplate), eventType, new { template.Subject });
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).Single(t => t.EventType == eventType);
    }

    public async Task<Result> ResetAsync(string eventType, CancellationToken ct)
    {
        if (!EmailTemplateCatalog.Exists(eventType))
        {
            return Result.Failure(Error.NotFound());
        }

        var template = await db.EmailTemplates.FirstOrDefaultAsync(t => t.EventType == eventType, ct);
        if (template is not null)
        {
            db.EmailTemplates.Remove(template);
            audit.Log("EmailTemplateReset", nameof(EmailTemplate), eventType, null);
            await db.SaveChangesAsync(ct);
        }

        return Result.Success();
    }

    /// <summary>Renders unsaved text with sample values, so the admin sees the email before saving.</summary>
    public async Task<Result<EmailPreviewDto>> PreviewAsync(string eventType, SaveEmailTemplateRequest r, CancellationToken ct)
    {
        if (!EmailTemplateCatalog.Exists(eventType))
        {
            return Error.NotFound();
        }

        var invalid = Validate(eventType, r);
        if (invalid is not null)
        {
            return invalid;
        }

        var values = await renderer.WithCommonValuesAsync(new Dictionary<string, string?>
        {
            ["RecipientName"] = "Nguyễn Văn A",
            ["Title"] = "Chờ ký: Hợp đồng số 12/2026/HĐ-BV",
            ["Body"] = "Bước \"Giám đốc ký\" của hợp đồng đang chờ bạn xử lý.",
            ["Link"] = links.Build("/trinh-ky").ToString(),
            ["UserName"] = "nguyenvana",
            ["Password"] = "Tam@1234",
            ["LoginUrl"] = links.Build("/dang-nhap").ToString(),
            ["SupplierName"] = "Công ty TNHH Dược phẩm Minh An",
        }, ct);
        return new EmailPreviewDto(EmailTemplateText.Render(r.Subject, values, html: false).Trim(), EmailTemplateText.Render(r.BodyHtml, values, html: true));
    }

    private static Error? Validate(string eventType, SaveEmailTemplateRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Subject))
        {
            return Error.Validation("EMAIL_TEMPLATE_SUBJECT_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(r.BodyHtml))
        {
            return Error.Validation("EMAIL_TEMPLATE_BODY_REQUIRED");
        }

        var allowed = EmailTemplateCatalog.Find(eventType).Placeholders.Select(p => p.Key).ToList();
        var unknown = EmailTemplateText.UnknownPlaceholders(r.Subject + "\n" + r.BodyHtml, allowed);
        return unknown.Count == 0
            ? null
            : Error.Validation("EMAIL_TEMPLATE_PLACEHOLDER_UNKNOWN") with { Detail = string.Join(", ", unknown.Select(u => $"{{{{{u}}}}}")) };
    }
}
