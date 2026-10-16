using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Common;
using BlueContract.Application.Notifications;
using BlueContract.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Notifications;

/// <summary>
/// AUDIT-1 §11.10 / spec §3 "template email theo sự kiện": every notification event has an email template the admin edits
/// on screen (subject + HTML with placeholders), can preview and reset; emails show times in Vietnam time, never UTC.
/// </summary>
[Collection(ApiCollection.Name)]
public class EmailTemplateTests(ApiFactory factory)
{
    private const string Event = "signing.required";

    [Fact]
    public async Task Admin_edits_previews_and_resets_the_template_of_an_event()
    {
        using var admin = await factory.LoginAsync("admin");
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/admin/email-templates");
        var signing = list.EnumerateArray().Single(t => t.GetProperty("eventType").GetString() == Event);
        signing.GetProperty("isCustomized").GetBoolean().ShouldBeFalse();
        signing.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        signing.GetProperty("placeholders").EnumerateArray().Select(p => p.GetProperty("key").GetString()).ShouldContain("RecipientName");
        list.EnumerateArray().Select(t => t.GetProperty("eventType").GetString()).ShouldContain("account.created");

        // Unknown placeholders and an empty subject are refused.
        using (var bad = await admin.PutAsJsonAsync($"/api/admin/email-templates/{Event}", new { subject = "{{Title}}", bodyHtml = "<p>{{Foo}}</p>" }))
        {
            bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await bad.Content.ReadAsStringAsync()).ShouldContain("Foo");
        }

        using (var empty = await admin.PutAsJsonAsync($"/api/admin/email-templates/{Event}", new { subject = " ", bodyHtml = "<p>x</p>" }))
        {
            empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        try
        {
            using (var saved = await admin.PutAsJsonAsync($"/api/admin/email-templates/{Event}", new
            {
                subject = "[BV] {{Title}}",
                bodyHtml = "<p>Kính gửi {{RecipientName}}</p><p>{{Body}}</p><p><a href=\"{{Link}}\">Mở</a> — {{SentAt}}</p>",
            }))
            {
                saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            }

            using (var preview = await admin.PostAsJsonAsync($"/api/admin/email-templates/{Event}/preview", new
            {
                subject = "[BV] {{Title}}",
                bodyHtml = "<p>{{RecipientName}} — {{SentAt}}</p>",
            }))
            {
                preview.StatusCode.ShouldBe(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
                var p = await preview.Content.ReadFromJsonAsync<JsonElement>();
                p.GetProperty("subject").GetString().ShouldStartWith("[BV] ");
                p.GetProperty("html").GetString()!.ShouldNotContain("{{");
            }

            // A real notification email uses the edited template, encodes values and shows Vietnam time.
            var email = await NotifyAdminAsync("Hợp đồng <số 1>", "Cần ký trước 17h");
            email.Subject.ShouldBe("[BV] Hợp đồng <số 1>");
            email.HtmlBody.ShouldContain("Kính gửi Quản trị");
            email.HtmlBody.ShouldContain("Cần ký trước 17h");
            email.HtmlBody.ShouldContain("/trinh-ky");
            var vietnamNow = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
            email.HtmlBody.ShouldContain(vietnamNow.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture));
            email.HtmlBody.ShouldNotContain("UTC");

            var edited = (await admin.GetFromJsonAsync<JsonElement>("/api/admin/email-templates")).EnumerateArray()
                .Single(t => t.GetProperty("eventType").GetString() == Event);
            edited.GetProperty("isCustomized").GetBoolean().ShouldBeTrue();
            edited.GetProperty("subject").GetString().ShouldBe("[BV] {{Title}}");
        }
        finally
        {
            using var reset = await admin.DeleteAsync(new Uri($"/api/admin/email-templates/{Event}", UriKind.Relative));
            reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        // Back to the built-in template.
        var restored = await NotifyAdminAsync("Hợp đồng số 2", null);
        restored.Subject.ShouldBe("Hợp đồng số 2");
        restored.HtmlBody.ShouldContain("Cổng mua sắm trực tuyến");

        // Only administrators of settings manage templates.
        using var buyer = await factory.LoginAsync("muasam");
        (await buyer.GetAsync(new Uri("/api/admin/email-templates", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Runs the notification service as a use case would and returns the email it queued for "admin".</summary>
    private async Task<EmailMessage> NotifyAdminAsync(string title, string? body)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = await db.Users.Where(u => u.UserName == "admin").Select(u => u.Id).SingleAsync();
        var outbox = scope.ServiceProvider.GetRequiredService<NotificationOutbox>();
        await scope.ServiceProvider.GetRequiredService<NotificationService>()
            .NotifyAsync([adminId], Event, title, body, "/trinh-ky", email: true, CancellationToken.None);
        var email = outbox.PendingEmails.ShouldHaveSingleItem();
        outbox.Clear();
        return email;
    }
}
