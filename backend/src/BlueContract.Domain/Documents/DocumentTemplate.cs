using BlueContract.Domain.Common;

namespace BlueContract.Domain.Documents;

/// <summary>Which data model a template is rendered with (decides the valid placeholders).</summary>
public enum TemplateKind
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
}

/// <summary>DOCX template (mẫu biểu) with versioning; the active version is used for new documents.</summary>
public sealed class DocumentTemplate : Entity
{
    private readonly List<DocumentTemplateVersion> _versions = [];

    private DocumentTemplate()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public DocumentTemplate(string code, string name, TemplateKind kind, string? description, bool isBuiltIn)
    {
        Code = Guard.Required(code, "TEMPLATE_CODE_REQUIRED").ToUpperInvariant();
        Name = Guard.Required(name, "TEMPLATE_NAME_REQUIRED");
        Kind = kind;
        Description = Guard.Optional(description);
        IsBuiltIn = isBuiltIn;
        IsActive = true;
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public TemplateKind Kind { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Shipped with the system (generated from templates/); admins may upload newer versions.</summary>
    public bool IsBuiltIn { get; private set; }

    public bool IsActive { get; private set; }

    public int ActiveVersionNumber { get; private set; }

    public IReadOnlyCollection<DocumentTemplateVersion> Versions => _versions;

    public DocumentTemplateVersion? ActiveVersion => _versions.FirstOrDefault(v => v.VersionNumber == ActiveVersionNumber);

    public void Update(string name, string? description, bool isActive)
    {
        Name = Guard.Required(name, "TEMPLATE_NAME_REQUIRED");
        Description = Guard.Optional(description);
        IsActive = isActive;
    }

    /// <summary>Adds an uploaded version and activates it.</summary>
    public DocumentTemplateVersion AddVersion(string fileKey, string sha256, long size, IReadOnlyCollection<string> placeholders, string? note, Guid? uploadedBy, DateTimeOffset at)
    {
        var number = (_versions.Count == 0 ? 0 : _versions.Max(v => v.VersionNumber)) + 1;
        var version = new DocumentTemplateVersion(Id, number, fileKey, sha256, size, string.Join('\n', placeholders), note, uploadedBy, at);
        _versions.Add(version);
        ActiveVersionNumber = number;
        return version;
    }

    public void Activate(int versionNumber)
    {
        if (_versions.All(v => v.VersionNumber != versionNumber))
        {
            throw new DomainException("TEMPLATE_VERSION_NOT_FOUND");
        }

        ActiveVersionNumber = versionNumber;
    }
}

public sealed class DocumentTemplateVersion
{
    private DocumentTemplateVersion()
    {
        FileKey = string.Empty;
        Sha256 = string.Empty;
        Placeholders = string.Empty;
    }

    internal DocumentTemplateVersion(Guid templateId, int number, string fileKey, string sha256, long size, string placeholders, string? note, Guid? uploadedBy, DateTimeOffset at)
    {
        Id = Guid.CreateVersion7();
        TemplateId = templateId;
        VersionNumber = number;
        FileKey = fileKey;
        Sha256 = sha256;
        Size = size;
        Placeholders = placeholders;
        Note = note;
        UploadedBy = uploadedBy;
        UploadedAt = at;
    }

    public Guid Id { get; private set; }

    public Guid TemplateId { get; private set; }

    public int VersionNumber { get; private set; }

    public string FileKey { get; private set; }

    public string Sha256 { get; private set; }

    public long Size { get; private set; }

    /// <summary>Newline-separated placeholder tags found in the file.</summary>
    public string Placeholders { get; private set; }

    public string? Note { get; private set; }

    public Guid? UploadedBy { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }
}

/// <summary>A live ONLYOFFICE editing session; its id is the editor's document key.</summary>
public sealed class DocumentEditSession
{
    private DocumentEditSession()
    {
        Mode = string.Empty;
    }

    public DocumentEditSession(Guid documentId, Guid baseVersionId, string mode, Guid userId, DateTimeOffset at)
    {
        Id = Guid.CreateVersion7();
        DocumentId = documentId;
        BaseVersionId = baseVersionId;
        Mode = mode;
        UserId = userId;
        CreatedAt = at;
    }

    public Guid Id { get; private set; }

    public Guid DocumentId { get; private set; }

    public Guid BaseVersionId { get; private set; }

    /// <summary>"edit" (full editing) or "review" (track changes forced).</summary>
    public string Mode { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public Guid? ResultVersionId { get; private set; }

    public bool IsOpen => ClosedAt is null;

    public void Close(DateTimeOffset at, Guid? resultVersionId)
    {
        ClosedAt ??= at;
        ResultVersionId ??= resultVersionId;
    }
}
