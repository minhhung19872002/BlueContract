using BlueContract.Domain.Common;

namespace BlueContract.Domain.Workflows;

public enum SigningRequestStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
    Expired = 3,
    Cancelled = 4,
}

/// <summary>
/// One signing attempt on a workflow step: the PDF was prepared (empty field + digest) and handed to a provider.
/// Finalised once the provider returns the CMS. Guarantees one active attempt per step.
/// </summary>
public sealed class SigningRequest
{
    private SigningRequest()
    {
        Provider = string.Empty;
        FieldName = string.Empty;
        PreparedPdfKey = string.Empty;
        BasePdfSha256 = string.Empty;
        DocumentDigest = string.Empty;
    }

    public SigningRequest(
        Guid documentId,
        Guid documentVersionId,
        Guid stepInstanceId,
        Guid userId,
        Guid? onBehalfOfUserId,
        SignatureKind kind,
        string provider,
        string fieldName,
        string preparedPdfKey,
        string basePdfSha256,
        string documentDigest,
        DateTimeOffset createdAt,
        TimeSpan lifetime)
    {
        Id = Guid.CreateVersion7();
        DocumentId = documentId;
        DocumentVersionId = documentVersionId;
        StepInstanceId = stepInstanceId;
        UserId = userId;
        OnBehalfOfUserId = onBehalfOfUserId;
        Kind = kind;
        Provider = provider;
        FieldName = fieldName;
        PreparedPdfKey = preparedPdfKey;
        BasePdfSha256 = basePdfSha256;
        DocumentDigest = documentDigest;
        CreatedAt = createdAt;
        ExpiresAt = createdAt.Add(lifetime);
        Status = SigningRequestStatus.Pending;
    }

    public Guid Id { get; private set; }

    public Guid DocumentId { get; private set; }

    public Guid DocumentVersionId { get; private set; }

    public Guid StepInstanceId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid? OnBehalfOfUserId { get; private set; }

    public SignatureKind Kind { get; private set; }

    public string Provider { get; private set; }

    public string FieldName { get; private set; }

    /// <summary>Storage key of the PDF with the empty signature field.</summary>
    public string PreparedPdfKey { get; private set; }

    /// <summary>SHA-256 of the PDF the request was prepared from; finalisation fails if the document changed meanwhile.</summary>
    public string BasePdfSha256 { get; private set; }

    /// <summary>Hex SHA-256 of the signed byte ranges.</summary>
    public string DocumentDigest { get; private set; }

    /// <summary>Base64 bytes the client must sign (USB token flow).</summary>
    public string? DataToSign { get; private set; }

    public string? ProviderMessage { get; private set; }

    public SigningRequestStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public Guid? SignatureId { get; private set; }

    public string? Error { get; private set; }

    public uint RowVersion { get; private set; }

    public bool IsOpen(DateTimeOffset now) => Status == SigningRequestStatus.Pending && now < ExpiresAt;

    public void SetProviderInfo(string? dataToSign, string? message)
    {
        DataToSign = dataToSign;
        ProviderMessage = message;
    }

    public void Complete(Guid signatureId, DateTimeOffset at)
    {
        if (Status != SigningRequestStatus.Pending)
        {
            throw new DomainException("SIGNING_REQUEST_CLOSED");
        }

        Status = SigningRequestStatus.Completed;
        SignatureId = signatureId;
        CompletedAt = at;
    }

    public void Fail(string error, DateTimeOffset at, bool expired = false)
    {
        if (Status != SigningRequestStatus.Pending)
        {
            return;
        }

        Status = expired ? SigningRequestStatus.Expired : SigningRequestStatus.Failed;
        Error = error;
        CompletedAt = at;
    }

    public void Cancel(DateTimeOffset at) => Fail("Đã hủy", at);
}
