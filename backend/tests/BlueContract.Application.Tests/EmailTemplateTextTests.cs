using BlueContract.Application.Common;
using BlueContract.Application.Notifications;
using Shouldly;

namespace BlueContract.Application.Tests.Notifications;

public class EmailTemplateTextTests
{
    private static readonly Dictionary<string, string?> Values = new()
    {
        ["Title"] = "HĐ <01> & \"phụ lục\"",
        ["Body"] = null,
        ["Link"] = "https://cong.example.vn/trinh-ky",
    };

    [Fact]
    public void Body_values_are_escaped_but_vietnamese_stays_readable() =>
        EmailTemplateText.Render("<b>{{Title}}</b>", Values, html: true).ShouldBe("<b>HĐ &lt;01&gt; &amp; &quot;phụ lục&quot;</b>");

    [Fact]
    public void Subject_values_are_plain_text() =>
        EmailTemplateText.Render("[BV] {{ Title }}", Values, html: false).ShouldBe("[BV] HĐ <01> & \"phụ lục\"");

    [Fact]
    public void Sections_are_kept_only_for_non_empty_values() =>
        EmailTemplateText.Render("{{#Body}}<p>{{Body}}</p>{{/Body}}{{#Link}}<a href=\"{{Link}}\">Mở</a>{{/Link}}", Values, html: true)
            .ShouldBe("<a href=\"https://cong.example.vn/trinh-ky\">Mở</a>");

    [Fact]
    public void Unknown_placeholders_are_reported_once() =>
        EmailTemplateText.UnknownPlaceholders("{{Title}} {{Foo}} {{#Bar}}x{{/Bar}} {{Foo}}", ["Title"]).ShouldBe(["Foo", "Bar"]);

    [Fact]
    public void Every_event_template_uses_only_its_own_placeholders()
    {
        foreach (var e in EmailTemplateCatalog.Events)
        {
            EmailTemplateText.UnknownPlaceholders(e.Subject + e.BodyHtml, e.Placeholders.Select(p => p.Key)).ShouldBeEmpty(e.EventType);
        }

        EmailTemplateCatalog.Events.Select(e => e.EventType).ShouldBeUnique();
        typeof(NotificationTypes).GetFields().Select(f => (string)f.GetValue(null)!)
            .ShouldAllBe(t => EmailTemplateCatalog.Exists(t), "every notification type has an editable email template");
    }

    [Fact]
    public void Times_are_shown_in_vietnam_time() =>
        VietnamTime.Format(new DateTimeOffset(2026, 10, 16, 18, 30, 0, TimeSpan.Zero)).ShouldBe("17/10/2026 01:30");
}
