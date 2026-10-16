using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Common;
using BlueContract.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Admin;

/// <summary>
/// AUDIT-1 §11.11 / spec §14 "Cấu hình email", "Cấu hình ký số": the administrator sets SMTP and signing options on screen,
/// sends a test email before saving, and every field shown is the one the system really uses.
/// </summary>
[Collection(ApiCollection.Name)]
public class SystemConfigTests(ApiFactory factory)
{
    private const string Mail = "/api/admin/system-config/mail";
    private const string Signing = "/api/admin/system-config/signing";

    [Fact]
    public async Task Admin_tests_and_saves_smtp_settings_used_by_every_email()
    {
        await using var smtp = new FakeSmtpServer();
        using var admin = await factory.LoginAsync("admin");
        var current = await admin.GetFromJsonAsync<JsonElement>(Mail);
        current.GetProperty("isOverridden").GetBoolean().ShouldBeFalse();
        current.GetProperty("hasPassword").GetBoolean().ShouldBeFalse();
        current.TryGetProperty("password", out _).ShouldBeFalse();

        object Settings(int port, string? password, bool enabled = true) => new
        {
            enabled,
            host = "127.0.0.1",
            port,
            useStartTls = false,
            userName = "smtp-user",
            password,
            fromAddress = "mua-sam@benhvien.example.vn",
            fromName = "Phòng Vật tư",
        };

        // "Gửi thư thử" with the values on the form, before they are saved.
        using (var test = await admin.PostAsJsonAsync($"{Mail}/test", new { to = "it@benhvien.example.vn", settings = Settings(smtp.Port, "p@ss") }))
        {
            test.StatusCode.ShouldBe(HttpStatusCode.NoContent, await test.Content.ReadAsStringAsync());
        }

        smtp.Messages.ShouldHaveSingleItem().ShouldContain("it@benhvien.example.vn");
        smtp.Messages[0].ShouldContain("mua-sam@benhvien.example.vn");
        smtp.Logins.ShouldBe(["smtp-user:p@ss"]);

        // A server that does not answer is reported, in Vietnamese, with the reason.
        using (var down = await admin.PostAsJsonAsync($"{Mail}/test", new { to = "it@benhvien.example.vn", settings = Settings(1, "x") }))
        {
            down.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await down.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("MAIL_TEST_FAILED");
        }

        using (var invalid = await admin.PutAsJsonAsync(Mail, new { enabled = true, host = "", port = 70000, useStartTls = false, fromAddress = "not-an-email", fromName = "x" }))
        {
            invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        try
        {
            using (var saved = await admin.PutAsJsonAsync(Mail, Settings(smtp.Port, "s3cret")))
            {
                saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            }

            var after = await admin.GetFromJsonAsync<JsonElement>(Mail);
            after.GetProperty("isOverridden").GetBoolean().ShouldBeTrue();
            after.GetProperty("hasPassword").GetBoolean().ShouldBeTrue();
            after.GetProperty("port").GetInt32().ShouldBe(smtp.Port);
            (await admin.GetStringAsync(new Uri(Mail, UriKind.Relative))).ShouldNotContain("s3cret");

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                (await db.Database.SqlQuery<string>($"SELECT value AS \"Value\" FROM config_overrides").ToListAsync())
                    .ShouldNotContain("s3cret", "the password is stored encrypted");

                // Every email of the system now goes through the saved server, although Email:Enabled is false in appsettings.
                await scope.ServiceProvider.GetRequiredService<IEmailSender>()
                    .SendAsync(new EmailMessage(["ketoan@benhvien.example.vn"], "Thông báo", "<p>x</p>"), CancellationToken.None);
            }

            smtp.Messages.Count.ShouldBe(2);
            smtp.Messages[1].ShouldContain("ketoan@benhvien.example.vn");
            smtp.Logins[^1].ShouldBe("smtp-user:s3cret");

            // Leaving the password empty keeps the saved one (also for the test email).
            using (var kept = await admin.PostAsJsonAsync($"{Mail}/test", new { to = "it@benhvien.example.vn", settings = Settings(smtp.Port, null) }))
            {
                kept.StatusCode.ShouldBe(HttpStatusCode.NoContent, await kept.Content.ReadAsStringAsync());
            }

            smtp.Logins[^1].ShouldBe("smtp-user:s3cret");
        }
        finally
        {
            using var reset = await admin.DeleteAsync(new Uri(Mail, UriKind.Relative));
            reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await admin.GetFromJsonAsync<JsonElement>(Mail)).GetProperty("isOverridden").GetBoolean().ShouldBeFalse();

        using var buyer = await factory.LoginAsync("muasam");
        (await buyer.GetAsync(new Uri(Mail, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await buyer.GetAsync(new Uri(Signing, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_sets_the_default_signing_provider_and_tsa()
    {
        using var admin = await factory.LoginAsync("admin");
        var current = await admin.GetFromJsonAsync<JsonElement>(Signing);
        current.GetProperty("defaultProvider").GetString().ShouldBe("MOCK");
        current.GetProperty("enableMockProviders").GetBoolean().ShouldBeTrue();
        current.GetProperty("providers").EnumerateArray().Select(p => p.GetProperty("code").GetString()).ShouldContain("MOCK_REMOTE");
        current.GetProperty("remoteCas").EnumerateArray().Select(p => p.GetProperty("code").GetString()).ShouldContain("VNPT_SMARTCA");

        object Settings(string provider, string? tsa) => new
        {
            defaultProvider = provider,
            tsaUrl = tsa,
            initialRequiresCa = false,
            mockRemoteDelaySeconds = 0,
            remoteCas = Array.Empty<object>(),
        };

        using (var unknown = await admin.PutAsJsonAsync(Signing, Settings("NOPE", null)))
        {
            unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var badTsa = await admin.PutAsJsonAsync(Signing, Settings("MOCK", "ftp://tsa")))
        {
            badTsa.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        try
        {
            using (var saved = await admin.PutAsJsonAsync(Signing, Settings("MOCK_REMOTE", "http://tsa.example.vn/tsr")))
            {
                saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            }

            var providers = await admin.GetFromJsonAsync<JsonElement>("/api/signing/providers");
            providers.EnumerateArray().Single(p => p.GetProperty("isDefault").GetBoolean()).GetProperty("code").GetString().ShouldBe("MOCK_REMOTE");
            (await admin.GetFromJsonAsync<JsonElement>(Signing)).GetProperty("tsaUrl").GetString().ShouldBe("http://tsa.example.vn/tsr");

            // A remote CA becomes available once its credentials are entered.
            using (var ca = await admin.PutAsJsonAsync(Signing, new
            {
                defaultProvider = "MOCK",
                tsaUrl = (string?)null,
                initialRequiresCa = false,
                mockRemoteDelaySeconds = 0,
                remoteCas = new[] { new { code = "VNPT_SMARTCA", baseUrl = "https://smartca.example.vn/", clientId = "bv", clientSecret = "ca-secret" } },
            }))
            {
                ca.StatusCode.ShouldBe(HttpStatusCode.OK, await ca.Content.ReadAsStringAsync());
            }

            (await admin.GetFromJsonAsync<JsonElement>("/api/signing/providers")).EnumerateArray()
                .Select(p => p.GetProperty("code").GetString()).ShouldContain("VNPT_SMARTCA");
            var signing = await admin.GetStringAsync(new Uri(Signing, UriKind.Relative));
            signing.ShouldNotContain("ca-secret");
            signing.ShouldContain("\"hasSecret\":true");
        }
        finally
        {
            using var reset = await admin.DeleteAsync(new Uri(Signing, UriKind.Relative));
            reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await admin.GetFromJsonAsync<JsonElement>("/api/signing/providers")).EnumerateArray()
            .Single(p => p.GetProperty("isDefault").GetBoolean()).GetProperty("code").GetString().ShouldBe("MOCK");
    }
}
