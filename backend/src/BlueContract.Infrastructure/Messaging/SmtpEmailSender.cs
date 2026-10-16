using BlueContract.Application.Common;
using BlueContract.Infrastructure.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BlueContract.Infrastructure.Messaging;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public bool Enabled { get; set; } = true;

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    public bool UseStartTls { get; set; }

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = "no-reply@blueprocure.local";

    public string FromName { get; set; } = "Cổng mua sắm trực tuyến";
}

/// <summary>
/// SMTP sender (Mailpit in dev) using the settings in effect (<see cref="RuntimeSettings"/>). Failures are logged and
/// rethrown so callers/jobs can retry.
/// </summary>
internal sealed class SmtpEmailSender(RuntimeSettings settings, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var o = settings.Email();
        if (!o.Enabled)
        {
            logger.LogInformation("Email disabled; skipped '{Subject}' to {To}", message.Subject, string.Join(",", message.To));
            return;
        }

        await SendWithAsync(o, message, ct);
        logger.LogInformation("Sent email '{Subject}' to {Count} recipient(s)", message.Subject, message.To.Count);
    }

    /// <summary>Sends with the given settings, whether email is enabled or not (the admin's "Gửi thư thử").</summary>
    public static async Task SendWithAsync(EmailOptions o, EmailMessage message, CancellationToken ct)
    {
        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        mime.To.AddRange(message.To.Select(MailboxAddress.Parse));
        if (message.Cc is not null)
        {
            mime.Cc.AddRange(message.Cc.Select(MailboxAddress.Parse));
        }

        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient { Timeout = 20_000 };
        await client.ConnectAsync(o.Host, o.Port, o.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);
        if (!string.IsNullOrEmpty(o.UserName))
        {
            await client.AuthenticateAsync(o.UserName, o.Password ?? string.Empty, ct);
        }

        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}

public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Public base URL of the web app, used in email deep links.</summary>
    public Uri PublicBaseUrl { get; set; } = new("http://localhost:47080");
}

internal sealed class AppLinkBuilder(IOptions<AppOptions> options) : IAppLinkBuilder
{
    public Uri Build(string relativePath) => new(options.Value.PublicBaseUrl, relativePath.TrimStart('/'));
}
