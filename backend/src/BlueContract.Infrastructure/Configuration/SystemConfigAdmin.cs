using System.Security.Authentication;
using System.Net.Sockets;
using BlueContract.Application.Admin;
using BlueContract.Application.Auth;
using BlueContract.Application.Common;
using BlueContract.Application.Signing;
using BlueContract.Domain.Settings;
using BlueContract.Infrastructure.Messaging;
using BlueContract.Infrastructure.Persistence;
using BlueContract.Infrastructure.Signing;
using MailKit;
using MailKit.Net.Smtp;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace BlueContract.Infrastructure.Configuration;

/// <summary>Admin screens "Cấu hình email" and "Cấu hình ký số": saves overrides of the Email / Signing sections.</summary>
internal sealed class SystemConfigAdmin(
    AppDbContext db,
    RuntimeSettings settings,
    ISecretProtector protector,
    IEnumerable<ISigningProvider> providers,
    IAuditLogger audit,
    IClock clock) : ISystemConfigAdmin
{
    private const string EmailPrefix = EmailOptions.SectionName + ":";
    private const string SigningPrefix = SigningOptions.SectionName + ":";

    /// <summary>Remote CAs: provider code, display name, configuration sub-section.</summary>
    private static readonly (string Code, string Name, string Section)[] RemoteCas =
    [
        ("VNPT_SMARTCA", "VNPT SmartCA", nameof(SigningOptions.VnptSmartCa)),
        ("VIETTEL_MYSIGN", "Viettel MySign", nameof(SigningOptions.ViettelMySign)),
        ("FPT_CA", "FPT CA", nameof(SigningOptions.FptCa)),
    ];

    private static readonly string[] MockProviders = ["MOCK", "MOCK_REMOTE"];

    // ---------------- Email ----------------

    public Task<MailSettingsDto> GetMailAsync(CancellationToken ct)
    {
        var o = settings.Email();
        return Task.FromResult(new MailSettingsDto(o.Enabled, o.Host, o.Port, o.UseStartTls, o.UserName, !string.IsNullOrEmpty(o.Password),
            o.FromAddress, o.FromName, settings.Overrides().Keys.Any(k => k.StartsWith(EmailPrefix, StringComparison.OrdinalIgnoreCase))));
    }

    public async Task<Result<MailSettingsDto>> SaveMailAsync(SaveMailSettingsRequest r, CancellationToken ct)
    {
        if (ValidateMail(r) is { } invalid)
        {
            return invalid;
        }

        var values = new Dictionary<string, (string Value, bool Secret)>
        {
            [EmailPrefix + nameof(EmailOptions.Enabled)] = (r.Enabled ? "true" : "false", false),
            [EmailPrefix + nameof(EmailOptions.Host)] = (r.Host.Trim(), false),
            [EmailPrefix + nameof(EmailOptions.Port)] = (r.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
            [EmailPrefix + nameof(EmailOptions.UseStartTls)] = (r.UseStartTls ? "true" : "false", false),
            [EmailPrefix + nameof(EmailOptions.UserName)] = (r.UserName?.Trim() ?? string.Empty, false),
            [EmailPrefix + nameof(EmailOptions.FromAddress)] = (r.FromAddress.Trim(), false),
            [EmailPrefix + nameof(EmailOptions.FromName)] = (r.FromName.Trim(), false),
        };
        if (r.Password is not null)
        {
            values[EmailPrefix + nameof(EmailOptions.Password)] = (r.Password, true);
        }

        await SaveAsync(values, ct);
        audit.Log("SystemConfigUpdate", "Email", null, new { r.Enabled, r.Host, r.Port, r.UseStartTls, r.UserName, r.FromAddress, PasswordChanged = r.Password is not null });
        await db.SaveChangesAsync(ct);
        settings.Invalidate();
        return await GetMailAsync(ct);
    }

    public async Task<Result> TestMailAsync(TestMailRequest r, CancellationToken ct)
    {
        if (ValidateMail(r.Settings) is { } invalid)
        {
            return Result.Failure(invalid);
        }

        if (!MailboxAddress.TryParse(r.To, out var to) || !to.Address.Contains('@', StringComparison.Ordinal))
        {
            return Result.Failure(Error.Validation("MAIL_TEST_RECIPIENT_INVALID"));
        }

        var s = r.Settings;
        var options = new EmailOptions
        {
            Enabled = true,
            Host = s.Host.Trim(),
            Port = s.Port,
            UseStartTls = s.UseStartTls,
            UserName = string.IsNullOrWhiteSpace(s.UserName) ? null : s.UserName.Trim(),
            Password = s.Password ?? settings.Email().Password,
            FromAddress = s.FromAddress.Trim(),
            FromName = s.FromName.Trim(),
        };
        var html = $"""
            <p>Đây là thư thử của <b>Cổng mua sắm trực tuyến</b>, gửi lúc {VietnamTime.Format(clock.UtcNow)}.</p>
            <p>Máy chủ: {System.Net.WebUtility.HtmlEncode(options.Host)}:{options.Port}{(options.UseStartTls ? " (STARTTLS)" : string.Empty)}.</p>
            <p>Nếu bạn nhận được thư này, cấu hình gửi email đã đúng.</p>
            """;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await SmtpEmailSender.SendWithAsync(options, new EmailMessage([to.Address], "Thư thử từ Cổng mua sắm trực tuyến", html), timeout.Token);
        }
        catch (Exception e) when (e is SocketException or IOException or ProtocolException or SmtpCommandException or SmtpProtocolException
            or AuthenticationException or MailKit.Security.AuthenticationException or MailKit.Security.SslHandshakeException or OperationCanceledException)
        {
            return Result.Failure(Error.Validation("MAIL_TEST_FAILED") with { Detail = e is OperationCanceledException ? "Hết thời gian chờ máy chủ." : e.Message });
        }

        audit.Log("SystemConfigTestMail", "Email", null, new { To = to.Address, options.Host, options.Port });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task ResetMailAsync(CancellationToken ct) => await ResetAsync(EmailPrefix, "Email", ct);

    // ---------------- Signing ----------------

    public Task<SigningSettingsDto> GetSigningAsync(CancellationToken ct)
    {
        var o = settings.Signing();
        var choices = Choosable(o).Select(p => new SigningProviderOptionDto(p.ProviderCode, p.DisplayName)).ToList();
        var cas = RemoteCas.Select(ca =>
        {
            var c = RemoteCa(o, ca.Section);
            return new RemoteCaSettingsDto(ca.Code, ca.Name, c.BaseUrl?.ToString(), c.ClientId, !string.IsNullOrEmpty(c.ClientSecret));
        }).ToList();
        return Task.FromResult(new SigningSettingsDto(o.DefaultProvider, string.IsNullOrWhiteSpace(o.TsaUrl) ? null : o.TsaUrl, o.InitialRequiresCa,
            o.MockRemoteDelaySeconds, o.EnableMockProviders, choices, cas,
            settings.Overrides().Keys.Any(k => k.StartsWith(SigningPrefix, StringComparison.OrdinalIgnoreCase))));
    }

    public async Task<Result<SigningSettingsDto>> SaveSigningAsync(SaveSigningSettingsRequest r, CancellationToken ct)
    {
        var current = settings.Signing();
        var tsa = r.TsaUrl?.Trim() ?? string.Empty;
        if (tsa.Length > 0 && !IsHttpUrl(tsa))
        {
            return Error.Validation("SIGNING_TSA_URL_INVALID");
        }

        if (r.MockRemoteDelaySeconds is < 0 or > 300)
        {
            return Error.Validation("SIGNING_MOCK_DELAY_INVALID");
        }

        var values = new Dictionary<string, (string Value, bool Secret)>
        {
            [SigningPrefix + nameof(SigningOptions.DefaultProvider)] = (r.DefaultProvider.Trim(), false),
            [SigningPrefix + nameof(SigningOptions.TsaUrl)] = (tsa, false),
            [SigningPrefix + nameof(SigningOptions.InitialRequiresCa)] = (r.InitialRequiresCa ? "true" : "false", false),
            [SigningPrefix + nameof(SigningOptions.MockRemoteDelaySeconds)] = (r.MockRemoteDelaySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
        };

        // Remote CAs in effect after this save, to check that the default provider can really sign.
        var configured = RemoteCas.ToDictionary(ca => ca.Code, ca => RemoteCa(current, ca.Section).IsConfigured);
        foreach (var ca in r.RemoteCas ?? [])
        {
            var known = RemoteCas.FirstOrDefault(x => x.Code == ca.Code);
            if (known.Code is null)
            {
                return Error.Validation("SIGNING_PROVIDER_UNKNOWN");
            }

            var baseUrl = ca.BaseUrl?.Trim() ?? string.Empty;
            if (baseUrl.Length > 0 && !IsHttpUrl(baseUrl))
            {
                return Error.Validation("SIGNING_CA_URL_INVALID");
            }

            var prefix = $"{SigningPrefix}{known.Section}:";
            values[prefix + nameof(RemoteCaOptions.BaseUrl)] = (baseUrl, false);
            values[prefix + nameof(RemoteCaOptions.ClientId)] = (ca.ClientId?.Trim() ?? string.Empty, false);
            var secret = ca.ClientSecret ?? RemoteCa(current, known.Section).ClientSecret ?? string.Empty;
            if (ca.ClientSecret is not null)
            {
                values[prefix + nameof(RemoteCaOptions.ClientSecret)] = (ca.ClientSecret, true);
            }

            configured[ca.Code] = baseUrl.Length > 0 && !string.IsNullOrWhiteSpace(ca.ClientId) && secret.Length > 0;
        }

        var provider = r.DefaultProvider.Trim();
        var selectable = providers.Where(p => p.ProviderCode is not (SigningDefaults.InitialStampProvider or SigningDefaults.OrganizationSealProvider))
            .Select(p => p.ProviderCode).ToHashSet(StringComparer.Ordinal);
        if (!selectable.Contains(provider))
        {
            return Error.Validation("SIGNING_PROVIDER_UNKNOWN");
        }

        if ((MockProviders.Contains(provider) && !current.EnableMockProviders) || (configured.TryGetValue(provider, out var ready) && !ready))
        {
            return Error.Validation("SIGNING_DEFAULT_PROVIDER_UNAVAILABLE");
        }

        await SaveAsync(values, ct);
        audit.Log("SystemConfigUpdate", "Signing", null, new
        {
            DefaultProvider = provider,
            TsaUrl = tsa,
            r.InitialRequiresCa,
            r.MockRemoteDelaySeconds,
            RemoteCas = (r.RemoteCas ?? []).Select(c => new { c.Code, c.BaseUrl, c.ClientId, SecretChanged = c.ClientSecret is not null }),
        });
        await db.SaveChangesAsync(ct);
        settings.Invalidate();
        return await GetSigningAsync(ct);
    }

    public async Task ResetSigningAsync(CancellationToken ct) => await ResetAsync(SigningPrefix, "Signing", ct);

    // ---------------- helpers ----------------

    /// <summary>Providers the admin may choose as default (not the internal ký nháy / đóng dấu ones; mocks only when enabled).</summary>
    private IEnumerable<ISigningProvider> Choosable(SigningOptions o) =>
        providers.Where(p => p.ProviderCode is not (SigningDefaults.InitialStampProvider or SigningDefaults.OrganizationSealProvider)
            && (o.EnableMockProviders || !MockProviders.Contains(p.ProviderCode)));

    private static RemoteCaOptions RemoteCa(SigningOptions o, string section) => section switch
    {
        nameof(SigningOptions.VnptSmartCa) => o.VnptSmartCa,
        nameof(SigningOptions.ViettelMySign) => o.ViettelMySign,
        _ => o.FptCa,
    };

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static Error? ValidateMail(SaveMailSettingsRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Host))
        {
            return Error.Validation("MAIL_HOST_REQUIRED");
        }

        if (r.Port is < 1 or > 65535)
        {
            return Error.Validation("MAIL_PORT_INVALID");
        }

        if (string.IsNullOrWhiteSpace(r.FromName))
        {
            return Error.Validation("MAIL_FROM_NAME_REQUIRED");
        }

        return MailboxAddress.TryParse(r.FromAddress?.Trim() ?? string.Empty, out var from) && from.Address.Contains('@', StringComparison.Ordinal)
            ? null
            : Error.Validation("MAIL_FROM_INVALID");
    }

    private async Task SaveAsync(Dictionary<string, (string Value, bool Secret)> values, CancellationToken ct)
    {
        var keys = values.Keys.ToList();
        var existing = await db.ConfigOverrides.Where(o => keys.Contains(o.Key)).ToDictionaryAsync(o => o.Key, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var (key, (value, secret)) in values)
        {
            var stored = secret ? protector.Protect(value) : value;
            if (existing.TryGetValue(key, out var row))
            {
                row.Update(stored);
            }
            else
            {
                db.ConfigOverrides.Add(new ConfigOverride(key, stored, secret));
            }
        }
    }

    private async Task ResetAsync(string prefix, string section, CancellationToken ct)
    {
        var rows = await db.ConfigOverrides.Where(o => o.Key.StartsWith(prefix)).ToListAsync(ct);
        db.ConfigOverrides.RemoveRange(rows);
        audit.Log("SystemConfigReset", section, null, null);
        await db.SaveChangesAsync(ct);
        settings.Invalidate();
    }
}
