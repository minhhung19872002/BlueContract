using System.Text.Json.Nodes;
using BlueContract.Application.Common;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Common;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Workflows;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueContract.Application.Documents;

public sealed record DocumentVersionDto(
    Guid Id,
    int VersionNumber,
    string Reason,
    string? CreatedByName,
    DateTimeOffset CreatedAt,
    bool HasDocx,
    string? DocxSha256,
    long? DocxSize,
    bool HasPdf,
    string? PdfSha256,
    long? PdfSize,
    int SignatureCount,
    bool IsLocked,
    bool IsFinal,
    bool IsCurrent);

public sealed record DocumentDto(
    Guid Id,
    DocumentType Type,
    string Title,
    DocumentStatus Status,
    string OwnerEntityType,
    Guid OwnerId,
    Guid? ContractId,
    Guid? TemplateId,
    string? TemplateName,
    Guid? CurrentVersionId,
    IReadOnlyList<DocumentVersionDto> Versions,
    bool CanEdit);

public sealed record CreateSampleDocumentRequest(Guid TemplateId, string? Title);

public sealed record StartEditRequest(EditorMode Mode);

public sealed record EditorSessionDto(Guid SessionId, string EditorScriptUrl, JsonObject Config);

public sealed record FileLinkDto(Uri Url, string FileName, DateTimeOffset ExpiresAt);

internal sealed class CreateSampleDocumentRequestValidator : AbstractValidator<CreateSampleDocumentRequest>
{
    public CreateSampleDocumentRequestValidator() => RuleFor(x => x.TemplateId).NotEmpty().WithName("Mẫu biểu");
}

/// <summary>Who may view or edit a document of each type (refined by workflow state in later phases).</summary>
public static class DocumentPermissions
{
    public static string[] View(DocumentType type) => type switch
    {
        DocumentType.Contract or DocumentType.Appendix => [Permissions.ContractView],
        DocumentType.Proposal or DocumentType.ProposalSummary => [Permissions.ProposalView],
        DocumentType.CallOrder => [Permissions.CallOrderView],
        DocumentType.WarehouseReceipt => [Permissions.ReceiptView],
        DocumentType.Acceptance or DocumentType.Valuation or DocumentType.PaymentRequest or DocumentType.Liquidation => [Permissions.PaymentView],
        _ => [Permissions.TemplateManage],
    };

    /// <summary>
    /// Permission (any of) a signer needs for a workflow action on top of <c>signing.sign</c>; empty when the step assignment
    /// alone decides. Contract actions follow the permission matrix (spec §4.2); the hospital e-seal is <c>contract.seal</c>
    /// whatever the document, since it is the only seal permission.
    /// </summary>
    public static string[] Act(DocumentType type, WorkflowActionType action) => (type, action) switch
    {
        (_, WorkflowActionType.OrgSeal) => [Permissions.ContractSeal],
        (DocumentType.Contract or DocumentType.Appendix, WorkflowActionType.Sign) => [Permissions.ContractSign],
        (DocumentType.Contract or DocumentType.Appendix, WorkflowActionType.Initial) => [Permissions.ContractInitial],
        (DocumentType.Contract or DocumentType.Appendix, WorkflowActionType.Review) => [Permissions.ContractReview, Permissions.ContractAccountantReview],
        (DocumentType.Contract or DocumentType.Appendix, WorkflowActionType.SupplierReview) => [Permissions.ContractSupplierReview],
        _ => [],
    };

    public static string[] Edit(DocumentType type, EditorMode mode) => (type, mode) switch
    {
        (_, EditorMode.View) => View(type),
        (DocumentType.Contract or DocumentType.Appendix, EditorMode.Review) => [Permissions.ContractSupplierReview, Permissions.ContractReview],
        (DocumentType.Contract or DocumentType.Appendix, EditorMode.Edit) => [Permissions.ContractCreate, Permissions.ContractAccountantReview],
        (DocumentType.Acceptance or DocumentType.Valuation or DocumentType.PaymentRequest or DocumentType.Liquidation, _) => [Permissions.PaymentManage],
        (DocumentType.WarehouseReceipt, _) => [Permissions.ReceiptManage],
        (DocumentType.Proposal, _) => [Permissions.ProposalCreate],
        (DocumentType.ProposalSummary, _) => [Permissions.ProposalAggregate],
        (DocumentType.CallOrder, _) => [Permissions.CallOrderCreate],
        _ => [Permissions.TemplateManage],
    };
}

