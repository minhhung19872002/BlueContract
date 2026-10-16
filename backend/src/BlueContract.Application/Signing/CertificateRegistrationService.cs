using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using BlueContract.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Signing;

/// <summary>Registered signing certificate of a user; nulls when none is registered.</summary>
public sealed record RegisteredCertificateDto(string? Serial, string? Subject, string? Issuer, DateTimeOffset? ValidTo, bool IsExpired, bool ExpiresSoon);

/// <summary>
/// "Đăng ký chứng thư số" (spec §9.2-7): the user uploads the public certificate (.cer / .crt / .pem) of the token or
/// remote-signing account they sign with. A supplier user's certificate must carry the company's tax code when it names
/// one (organisation certificates of Vietnamese CAs put "MST:…" in the subject).
/// </summary>
public sealed partial class CertificateRegistrationService(IAppDbContext db, ICurrentUser current, IClock clock, IAuditLogger audit)
{
    private const int MaxCertificateBytes = 64 * 1024;
    private static readonly TimeSpan ExpiryWarning = TimeSpan.FromDays(30);

    [GeneratedRegex(@"MST\s*[:=]?\s*(\d{10}(?:-\d{3})?)", RegexOptions.IgnoreCase)]
    private static partial Regex TaxCodeInSubject();

    public async Task<RegisteredCertificateDto> GetMineAsync(CancellationToken ct)
    {
        var me = current.UserId!.Value;
        var u = await db.Users.AsNoTracking().Where(x => x.Id == me)
            .Select(x => new { x.CertificateSerial, x.CertificateSubject, x.CertificateIssuer, x.CertificateValidTo }).FirstAsync(ct);
        var now = clock.UtcNow;
        return new RegisteredCertificateDto(u.CertificateSerial, u.CertificateSubject, u.CertificateIssuer, u.CertificateValidTo,
            u.CertificateValidTo < now, u.CertificateValidTo >= now && u.CertificateValidTo < now + ExpiryWarning);
    }

    public async Task<Result<RegisteredCertificateDto>> RegisterMineAsync(UploadedFile file, CancellationToken ct)
    {
        if (file.Content.Length > MaxCertificateBytes || Load(file.Content) is not { } cert)
        {
            return Error.Validation("ACCOUNT_CERTIFICATE_INVALID");
        }

        using (cert)
        {
            var now = clock.UtcNow;
            if (now > cert.NotAfter.ToUniversalTime() || now < cert.NotBefore.ToUniversalTime())
            {
                return Error.Validation("ACCOUNT_CERTIFICATE_EXPIRED");
            }

            var me = current.UserId!.Value;
            var user = await db.Users.FirstAsync(u => u.Id == me, ct);
            if (user.SupplierId is { } supplierId && TaxCodeInSubject().Match(cert.Subject) is { Success: true } match)
            {
                var taxCode = await db.Suppliers.IgnoreQueryFilters().Where(s => s.Id == supplierId).Select(s => s.TaxCode).FirstAsync(ct);
                if (!string.Equals(match.Groups[1].Value, taxCode, StringComparison.Ordinal))
                {
                    return Error.Validation("ACCOUNT_CERTIFICATE_TAX_CODE_MISMATCH");
                }
            }

            user.RegisterCertificate(cert.SerialNumber, cert.Issuer, cert.NotAfter.ToUniversalTime(), cert.Subject);
            audit.Log("CertificateRegister", "User", me.ToString(), new { cert.SerialNumber, cert.Subject, cert.Issuer, ValidTo = cert.NotAfter.ToUniversalTime() });
            await db.SaveChangesAsync(ct);
        }

        return await GetMineAsync(ct);
    }

    public async Task<Result> RemoveMineAsync(CancellationToken ct)
    {
        var me = current.UserId!.Value;
        var user = await db.Users.FirstAsync(u => u.Id == me, ct);
        user.RegisterCertificate(null, null, null);
        audit.Log("CertificateRemove", "User", me.ToString());
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// A certificate presented for signing (USB token) must be the registered one when the user registered any;
    /// throws <c>SIGNING_CERTIFICATE_NOT_REGISTERED</c> otherwise.
    /// </summary>
    public async Task EnsureRegisteredAsync(Guid userId, byte[] certificate, CancellationToken ct)
    {
        var registered = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.CertificateSerial).FirstOrDefaultAsync(ct);
        if (registered is null)
        {
            return;
        }

        using var cert = Load(certificate);
        if (cert is null || !SameSerial(cert.SerialNumber, registered))
        {
            throw new Domain.Common.DomainException("SIGNING_CERTIFICATE_NOT_REGISTERED");
        }
    }

    private static bool SameSerial(string a, string b) =>
        string.Equals(a.TrimStart('0'), b.TrimStart('0'), StringComparison.OrdinalIgnoreCase);

    /// <summary>DER (.cer / .crt) or PEM; null when the bytes are not a certificate.</summary>
    private static X509Certificate2? Load(byte[] content)
    {
        try
        {
            var text = System.Text.Encoding.ASCII.GetString(content);
            return text.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal)
                ? X509Certificate2.CreateFromPem(text)
                : X509CertificateLoader.LoadCertificate(content);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
