using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BlueContract.Application.Auth;
using BlueContract.Application.Common;
using BlueContract.Application.Signing;
using BlueContract.Domain.Common;
using BlueContract.Domain.Workflows;
using BlueContract.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Infrastructure.Signing;

/// <summary>
/// Development/demo certificates for <see cref="MockSigningProvider"/> (spec §8.2): a self-signed RSA-2048 certificate per
/// user and one organisation certificate for the hospital e-seal. Private keys are stored encrypted (Data Protection).
/// </summary>
internal sealed class CertificateStore(AppDbContext db, ISecretProtector protector, IClock clock, IAuditLogger audit) : IOrganizationCertificates
{
    public const string MockProvider = "MOCK";

    /// <summary>Provider tag of the certificate installed by the administrator for the e-seal.</summary>
    private const string InstalledProvider = SigningDefaults.OrganizationSealProvider;

    public async Task<Result<OrganizationCertificateDto>> InstallAsync(byte[] pkcs12, string? password, CancellationToken ct)
    {
        X509Certificate2 cert;
        try
        {
            cert = X509CertificateLoader.LoadPkcs12(pkcs12, password, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
        }
        catch (CryptographicException)
        {
            return Error.Validation("ORG_CERTIFICATE_INVALID");
        }

        using (cert)
        {
            using var key = cert.GetRSAPrivateKey();
            if (key is null)
            {
                return Error.Validation("ORG_CERTIFICATE_INVALID");
            }

            var now = clock.UtcNow.UtcDateTime;
            if (cert.NotAfter.ToUniversalTime() <= now || cert.NotBefore.ToUniversalTime() > now)
            {
                return Error.Validation("ORG_CERTIFICATE_EXPIRED");
            }

            // Stored without the file password, encrypted with Data Protection like every other signing key.
            var protectedPfx = protector.Protect(Convert.ToBase64String(cert.Export(X509ContentType.Pfx)));
            db.SigningCertificates.RemoveRange(await db.SigningCertificates.Where(c => c.IsOrganization && c.Provider == InstalledProvider).ToListAsync(ct));
            db.SigningCertificates.Add(new SigningCertificate(null, true, InstalledProvider, cert.Subject, cert.SerialNumber, cert.Issuer,
                cert.NotBefore.ToUniversalTime(), cert.NotAfter.ToUniversalTime(), protectedPfx));
            audit.Log("OrganizationCertificateInstall", "Organization", null, new { cert.Subject, cert.SerialNumber, NotAfter = cert.NotAfter.ToUniversalTime() });
            await db.SaveChangesAsync(ct);
            return new OrganizationCertificateDto(cert.Subject, cert.Issuer, cert.SerialNumber, cert.NotBefore.ToUniversalTime(), cert.NotAfter.ToUniversalTime(),
                cert.Subject == cert.Issuer);
        }
    }

    public async Task<OrganizationCertificateDto?> GetAsync(CancellationToken ct) =>
        await db.SigningCertificates.AsNoTracking().Where(c => c.IsOrganization && c.Provider == InstalledProvider)
            .Select(c => new OrganizationCertificateDto(c.Subject, c.Issuer, c.SerialNumber, c.NotBefore, c.NotAfter, c.Subject == c.Issuer))
            .FirstOrDefaultAsync(ct);

    public async Task RemoveAsync(CancellationToken ct)
    {
        var installed = await db.SigningCertificates.Where(c => c.IsOrganization && c.Provider == InstalledProvider).ToListAsync(ct);
        if (installed.Count > 0)
        {
            db.SigningCertificates.RemoveRange(installed);
            audit.Log("OrganizationCertificateRemove", "Organization", null, new { installed[0].SerialNumber });
            await db.SaveChangesAsync(ct);
        }
    }

    public Task<bool> IsInstalledAsync(CancellationToken ct) =>
        db.SigningCertificates.AnyAsync(c => c.IsOrganization && c.Provider == InstalledProvider, ct);

    /// <summary>The installed hospital certificate with its key; fails when none is installed or it has expired.</summary>
    public async Task<X509Certificate2> GetInstalledOrganizationCertificateAsync(CancellationToken ct)
    {
        var installed = await db.SigningCertificates.AsNoTracking().Where(c => c.IsOrganization && c.Provider == InstalledProvider)
            .FirstOrDefaultAsync(ct) ?? throw new DomainException("ORG_CERTIFICATE_NOT_INSTALLED");
        return installed.NotAfter <= clock.UtcNow ? throw new DomainException("ORG_CERTIFICATE_EXPIRED") : Load(installed);
    }

    public async Task<X509Certificate2> GetUserCertificateAsync(Guid userId, CancellationToken ct)
    {
        var existing = await db.SigningCertificates.AsNoTracking()
            .Where(c => c.UserId == userId && !c.IsOrganization && c.Provider == MockProvider && c.NotAfter > clock.UtcNow)
            .OrderByDescending(c => c.NotAfter).FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            return Load(existing);
        }

        var user = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.FullName, u.Email, u.SupplierId }).FirstAsync(ct);
        var orgName = user.SupplierId is { } sid
            ? await db.Suppliers.IgnoreQueryFilters().Where(s => s.Id == sid).Select(s => s.Name).FirstOrDefaultAsync(ct)
            : await db.Organizations.Select(o => o.Name).FirstOrDefaultAsync(ct);
        return await CreateAsync(userId, isOrganization: false, user.FullName, orgName, user.Email, ct);
    }

    public async Task<X509Certificate2> GetOrganizationCertificateAsync(CancellationToken ct)
    {
        var existing = await db.SigningCertificates.AsNoTracking()
            .Where(c => c.IsOrganization && c.Provider == MockProvider && c.NotAfter > clock.UtcNow)
            .OrderByDescending(c => c.NotAfter).FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            return Load(existing);
        }

        var org = await db.Organizations.Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "Bệnh viện";
        return await CreateAsync(null, isOrganization: true, org, org, null, ct);
    }

    private async Task<X509Certificate2> CreateAsync(Guid? userId, bool isOrganization, string commonName, string? organization, string? email, CancellationToken ct)
    {
        using var rsa = RSA.Create(2048);
        var dn = new X500DistinguishedNameBuilder();
        dn.AddCountryOrRegion("VN");
        if (!string.IsNullOrWhiteSpace(organization))
        {
            dn.AddOrganizationName(organization);
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            dn.AddEmailAddress(email);
        }

        dn.AddCommonName(commonName);
        var request = new CertificateRequest(dn.Build(), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.4"), new Oid("1.3.6.1.4.1.311.10.3.12")], critical: false)); // email protection, document signing
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        var now = clock.UtcNow;
        var serial = RandomNumberGenerator.GetBytes(12);
        serial[0] &= 0x7F;
        using var generated = request.Create(request.SubjectName, X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1), now.AddMinutes(-5), now.AddYears(3), serial);
        using var withKey = generated.CopyWithPrivateKey(rsa);
        var pfx = withKey.Export(X509ContentType.Pfx);

        db.SigningCertificates.Add(new SigningCertificate(userId, isOrganization, MockProvider, withKey.Subject, withKey.SerialNumber, withKey.Issuer,
            withKey.NotBefore.ToUniversalTime(), withKey.NotAfter.ToUniversalTime(), protector.Protect(Convert.ToBase64String(pfx))));
        await db.SaveChangesAsync(ct);
        return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
    }

    private X509Certificate2 Load(SigningCertificate c) =>
        X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(protector.Unprotect(c.ProtectedPfx)), null, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
}
