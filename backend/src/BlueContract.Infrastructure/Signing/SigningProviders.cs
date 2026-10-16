using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using BlueContract.Application.Common;
using BlueContract.Application.Signing;
using BlueContract.Domain.Common;
using BlueContract.Domain.Workflows;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

using BlueContract.Infrastructure.Configuration;

namespace BlueContract.Infrastructure.Signing;

public sealed class SigningOptions
{
    public const string SectionName = "Signing";

    /// <summary>Provider used when the client does not choose one.</summary>
    public string DefaultProvider { get; set; } = "MOCK";

    /// <summary>Seconds the simulated phone confirmation takes (MOCK_REMOTE).</summary>
    public int MockRemoteDelaySeconds { get; set; } = 5;

    /// <summary>Allow the mock providers (dev/demo). Disable in production once a real CA is configured.</summary>
    public bool EnableMockProviders { get; set; } = true;

    /// <summary>
    /// Ký nháy (spec §8.2) needs no CA by default: the initial is stamped with the user's internal certificate.
    /// Set to true to require the user's CA provider for initials too.
    /// </summary>
    public bool InitialRequiresCa { get; set; }

    /// <summary>Optional RFC 3161 timestamp authority URL (TSA).</summary>
    public string? TsaUrl { get; set; }

    public RemoteCaOptions VnptSmartCa { get; set; } = new();

    public RemoteCaOptions ViettelMySign { get; set; } = new();

    public RemoteCaOptions FptCa { get; set; } = new();
}

/// <summary>Credentials for a remote-signing CA (provided by the CA when the hospital subscribes).</summary>
public sealed class RemoteCaOptions
{
    public Uri? BaseUrl { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public bool IsConfigured => BaseUrl is not null && !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);
}

internal static class ProviderState
{
    public static readonly DistributedCacheEntryOptions Lifetime = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) };

    public static SignerCertificate Describe(X509Certificate2 cert) =>
        new(cert.RawData, cert.Subject, cert.Issuer, cert.SerialNumber, cert.NotBefore.ToUniversalTime(), cert.NotAfter.ToUniversalTime());

    /// <summary>Signs on the server with the internal certificate of the user (or of the organization for the e-seal).</summary>
    public static async Task<SigningSession> SignLocallyAsync(CertificateStore certificates, ICmsBuilder cms, IClock clock, SignRequest req, CancellationToken ct)
    {
        using var cert = req.Kind == SignatureKind.Organization
            ? await certificates.GetOrganizationCertificateAsync(ct)
            : await certificates.GetUserCertificateAsync(req.UserId, ct);
        var at = clock.UtcNow;
        var toSign = cms.GetDataToSign(req.DocumentDigest, cert.RawData, at);
        var envelope = cms.Assemble(req.DocumentDigest, cert.RawData, at, SignWithKey(cert, toSign));
        return new SigningSession(req.SessionId.ToString("N"), SigningMode.ServerSide, SigningStatus.Completed, envelope, Describe(cert), null, null);
    }

    public static byte[] SignWithKey(X509Certificate2 cert, byte[] data)
    {
        using var rsa = cert.GetRSAPrivateKey() ?? throw new InvalidOperationException("Certificate has no RSA private key.");
        return rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}

/// <summary>Server-side signing with per-user self-signed certificates (spec §8.2 — dev/demo, structurally valid PAdES).</summary>
internal sealed class MockSigningProvider(CertificateStore certificates, ICmsBuilder cms, IClock clock, RuntimeSettings settings) : ISigningProvider
{
    public string ProviderCode => "MOCK";

    public SigningMode Mode => SigningMode.ServerSide;

    public string DisplayName => "Chứng thư thử nghiệm (ký ngay trên máy chủ)";

    public bool IsAvailable => settings.Signing().EnableMockProviders;

    public Task<SigningSession> BeginAsync(SignRequest req, CancellationToken ct) => ProviderState.SignLocallyAsync(certificates, cms, clock, req, ct);

    public Task<SignResult> CompleteAsync(string sessionId, SignCompletion completion, CancellationToken ct) =>
        throw new DomainException("SIGNING_NOT_SUPPORTED");

    public Task<SigningSession> GetStatusAsync(string sessionId, CancellationToken ct) =>
        throw new DomainException("SIGNING_NOT_SUPPORTED");
}

/// <summary>
/// Ký nháy without a CA (spec §8.2): stamps the initial with the user's internal certificate so the PDF still records
/// who initialled and that nothing changed afterwards. Not offered to users; selected automatically for Initial steps
/// unless <see cref="SigningOptions.InitialRequiresCa"/> is set.
/// </summary>
internal sealed class InitialStampProvider(CertificateStore certificates, ICmsBuilder cms, IClock clock) : ISigningProvider
{
    public string ProviderCode => SigningDefaults.InitialStampProvider;

    public SigningMode Mode => SigningMode.ServerSide;

    public string DisplayName => "Ký nháy nội bộ (không cần chứng thư CA)";

    public bool IsAvailable => true;

