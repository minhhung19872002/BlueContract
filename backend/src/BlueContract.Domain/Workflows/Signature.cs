namespace BlueContract.Domain.Workflows;

public enum SignatureKind
{
    /// <summary>Chữ ký số cá nhân.</summary>
    Personal = 0,

    /// <summary>Dấu điện tử của tổ chức (Văn thư).</summary>
    Organization = 1,

    /// <summary>Ký nháy.</summary>
    Initial = 2,
}

public enum SignatureVerification
{
    Unverified = 0,
    Valid = 1,
    Invalid = 2,
}

/// <summary>One embedded PDF signature (spec §5.7): who, with which certificate, when, and the hashes around it.</summary>
public sealed class Signature
{
    private Signature()
    {
        SignerName = string.Empty;
        Provider = string.Empty;
        FieldName = string.Empty;
        HashBefore = string.Empty;
        HashAfter = string.Empty;
    }

    public Signature(
        Guid documentId,
        Guid documentVersionId,
        Guid? stepInstanceId,
        Guid signerUserId,
        string signerName,
        string? signerTitle,
        Guid? onBehalfOfUserId,
        string? caption,
        SignatureKind kind,
        string provider,
        string fieldName,
        string? certificateSerial,
        string? certificateSubject,
        string? certificateIssuer,
        DateTimeOffset signedAt,
        string hashBefore,
        string hashAfter)
    {
        Id = Guid.CreateVersion7();
        DocumentId = documentId;
        DocumentVersionId = documentVersionId;
        StepInstanceId = stepInstanceId;
        SignerUserId = signerUserId;
        SignerName = signerName;
        SignerTitle = signerTitle;
        OnBehalfOfUserId = onBehalfOfUserId;
        Caption = caption;
        Kind = kind;
        Provider = provider;
        FieldName = fieldName;
        CertificateSerial = certificateSerial;
        CertificateSubject = certificateSubject;
        CertificateIssuer = certificateIssuer;
        SignedAt = signedAt;
        HashBefore = hashBefore;
        HashAfter = hashAfter;
        Verification = SignatureVerification.Valid;
    }

    public Guid Id { get; private set; }

    public Guid DocumentId { get; private set; }

    public Guid DocumentVersionId { get; private set; }

    public Guid? StepInstanceId { get; private set; }

    public Guid SignerUserId { get; private set; }

    public string SignerName { get; private set; }

    public string? SignerTitle { get; private set; }

    public Guid? OnBehalfOfUserId { get; private set; }

    /// <summary>Signature caption, e.g. "KT. GIÁM ĐỐC / PHÓ GIÁM ĐỐC".</summary>
    public string? Caption { get; private set; }

    public SignatureKind Kind { get; private set; }

    /// <summary>MOCK, USB_TOKEN, VNPT_SMARTCA, VIETTEL_MYSIGN...</summary>
    public string Provider { get; private set; }

    /// <summary>PDF signature field name.</summary>
    public string FieldName { get; private set; }

    public string? CertificateSerial { get; private set; }

    public string? CertificateSubject { get; private set; }

    public string? CertificateIssuer { get; private set; }

    public DateTimeOffset SignedAt { get; private set; }

    /// <summary>SHA-256 of the PDF before this signature was added.</summary>
    public string HashBefore { get; private set; }

    /// <summary>SHA-256 of the PDF after signing (the new current file).</summary>
    public string HashAfter { get; private set; }

    public SignatureVerification Verification { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    public void SetVerification(SignatureVerification result, DateTimeOffset at)
    {
        Verification = result;
        VerifiedAt = at;
    }
}

/// <summary>In-app notification (bell icon); email and realtime push are sent alongside.</summary>
public sealed class Notification
{
    private Notification()
    {
        Title = string.Empty;
        Type = string.Empty;
    }

    public Notification(Guid userId, string type, string title, string? body, string? link, DateTimeOffset at)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        Type = type;
        Title = title;
        Body = body;
        Link = link;
        CreatedAt = at;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Type { get; private set; }

    public string Title { get; private set; }

    public string? Body { get; private set; }

    public string? Link { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public void MarkRead(DateTimeOffset at) => ReadAt ??= at;
}

/// <summary>
/// Signing certificate held by the system (MockSigningProvider for users; the hospital's organisation certificate
/// for the e-seal). The PKCS#12 blob is encrypted at rest.
/// </summary>
public sealed class SigningCertificate
{
    private SigningCertificate()
    {
        Subject = string.Empty;
        SerialNumber = string.Empty;
        Issuer = string.Empty;
        ProtectedPfx = string.Empty;
        Provider = string.Empty;
    }

    public SigningCertificate(Guid? userId, bool isOrganization, string provider, string subject, string serialNumber, string issuer, DateTimeOffset notBefore, DateTimeOffset notAfter, string protectedPfx)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        IsOrganization = isOrganization;
        Provider = provider;
        Subject = subject;
        SerialNumber = serialNumber;
        Issuer = issuer;
        NotBefore = notBefore;
        NotAfter = notAfter;
        ProtectedPfx = protectedPfx;
    }

    public Guid Id { get; private set; }

    public Guid? UserId { get; private set; }

    public bool IsOrganization { get; private set; }

    public string Provider { get; private set; }

    public string Subject { get; private set; }

    public string SerialNumber { get; private set; }

    public string Issuer { get; private set; }

    public DateTimeOffset NotBefore { get; private set; }

    public DateTimeOffset NotAfter { get; private set; }

    public string ProtectedPfx { get; private set; }
}