/// <summary>Document dossier core (spec §5.7): versions with SHA-256, PDF rendition, online editing via ONLYOFFICE.</summary>
public sealed class DocumentService(
    IAppDbContext db,
    IFileStorage storage,
    TemplateService templates,
    IPdfConverter pdf,
    IDocumentEditorService editor,
    IUrlSigner urlSigner,
    IAuditLogger audit,
    WorkflowAccess workflowAccess,
    IDocxRevisions revisions,
    IServiceProvider services,
    ICurrentUser currentUser,
    IClock clock,
    DataScope dataScope)
{
    public const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EditorTokenLifetime = TimeSpan.FromHours(12);

    /// <summary>Creates a sandbox document from a template rendered with sample data (Phase 2 verification flow).</summary>
    public async Task<Result<Guid>> CreateSampleAsync(CreateSampleDocumentRequest r, CancellationToken ct)
    {
        var template = await db.DocumentTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == r.TemplateId, ct);
        if (template is null)
        {
            return Error.NotFound("TEMPLATE_NOT_FOUND");
        }

        var rendered = await templates.RenderAsync(template.Id, TemplateFieldCatalog.SampleData(template.Kind), ct);
        if (!rendered.IsSuccess)
        {
            return rendered.Error!;
        }

        var title = string.IsNullOrWhiteSpace(r.Title) ? $"{template.Name} (tài liệu thử)" : r.Title.Trim();
        var document = new Document(DocumentType.TemplateSample, title, nameof(DocumentTemplate), template.Id, null, null, template.Id);
        db.Documents.Add(document);
        await AddDocxVersionAsync(document, rendered.Value, "Sinh từ mẫu biểu với dữ liệu mẫu", currentUser.UserId, currentUser.FullName, ct);
        await db.SaveChangesAsync(ct);
        return document.Id;
    }

    /// <summary>Creates a business document from rendered DOCX bytes (used by contracts, receipts... in later phases).</summary>
    public async Task<Document> CreateFromDocxAsync(DocumentType type, string title, string ownerType, Guid ownerId, Guid? supplierId, Guid? contractId, Guid? templateId, byte[] docx, string reason, CancellationToken ct)
    {
        var document = new Document(type, title, ownerType, ownerId, supplierId, contractId, templateId);
        db.Documents.Add(document);
        await AddDocxVersionAsync(document, docx, reason, currentUser.UserId, currentUser.FullName, ct);
        return document;
    }

    public async Task<Result<DocumentDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var document = await db.Documents.AsNoTracking().Include(d => d.Versions).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null || !await CanViewAsync(document, ct))
        {
            return Error.NotFound();
        }

        var templateName = document.TemplateId is { } tid
            ? await db.DocumentTemplates.AsNoTracking().Where(t => t.Id == tid).Select(t => t.Name).FirstOrDefaultAsync(ct)
            : null;
        return ToDto(document, templateName);
    }

    /// <summary>Uploads a DOCX or PDF as a new version (e.g. procurement edits offline).</summary>
    public async Task<Result<Guid>> UploadVersionAsync(Guid id, UploadedFile file, string? reason, CancellationToken ct)
    {
        var document = await db.Documents.Include(d => d.Versions).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null || !await CanViewAsync(document, ct))
        {
            return Error.NotFound();
        }

        if (!currentUser.Permissions.Overlaps(DocumentPermissions.Edit(document.Type, EditorMode.Edit)))
        {
            return Error.Forbidden();
        }

        var kind = FileInspector.Detect(file.Content);
        if (kind is not (FileKind.Docx or FileKind.Pdf))
        {
            return Error.Validation("DOCUMENT_FILE_TYPE_INVALID");
        }

        if (await LockedInWorkflowAsync(document, ct))
        {
            return Error.Forbidden("DOCUMENT_LOCKED_IN_WORKFLOW");
        }

        var note = string.IsNullOrWhiteSpace(reason) ? "Tải lên phiên bản mới" : reason.Trim();
        var version = kind == FileKind.Docx
            ? await AddDocxVersionAsync(document, file.Content, note, currentUser.UserId, currentUser.FullName, ct)
            : await AddPdfVersionAsync(document, file.Content, note, ct);
        await db.SaveChangesAsync(ct);
        return version.Id;
    }

    /// <summary>Renders the PDF (signable rendition) of a DOCX version through Gotenberg.</summary>
    public async Task<Result<DocumentVersionDto>> RenderPdfAsync(Guid id, Guid versionId, CancellationToken ct)
    {
        var document = await db.Documents.Include(d => d.Versions).FirstOrDefaultAsync(d => d.Id == id, ct);
        var version = document?.Versions.FirstOrDefault(v => v.Id == versionId);
        if (document is null || version is null || !await CanViewAsync(document, ct))
        {
            return Error.NotFound();
        }

        if (version.DocxFileKey is null)
        {
            return Error.Validation("DOCUMENT_DOCX_REQUIRED");
        }

        if (version.IsLocked)
        {
            return Error.Validation("DOCUMENT_VERSION_LOCKED");
        }

        await RenderVersionPdfAsync(document, version, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(version, document.CurrentVersionId);
    }

    /// <summary>
    /// Makes the current version signable (spec §6.1 B3-5): with <paramref name="finalize"/> every remaining tracked change is
    /// accepted into a new "bản chốt" version first; then the PDF rendition is produced if missing. No-op once signed.
    /// </summary>
    public async Task PrepareForSigningAsync(Document document, bool finalize, CancellationToken ct)
    {
        var version = document.CurrentVersion;
        if (version is null || version.IsLocked || version.DocxFileKey is null)
        {
            return;
        }

        if (finalize)
        {
            var docx = await ReadAllAsync(version.DocxFileKey, ct);
            if (revisions.List(docx).Count > 0)
            {
                version = await AddDocxVersionAsync(document, revisions.AcceptAll(docx), "Chốt bản để ký: chấp nhận các thay đổi đã theo dõi",
                    currentUser.UserId, currentUser.FullName ?? "Hệ thống", ct);
            }
        }

        if (version.PdfFileKey is null)
        {
            await RenderVersionPdfAsync(document, version, ct);
        }
    }

    /// <summary>Current DOCX of a document (for revision review), with its version id.</summary>
    internal async Task<(DocumentVersion Version, byte[] Docx)?> ReadCurrentDocxAsync(Document document, CancellationToken ct)
    {
        var version = document.CurrentVersion;
        return version?.DocxFileKey is null ? null : (version, await ReadAllAsync(version.DocxFileKey, ct));
    }

    private async Task RenderVersionPdfAsync(Document document, DocumentVersion version, CancellationToken ct)
    {
        var docx = await ReadAllAsync(version.DocxFileKey!, ct);
        var bytes = await pdf.ConvertToPdfAsync(docx, document.Title, ct);
        using var stream = new MemoryStream(bytes);
        var stored = await storage.UploadAsync($"documents/{document.Id:N}/v{version.VersionNumber}/document.pdf", stream, "application/pdf", ct);
        version.AttachPdf(stored.ObjectKey, stored.Sha256, stored.Size);
    }

    /// <summary>Short-lived presigned download link after the permission check (spec §11). Every download is audited.</summary>
    public async Task<Result<FileLinkDto>> GetFileLinkAsync(Guid id, Guid versionId, string format, CancellationToken ct)
    {
        var document = await db.Documents.AsNoTracking().Include(d => d.Versions).FirstOrDefaultAsync(d => d.Id == id, ct);
        var version = document?.Versions.FirstOrDefault(v => v.Id == versionId);
        if (document is null || version is null || !await CanViewAsync(document, ct))
        {
            return Error.NotFound();
        }

        var key = format.Equals("pdf", StringComparison.OrdinalIgnoreCase) ? version.PdfFileKey : version.DocxFileKey;
        if (key is null)
        {
            return Error.NotFound("DOCUMENT_FILE_NOT_FOUND");
        }

        var url = await storage.GetPresignedDownloadUrlAsync(key, LinkLifetime, ct);
        audit.Log("Download", nameof(Document), document.Id.ToString(), new { versionId, version.VersionNumber, format });
        await db.SaveChangesAsync(ct);
        var extension = key.EndsWith(".pdf", StringComparison.Ordinal) ? "pdf" : "docx";
        return new FileLinkDto(url, $"{Slug(document.Title)}_v{version.VersionNumber}.{extension}", clock.UtcNow.Add(LinkLifetime));
    }

    /// <summary>Streams a version's file through the API (used by the PDF viewer to avoid cross-origin issues).</summary>
    public async Task<Result<(byte[] Content, string ContentType, string FileName)>> ReadFileAsync(Guid id, Guid versionId, string format, CancellationToken ct)
    {
        var document = await db.Documents.AsNoTracking().Include(d => d.Versions).FirstOrDefaultAsync(d => d.Id == id, ct);
        var version = document?.Versions.FirstOrDefault(v => v.Id == versionId);
        if (document is null || version is null || !await CanViewAsync(document, ct))
        {
            return Error.NotFound();
        }

        var isPdf = format.Equals("pdf", StringComparison.OrdinalIgnoreCase);
        var key = isPdf ? version.PdfFileKey : version.DocxFileKey;
        if (key is null)
        {
            return Error.NotFound("DOCUMENT_FILE_NOT_FOUND");
        }

        audit.Log("View", nameof(Document), document.Id.ToString(), new { versionId, version.VersionNumber, format });
        await db.SaveChangesAsync(ct);
        return (await ReadAllAsync(key, ct), isPdf ? "application/pdf" : DocxContentType, $"{Slug(document.Title)}_v{version.VersionNumber}.{(isPdf ? "pdf" : "docx")}");
    }

    /// <summary>Opens an ONLYOFFICE session on the current version (review mode forces track changes).</summary>
    public async Task<Result<EditorSessionDto>> StartEditSessionAsync(Guid id, StartEditRequest r, Uri apiBaseForEditor, CancellationToken ct)
    {
        var document = await db.Documents.Include(d => d.Versions).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (document is null || !await CanViewAsync(document, ct))
        {
            return Error.NotFound();
        }

        if (!currentUser.Permissions.Overlaps(DocumentPermissions.Edit(document.Type, r.Mode)))
        {
            return Error.Forbidden();
        }

        var current = document.CurrentVersion;
        if (current?.DocxFileKey is null)
        {
            return Error.Validation("DOCUMENT_DOCX_REQUIRED");
        }

        if (r.Mode != EditorMode.View && document.Status is DocumentStatus.Completed or DocumentStatus.Cancelled)
        {
            return Error.Validation("DOCUMENT_CLOSED");
        }

        if (r.Mode != EditorMode.View && await LockedInWorkflowAsync(document, ct))
        {
            return Error.Forbidden("DOCUMENT_LOCKED_IN_WORKFLOW");
        }

        var session = new DocumentEditSession(document.Id, current.Id, r.Mode.ToString().ToLowerInvariant(), currentUser.UserId!.Value, clock.UtcNow);
        db.DocumentEditSessions.Add(session);
        audit.Log("OpenEditor", nameof(Document), document.Id.ToString(), new { sessionId = session.Id, mode = r.Mode.ToString(), current.VersionNumber });
        await db.SaveChangesAsync(ct);

        var token = Uri.EscapeDataString(urlSigner.Sign(session.Id.ToString("N"), EditorTokenLifetime));
        var key = session.Id.ToString("N");
        var config = editor.BuildConfig(new EditorConfigRequest(
            key,
            document.Title,
            new Uri(apiBaseForEditor, $"api/onlyoffice/files/{key}?token={token}"),
            new Uri(apiBaseForEditor, $"api/onlyoffice/callback/{key}?token={token}"),
            currentUser.UserId!.Value.ToString("N"),
            currentUser.FullName ?? currentUser.UserName ?? "Người dùng",
            r.Mode));
        return new EditorSessionDto(session.Id, new Uri(editor.EditorScriptBaseUrl, "web-apps/apps/api/documents/api.js").ToString(), config.Config);
    }

    /// <summary>Called by the document server (no user session): returns the base DOCX of an edit session.</summary>
    public async Task<Result<byte[]>> GetEditorFileAsync(string sessionKey, string? token, CancellationToken ct)
    {
        if (!TryResolveSession(sessionKey, token, out var sessionId))
        {
            return Error.Forbidden();
        }

        var session = await db.DocumentEditSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
        {
            return Error.NotFound();
        }

        var key = await db.Documents.IgnoreQueryFilters().Where(d => d.Id == session.DocumentId)
            .SelectMany(d => d.Versions).Where(v => v.Id == session.BaseVersionId).Select(v => v.DocxFileKey).FirstOrDefaultAsync(ct);
        return key is null ? Error.NotFound() : await ReadAllAsync(key, ct);
    }

    /// <summary>Save callback from the document server: stores the edited file as a new version.</summary>
    public async Task<Result> HandleEditorCallbackAsync(string sessionKey, string? token, string body, string? authorization, CancellationToken ct)
    {
        if (!TryResolveSession(sessionKey, token, out var sessionId))
        {
            return Result.Failure(Error.Forbidden());
        }

        var callback = editor.ParseCallback(body, authorization);
        if (callback is null || callback.Key != sessionKey)
        {
            return Result.Failure(Error.Forbidden("EDITOR_CALLBACK_INVALID"));
        }

        var session = await db.DocumentEditSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
        {
            return Result.Failure(Error.NotFound());
        }

        Guid? newVersionId = null;
        if (callback.HasNewFile && session.IsOpen)
        {
            var document = await db.Documents.IgnoreQueryFilters().Include(d => d.Versions).FirstAsync(d => d.Id == session.DocumentId, ct);
            var userName = await db.Users.IgnoreQueryFilters().Where(u => u.Id == session.UserId).Select(u => u.FullName).FirstOrDefaultAsync(ct);
            var content = await editor.DownloadEditedFileAsync(callback.Url!, ct);
            var reason = session.Mode == "review" ? "Rà soát trực tuyến (có theo dõi thay đổi)" : "Chỉnh sửa trực tuyến";
            var version = await AddDocxVersionAsync(document, content, reason, session.UserId, userName, ct);
            newVersionId = version.Id;
            audit.Log("EditorSave", nameof(Document), document.Id.ToString(), new { sessionId, version.VersionNumber, callback.Status });
        }

        if (callback.ClosesSession)
        {
            session.Close(clock.UtcNow, newVersionId);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    internal async Task<DocumentVersion> AddDocxVersionAsync(Document document, byte[] docx, string reason, Guid? userId, string? userName, CancellationToken ct)
    {
        var version = new DocumentVersion(document.Id, reason, userId, userName, clock.UtcNow);
        var restarts = document.AddVersion(version);
        db.DocumentVersions.Add(version);
        using var stream = new MemoryStream(docx);
        var stored = await storage.UploadAsync($"documents/{document.Id:N}/v{version.VersionNumber}/document.docx", stream, DocxContentType, ct);
        version.AttachDocx(stored.ObjectKey, stored.Sha256, stored.Size);
        if (restarts)
        {
            await RestartWorkflowsAsync(document, version, userId, userName, ct);
        }

        return version;
    }

    /// <summary>A new version after a signature invalidates every signature: the workflow restarts from its first step (spec §5.7).</summary>
    private async Task RestartWorkflowsAsync(Document document, DocumentVersion version, Guid? userId, string? userName, CancellationToken ct)
    {
        var instances = await db.WorkflowInstances.Include("_steps").Include("_events")
            .Where(w => w.DocumentId == document.Id && (w.Status == WorkflowStatus.Running || w.Status == WorkflowStatus.Returned))
            .ToListAsync(ct);
        var observers = services.GetServices<IWorkflowObserver>().Where(o => o.Handles(document.Type)).ToList();
        foreach (var wf in instances)
        {
            wf.RestartForNewVersion(userId ?? Guid.Empty, userName ?? "Hệ thống", clock.UtcNow);
            foreach (var observer in observers)
            {
                await observer.OnChangedAsync(new WorkflowChange(wf, document, WorkflowChangeKind.Restarted, null, null), ct);
            }
        }

        if (instances.Count == 0)
        {
            document.MarkDraft();
        }
        audit.Log("WorkflowReset", nameof(Document), document.Id.ToString(), new { version.VersionNumber }, "Phiên bản mới sau khi đã có chữ ký — luồng ký phải bắt đầu lại.");
    }

    /// <summary>
    /// While signing is in progress only the assignee of a step that allows editing may change the content — whatever the
    /// channel (online editor, DOCX or PDF upload).
    /// </summary>
    private async Task<bool> LockedInWorkflowAsync(Document document, CancellationToken ct)
    {
        var running = await db.WorkflowInstances.Include("_steps").FirstOrDefaultAsync(w => w.DocumentId == document.Id && w.Status == WorkflowStatus.Running, ct);
        if (running?.ActiveStep is not { } active)
        {
            return false;
        }

        var standing = await workflowAccess.StandingAsync(running, active, ct);
        var editable = active.AllowEdit || active.ActionType == WorkflowActionType.SupplierReview;
        return !standing.CanAct || !editable;
    }

    private async Task<DocumentVersion> AddPdfVersionAsync(Document document, byte[] content, string reason, CancellationToken ct)
    {
        var version = new DocumentVersion(document.Id, reason, currentUser.UserId, currentUser.FullName, clock.UtcNow);
        var restarts = document.AddVersion(version);
        db.DocumentVersions.Add(version);
        using var stream = new MemoryStream(content);
        var stored = await storage.UploadAsync($"documents/{document.Id:N}/v{version.VersionNumber}/document.pdf", stream, "application/pdf", ct);
        version.AttachPdf(stored.ObjectKey, stored.Sha256, stored.Size);
        if (restarts)
        {
            await RestartWorkflowsAsync(document, version, currentUser.UserId, currentUser.FullName, ct);
        }

        return version;
    }

    private bool TryResolveSession(string sessionKey, string? token, out Guid sessionId)
    {
        sessionId = Guid.Empty;
        return urlSigner.TryValidate(token, out var signed) && signed == sessionKey && Guid.TryParseExact(sessionKey, "N", out sessionId);
    }

    private bool CanView(Document document) => currentUser.Permissions.Overlaps(DocumentPermissions.View(document.Type));

    /// <summary>
    /// Permission on the document type within the user's data scope (contract documents), or taking part in its signing
    /// workflow (signers of other departments).
    /// </summary>
    internal async Task<bool> CanViewAsync(Guid documentId, CancellationToken ct) =>
        await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct) is { } document && await CanViewAsync(document, ct);

    private async Task<bool> CanViewAsync(Document document, CancellationToken ct) =>
        (CanView(document) && (document.ContractId is null || await dataScope.CanSeeContractAsync(document.ContractId, ct)))
        || await workflowAccess.IsParticipantAsync(document.Id, ct);

    private DocumentDto ToDto(Document d, string? templateName) => new(
        d.Id, d.Type, d.Title, d.Status, d.OwnerEntityType, d.OwnerId, d.ContractId, d.TemplateId, templateName, d.CurrentVersionId,
        [.. d.Versions.OrderByDescending(v => v.VersionNumber).Select(v => ToDto(v, d.CurrentVersionId))],
        currentUser.Permissions.Overlaps(DocumentPermissions.Edit(d.Type, EditorMode.Edit)));

    private static DocumentVersionDto ToDto(DocumentVersion v, Guid? currentId) => new(
        v.Id, v.VersionNumber, v.Reason, v.CreatedByName, v.CreatedAt, v.DocxFileKey is not null, v.DocxSha256, v.DocxSize,
        v.PdfFileKey is not null, v.PdfSha256, v.PdfSize, v.SignatureCount, v.IsLocked, v.IsFinal, v.Id == currentId);

    private async Task<byte[]> ReadAllAsync(string key, CancellationToken ct)
    {
        await using var stream = await storage.DownloadAsync(key, ct);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, ct);
        return copy.ToArray();
    }

    private static string Slug(string title)
    {
        var normalized = title.Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => c is 'đ' ? 'd' : c is 'Đ' ? 'D' : c)
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        var slug = new string([.. chars]);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');
        return slug.Length > 60 ? slug[..60] : slug.Length == 0 ? "tai-lieu" : slug;
    }
}
