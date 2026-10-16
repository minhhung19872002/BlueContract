using BlueContract.Domain.Workflows;

namespace BlueContract.Application.Signing;

/// <summary>How a provider produces the signature (spec §8.1).</summary>
public enum SigningMode
{
    /// <summary>The server holds the key (mock certificates, HSM): signing completes immediately.</summary>
    ServerSide = 0,

    /// <summary>The browser signs the hash with a USB token through the CA's local plugin.</summary>
    ClientSide = 1,

    /// <summary>Remote signing (VNPT SmartCA, Viettel MySign...): the user confirms on a phone; the server polls.</summary>
    RemoteAsync = 2,
}

public enum SigningStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
    Expired = 3,
}

/// <summary>The signer's certificate as embedded in the CMS.</summary>
public sealed record SignerCertificate(byte[] RawData, string Subject, string Issuer, string SerialNumber, DateTimeOffset NotBefore, DateTimeOffset NotAfter);

/// <summary>
/// Input for a provider: the SHA-256 of the PDF byte ranges (prepared by the platform with an empty signature field)
/// plus who signs. Providers return a detached CMS (CAdES) to embed.
/// </summary>
public sealed record SignRequest(
    Guid SessionId,
    Guid UserId,
    string UserName,
    SignatureKind Kind,
    byte[] DocumentDigest,
    byte[]? ClientCertificate = null);

/// <summary>State of a provider session. <see cref="Cms"/> is set once the signature exists.</summary>
public sealed record SigningSession(
    string ProviderSessionId,
    SigningMode Mode,
    SigningStatus Status,
    byte[]? Cms,
    SignerCertificate? Certificate,
    byte[]? DataToSign,
    string? Message);

/// <summary>Client-side completion: the raw signature over <see cref="SigningSession.DataToSign"/>.</summary>
public sealed record SignCompletion(byte[] SignatureValue);

public sealed record SignResult(byte[] Cms, SignerCertificate Certificate);

/// <summary>A signing method (spec §8.1). PDF preparation/embedding is done by <see cref="IPdfSignatureService"/>.</summary>
public interface ISigningProvider
{
    /// <summary>"MOCK", "MOCK_REMOTE", "USB_TOKEN", "VNPT_SMARTCA", "VIETTEL_MYSIGN"...</summary>
    string ProviderCode { get; }

    SigningMode Mode { get; }

    /// <summary>Display name for the UI.</summary>
    string DisplayName { get; }

    /// <summary>True when the provider is configured and usable in this installation.</summary>
    bool IsAvailable { get; }

    Task<SigningSession> BeginAsync(SignRequest req, CancellationToken ct);

    Task<SignResult> CompleteAsync(string sessionId, SignCompletion completion, CancellationToken ct);

    Task<SigningSession> GetStatusAsync(string sessionId, CancellationToken ct);
}

/// <summary>Where and how the visible signature is drawn.</summary>
public sealed record SignatureAppearance(
    SignatureKind Kind,
    string SignerName,
    string? SignerTitle,
    string? Caption,
    string? Anchor,
    DateTimeOffset SignedAt,
    int FallbackSlot,
    byte[]? Image = null);

public sealed record PreparedPdf(byte[] Content, string FieldName, byte[] DocumentDigest);

public sealed record PdfSignatureInfo(
    string FieldName,
    int Revision,
    int TotalRevisions,
    bool CoversWholeDocument,
    bool IntegrityValid,
    string SignerSubject,
    string? SignerName,
    string IssuerSubject,
    string SerialNumber,
    bool IsSelfSigned,
    DateTimeOffset? SigningTime,
    bool HasTimestamp,
    string? Reason,
    string? Location);

/// <summary>PAdES mechanics: incremental-update preparation with an empty field, CMS embedding, verification.</summary>
public interface IPdfSignatureService
{
    /// <summary>Adds an empty signature field (append mode, preserving earlier signatures) and returns the byte-range digest.</summary>
    PreparedPdf Prepare(byte[] pdf, SignatureAppearance appearance);

    /// <summary>Embeds a detached CMS into a prepared PDF.</summary>
    byte[] Embed(byte[] preparedPdf, string fieldName, byte[] cms);

    /// <summary>Lists and verifies every signature in a PDF.</summary>
    IReadOnlyList<PdfSignatureInfo> Verify(byte[] pdf);
}

/// <summary>Builds CAdES-detached CMS from a document digest and an external signer (key held by a provider or token).</summary>
public interface ICmsBuilder
{
    /// <summary>The bytes that must be signed (DER of signed attributes) for the given certificate and digest.</summary>
    byte[] GetDataToSign(byte[] documentDigest, byte[] certificate, DateTimeOffset signingTime);

    /// <summary>Assembles the CMS from the external signature value (RSA PKCS#1 v1.5 / SHA-256).</summary>
    byte[] Assemble(byte[] documentDigest, byte[] certificate, DateTimeOffset signingTime, byte[] signatureValue);
}