    public Task<SigningSession> BeginAsync(SignRequest req, CancellationToken ct) =>
        req.Kind == SignatureKind.Initial
            ? ProviderState.SignLocallyAsync(certificates, cms, clock, req, ct)
            : throw new DomainException("SIGNING_PROVIDER_NOT_CONFIGURED");

    public Task<SignResult> CompleteAsync(string sessionId, SignCompletion completion, CancellationToken ct) =>
        throw new DomainException("SIGNING_NOT_SUPPORTED");

    public Task<SigningSession> GetStatusAsync(string sessionId, CancellationToken ct) =>
        throw new DomainException("SIGNING_NOT_SUPPORTED");
}

/// <summary>
/// "Đóng dấu điện tử" with the hospital certificate installed by the administrator (spec §8): signed on the server.
/// Selected automatically for OrgSeal steps once a certificate is installed; never offered to users.
/// </summary>
internal sealed class OrganizationSealProvider(CertificateStore certificates, ICmsBuilder cms, IClock clock) : ISigningProvider
{
    public string ProviderCode => SigningDefaults.OrganizationSealProvider;

    public SigningMode Mode => SigningMode.ServerSide;

    public string DisplayName => "Chứng thư số tổ chức của Bệnh viện";

    public bool IsAvailable => true;

    public async Task<SigningSession> BeginAsync(SignRequest req, CancellationToken ct)
    {
        if (req.Kind != SignatureKind.Organization)
        {
            throw new DomainException("SIGNING_PROVIDER_NOT_CONFIGURED");
        }

        using var cert = await certificates.GetInstalledOrganizationCertificateAsync(ct);
        var at = clock.UtcNow;
        var toSign = cms.GetDataToSign(req.DocumentDigest, cert.RawData, at);
        var envelope = cms.Assemble(req.DocumentDigest, cert.RawData, at, ProviderState.SignWithKey(cert, toSign));
        return new SigningSession(req.SessionId.ToString("N"), SigningMode.ServerSide, SigningStatus.Completed, envelope, ProviderState.Describe(cert), null, null);
    }

    public Task<SignResult> CompleteAsync(string sessionId, SignCompletion completion, CancellationToken ct) =>
        throw new DomainException("SIGNING_NOT_SUPPORTED");

    public Task<SigningSession> GetStatusAsync(string sessionId, CancellationToken ct) =>
        throw new DomainException("SIGNING_NOT_SUPPORTED");
}

/// <summary>
/// Simulates remote signing (VNPT SmartCA / Viettel MySign style): the request waits for the user to confirm on a phone
/// and the client polls GetStatus. Lets the whole async UI flow run without a real CA account.
/// </summary>
internal sealed class MockRemoteSigningProvider(
    CertificateStore certificates,
    ICmsBuilder cms,
    IDistributedCache cache,
    IClock clock,
    RuntimeSettings settings) : ISigningProvider
{
    public string ProviderCode => "MOCK_REMOTE";

    public SigningMode Mode => SigningMode.RemoteAsync;

    public string DisplayName => "Ký số từ xa (giả lập xác nhận trên điện thoại)";

    public bool IsAvailable => settings.Signing().EnableMockProviders;

    public async Task<SigningSession> BeginAsync(SignRequest req, CancellationToken ct)
    {
        var id = req.SessionId.ToString("N");
        var state = new RemoteState(req.UserId, req.Kind, req.DocumentDigest, clock.UtcNow.AddSeconds(settings.Signing().MockRemoteDelaySeconds));
        await cache.SetStringAsync(Key(id), JsonSerializer.Serialize(state), ProviderState.Lifetime, ct);
        return new SigningSession(id, Mode, SigningStatus.Pending, null, null, null, "Vui lòng mở ứng dụng ký số trên điện thoại và xác nhận yêu cầu ký.");
    }

    public Task<SignResult> CompleteAsync(string sessionId, SignCompletion completion, CancellationToken ct) =>
        throw new DomainException("SIGNING_NOT_SUPPORTED");

    public async Task<SigningSession> GetStatusAsync(string sessionId, CancellationToken ct)
    {
        var json = await cache.GetStringAsync(Key(sessionId), ct);
        if (json is null)
        {
            return new SigningSession(sessionId, Mode, SigningStatus.Expired, null, null, null, "Yêu cầu ký đã hết hạn.");
        }

        var state = JsonSerializer.Deserialize<RemoteState>(json)!;
        if (clock.UtcNow < state.ReadyAt)
        {
            return new SigningSession(sessionId, Mode, SigningStatus.Pending, null, null, null, "Đang chờ xác nhận trên điện thoại...");
        }

        using var cert = state.Kind == SignatureKind.Organization
            ? await certificates.GetOrganizationCertificateAsync(ct)
            : await certificates.GetUserCertificateAsync(state.UserId, ct);
        var at = clock.UtcNow;
        var toSign = cms.GetDataToSign(state.Digest, cert.RawData, at);
        var envelope = cms.Assemble(state.Digest, cert.RawData, at, ProviderState.SignWithKey(cert, toSign));
        await cache.RemoveAsync(Key(sessionId), ct);
        return new SigningSession(sessionId, Mode, SigningStatus.Completed, envelope, ProviderState.Describe(cert), null, null);
    }

    private static string Key(string id) => "sign:remote:" + id;

    private sealed record RemoteState(Guid UserId, SignatureKind Kind, byte[] Digest, DateTimeOffset ReadyAt);
}

