using BlueContract.Domain.Common;

namespace BlueContract.Domain.Documents;

public enum DocumentType
{
    Contract = 0,
    Appendix = 1,
    Proposal = 2,
    ProposalSummary = 3,
    CallOrder = 4,
    WarehouseReceipt = 5,
    Acceptance = 6,
    Valuation = 7,
    PaymentRequest = 8,
    Liquidation = 9,

    /// <summary>Documents generated from a template preview by administrators (not part of any dossier).</summary>
    TemplateSample = 99,
}

public enum DocumentStatus
{
    Draft = 0,
    InWorkflow = 1,
    Completed = 2,
    Cancelled = 3,
}

/// <summary>
/// A business document (contract, receipt, minutes...) with immutable versions. Every file is hashed.
/// Once a version carries a signature it can never change; editing creates a new version and restarts signing.
/// </summary>
public sealed class Document : Entity
{
    private readonly List<DocumentVersion> _versions = [];

    private Document()
    {
        Title = string.Empty;
        OwnerEntityType = string.Empty;
    }

    public Document(DocumentType type, string title, string ownerEntityType, Guid ownerId, Guid? supplierId, Guid? contractId, Guid? templateId)
    {
        Type = type;
        Title = Guard.Required(title, "DOCUMENT_TITLE_REQUIRED");
        OwnerEntityType = Guard.Required(ownerEntityType, "DOCUMENT_OWNER_REQUIRED");
        OwnerId = ownerId;
        SupplierId = supplierId;
        ContractId = contractId;
        TemplateId = templateId;
        Status = DocumentStatus.Draft;
    }

    public DocumentType Type { get; private set; }

    public string Title { get; private set; }

    /// <summary>Owning aggregate (e.g. "Contract", "WarehouseReceipt") and its id.</summary>
    public string OwnerEntityType { get; private set; }

    public Guid OwnerId { get; private set; }

    /// <summary>Supplier party of the document; supplier users only see their own documents.</summary>
    public Guid? SupplierId { get; private set; }

    /// <summary>Contract dossier this document belongs to (hồ sơ hợp đồng).</summary>
    public Guid? ContractId { get; private set; }

    public Guid? BidDecisionId { get; private set; }

    public Guid? TemplateId { get; private set; }

    public DocumentStatus Status { get; private set; }

    public Guid? CurrentVersionId { get; private set; }

    public IReadOnlyCollection<DocumentVersion> Versions => _versions;

    public DocumentVersion? CurrentVersion => _versions.FirstOrDefault(v => v.Id == CurrentVersionId);

    public void Rename(string title) => Title = Guard.Required(title, "DOCUMENT_TITLE_REQUIRED");

    public void LinkBidDecision(Guid? bidDecisionId) => BidDecisionId = bidDecisionId;

    /// <summary>
    /// Adds a new version and makes it current. Returns true when the previous current version was already signed,
    /// meaning the signing workflow must restart (spec §5.7).
    /// </summary>
    public bool AddVersion(DocumentVersion version)
    {
        if (Status == DocumentStatus.Completed || Status == DocumentStatus.Cancelled)
        {
            throw new DomainException("DOCUMENT_CLOSED");
        }

        if (version.DocumentId != Id)
        {
            throw new DomainException("DOCUMENT_VERSION_MISMATCH");
        }

        var previous = CurrentVersion;
        version.VersionNumber = (_versions.Count == 0 ? 0 : _versions.Max(v => v.VersionNumber)) + 1;
        _versions.Add(version);
        CurrentVersionId = version.Id;
        return previous?.IsLocked == true;
    }

    public void MarkInWorkflow()
    {
        if (Status is DocumentStatus.Completed or DocumentStatus.Cancelled)
        {
            throw new DomainException("DOCUMENT_CLOSED");
        }

        Status = DocumentStatus.InWorkflow;
    }

    public void MarkDraft() => Status = Status == DocumentStatus.InWorkflow ? DocumentStatus.Draft : Status;

    public void Complete()
    {
        CurrentVersion?.MarkFinal();
        Status = DocumentStatus.Completed;
    }

    public void Cancel() => Status = DocumentStatus.Cancelled;
}

/// <summary>
/// One immutable revision: optional DOCX (editable source) and/or PDF (the signable rendition), each with SHA-256.
/// </summary>
public sealed class DocumentVersion
{
    private DocumentVersion()
    {
        Reason = string.Empty;
    }

    public DocumentVersion(Guid documentId, string reason, Guid? createdBy, string? createdByName, DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        DocumentId = documentId;
        Reason = Guard.Required(reason, "DOCUMENT_VERSION_REASON_REQUIRED");
        CreatedBy = createdBy;
        CreatedByName = createdByName;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid DocumentId { get; private set; }

    public int VersionNumber { get; internal set; }

    public string? DocxFileKey { get; private set; }

    public string? DocxSha256 { get; private set; }

    public long? DocxSize { get; private set; }

    public string? PdfFileKey { get; private set; }

    public string? PdfSha256 { get; private set; }

    public long? PdfSize { get; private set; }

    public string Reason { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public string? CreatedByName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Number of signatures embedded in <see cref="PdfFileKey"/>; &gt; 0 makes the version immutable.</summary>
    public int SignatureCount { get; private set; }

    public bool IsLocked => SignatureCount > 0;

    public bool IsFinal { get; private set; }

    public void AttachDocx(string fileKey, string sha256, long size)
    {
        EnsureEditable();
        DocxFileKey = fileKey;
        DocxSha256 = sha256;
        DocxSize = size;
    }

    /// <summary>Sets the rendered PDF (only before any signature exists).</summary>
    public void AttachPdf(string fileKey, string sha256, long size)
    {
        EnsureEditable();
        PdfFileKey = fileKey;
        PdfSha256 = sha256;
        PdfSize = size;
    }

    /// <summary>Records a signed PDF produced by incremental update over the current one.</summary>
    public void ApplySignedPdf(string fileKey, string sha256, long size)
    {
        if (PdfFileKey is null)
        {
            throw new DomainException("DOCUMENT_PDF_REQUIRED");
        }

        PdfFileKey = fileKey;
        PdfSha256 = sha256;
        PdfSize = size;
        SignatureCount++;
    }

    public void MarkFinal() => IsFinal = true;

    private void EnsureEditable()
    {
        if (IsLocked)
        {
            throw new DomainException("DOCUMENT_VERSION_LOCKED");
        }
    }
}