/// <summary>
/// USB token through the CA's local browser plugin (spec §8.2): the server returns the bytes to sign, the plugin signs them
/// with the token's private key, and the server assembles the CMS around the returned value.
/// </summary>
internal sealed class UsbTokenSigningProvider(ICmsBuilder cms, IDistributedCache cache, IClock clock) : ISigningProvider
{
    public string ProviderCode => "USB_TOKEN";

    public SigningMode Mode => SigningMode.ClientSide;

    public string DisplayName => "USB token (plugin ký số của nhà cung cấp chứng thư)";

    public bool IsAvailable => true;

    public async Task<SigningSession> BeginAsync(SignRequest req, CancellationToken ct)
    {
        if (req.ClientCertificate is null || req.ClientCertificate.Length == 0)
        {
            throw new DomainException("SIGNING_CERTIFICATE_REQUIRED");
        }

        using var cert = X509CertificateLoader.LoadCertificate(req.ClientCertificate);
        var at = clock.UtcNow;
        if (at < cert.NotBefore.ToUniversalTime() || at > cert.NotAfter.ToUniversalTime())
        {
            throw new DomainException("SIGNING_CERTIFICATE_EXPIRED");
        }

        var toSign = cms.GetDataToSign(req.DocumentDigest, cert.RawData, at);
        var id = req.SessionId.ToString("N");
        var state = new TokenState(req.DocumentDigest, cert.RawData, at, toSign);
        await cache.SetStringAsync(Key(id), JsonSerializer.Serialize(state), ProviderState.Lifetime, ct);
        return new SigningSession(id, Mode, SigningStatus.Pending, null, ProviderState.Describe(cert), toSign, "Cắm USB token và xác nhận ký trên máy tính.");
    }

    public async Task<SignResult> CompleteAsync(string sessionId, SignCompletion completion, CancellationToken ct)
    {
        var json = await cache.GetStringAsync(Key(sessionId), ct) ?? throw new DomainException("SIGNING_SESSION_EXPIRED");
        var state = JsonSerializer.Deserialize<TokenState>(json)!;
        using var cert = X509CertificateLoader.LoadCertificate(state.Certificate);
        using var rsa = cert.GetRSAPublicKey() ?? throw new DomainException("SIGNING_CERTIFICATE_UNSUPPORTED");
        if (!rsa.VerifyData(state.DataToSign, completion.SignatureValue, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        {
            throw new DomainException("SIGNING_SIGNATURE_INVALID");
        }

        var envelope = cms.Assemble(state.Digest, state.Certificate, state.SigningTime, completion.SignatureValue);
        await cache.RemoveAsync(Key(sessionId), ct);
        return new SignResult(envelope, ProviderState.Describe(cert));
    }

    public Task<SigningSession> GetStatusAsync(string sessionId, CancellationToken ct) =>
        Task.FromResult(new SigningSession(sessionId, Mode, SigningStatus.Pending, null, null, null, "Đang chờ chữ ký từ USB token."));

    private static string Key(string id) => "sign:token:" + id;

    private sealed record TokenState(byte[] Digest, byte[] Certificate, DateTimeOffset SigningTime, byte[] DataToSign);
}

/// <summary>
/// Remote signing CAs (VNPT SmartCA, Viettel MySign, FPT CA). Each CA publishes its own REST API and issues credentials
/// on subscription; until <see cref="SigningOptions"/> holds them the provider reports itself unavailable with a clear
/// message (see docs/DEPLOY.md). The polling flow is exercised end-to-end by <see cref="MockRemoteSigningProvider"/>.
/// </summary>
internal sealed class RemoteCaSigningProvider(string code, string displayName, Func<SigningOptions, RemoteCaOptions> select, RuntimeSettings settings) : ISigningProvider
{
    public string ProviderCode => code;

    public SigningMode Mode => SigningMode.RemoteAsync;

    public string DisplayName => displayName;

    public bool IsAvailable => select(settings.Signing()).IsConfigured;

    public Task<SigningSession> BeginAsync(SignRequest req, CancellationToken ct) =>
        throw new DomainException(IsAvailable ? "SIGNING_PROVIDER_NOT_IMPLEMENTED" : "SIGNING_PROVIDER_NOT_CONFIGURED");

    public Task<SignResult> CompleteAsync(string sessionId, SignCompletion completion, CancellationToken ct) =>
        throw new DomainException("SIGNING_NOT_SUPPORTED");

    public Task<SigningSession> GetStatusAsync(string sessionId, CancellationToken ct) =>
        throw new DomainException(IsAvailable ? "SIGNING_PROVIDER_NOT_IMPLEMENTED" : "SIGNING_PROVIDER_NOT_CONFIGURED");
}
