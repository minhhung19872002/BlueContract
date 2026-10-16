using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Common;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Signing;

public sealed record SigningProviderDto(string Code, string DisplayName, SigningMode Mode, bool IsDefault);

public sealed record BeginSignRequest(string? Provider, string? ClientCertificate, string? Comment);

public sealed record CompleteSignRequest(string SignatureValue);

public sealed record BatchSignRequest(IReadOnlyList<Guid> StepIds, string? Provider);

public sealed record SigningRequestDto(
    Guid RequestId,
    Guid StepId,
    Guid DocumentId,
    string Provider,
    SigningMode Mode,
    SigningRequestStatus Status,
    string? DataToSign,
    string? Message,
    string? Error,
    Guid? SignatureId);

public sealed record BatchSignResultDto(Guid StepId, bool Success, SigningRequestDto? Request, string? ErrorCode, string? ErrorMessage);

public sealed record VerifyResultDto(int SignatureCount, bool AllValid, IReadOnlyList<PdfSignatureInfo> Signatures);

/// <summary>
/// Orchestrates digital signing on workflow steps (spec §8): prepare the PDF at the step's anchor, call the provider,
/// embed the CMS as an incremental update, record the Signature and advance the workflow.
/// </summary>
public sealed class SigningService(
    IAppDbContext db,
    IEnumerable<ISigningProvider> providers,
    IPdfSignatureService pdf,
    IFileStorage storage,
    WorkflowAccess access,
    WorkflowService workflows,
    DocumentService documents,
    IAuditLogger audit,
    ICurrentUser current,
    IClock clock,
    SigningDefaults defaults,
    SignatureImageService images)
{
    private static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(10);

    public IReadOnlyList<SigningProviderDto> Providers() =>
        providers.Where(p => p.IsAvailable && p.ProviderCode != SigningDefaults.InitialStampProvider)
            .Select(p => new SigningProviderDto(p.ProviderCode, p.DisplayName, p.Mode, p.ProviderCode == defaults.DefaultProvider))
            .ToList();

    public async Task<Result<SigningRequestDto>> BeginAsync(Guid stepId, BeginSignRequest r, CancellationToken ct)
    {
        var (wf, step, error) = await workflows.LoadActiveAsync(stepId, ct);
        if (error is not null)
        {
            return error;
        }

        if (step!.Status == StepStatus.Done)
        {
            // Idempotent: pressing "Ký" twice returns the completed request instead of signing again.
            var done = await db.SigningRequests.AsNoTracking().Where(x => x.StepInstanceId == stepId && x.Status == SigningRequestStatus.Completed)
                .OrderByDescending(x => x.CompletedAt).FirstOrDefaultAsync(ct);
            return done is null ? Error.Conflict("WORKFLOW_STEP_NOT_ACTIVE") : ToDto(done, null);
        }

        if (!step.RequiresSignature)
        {
            return Error.Validation("WORKFLOW_STEP_NOT_SIGNING");
        }

        var standing = await access.StandingAsync(wf!, step, ct);
        if (!standing.CanAct)
        {
            return Error.Forbidden(standing.DenyReason ?? "FORBIDDEN");
        }

        if (await workflows.CheckGateAsync(wf!, step, r.Comment, ct) is { } blocked)
        {
            return blocked;
        }

        var providerCode = step.ActionType == WorkflowActionType.Initial && !defaults.InitialRequiresCa
            ? SigningDefaults.InitialStampProvider
            : r.Provider ?? defaults.DefaultProvider;
        var provider = providers.FirstOrDefault(p => p.ProviderCode == providerCode && p.IsAvailable);
        if (provider is null)
        {
            return Error.Validation("SIGNING_PROVIDER_NOT_CONFIGURED");
        }

        // One open attempt per step: earlier pending attempts by this user are cancelled.
        var now = clock.UtcNow;
        var open = await db.SigningRequests.Where(x => x.StepInstanceId == stepId && x.Status == SigningRequestStatus.Pending).ToListAsync(ct);
        foreach (var o in open)
        {
            if (o.UserId != current.UserId && o.IsOpen(now))
            {
                return Error.Conflict("SIGNING_IN_PROGRESS");
            }

            o.Cancel(now);
        }

        var document = await db.Documents.Include(d => d.Versions).FirstAsync(d => d.Id == wf!.DocumentId, ct);
        if (document.CurrentVersion is { IsLocked: false, PdfFileKey: null })
        {
            await documents.PrepareForSigningAsync(document, finalize: true, ct);
            await db.SaveChangesAsync(ct);
        }

        var version = document.CurrentVersion;
        if (version?.PdfFileKey is null || version.PdfSha256 is null)
        {
            return Error.Validation("DOCUMENT_PDF_REQUIRED");
        }

        var kind = step.ActionType switch
        {
            WorkflowActionType.Initial => SignatureKind.Initial,
            WorkflowActionType.OrgSeal => SignatureKind.Organization,
            _ => SignatureKind.Personal,
        };
        var signerName = kind == SignatureKind.Organization
            ? await db.Organizations.Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "Bệnh viện"
            : current.FullName ?? current.UserName ?? string.Empty;
        var signerTitle = standing.Title ?? await db.Users.IgnoreQueryFilters().Where(u => u.Id == current.UserId).Select(u => u.JobTitle).FirstOrDefaultAsync(ct);

        var original = await ReadAllAsync(version.PdfFileKey, ct);
        var image = await images.ForSignatureAsync(kind, current.UserId!.Value, ct);
        var prepared = pdf.Prepare(original, new SignatureAppearance(kind, signerName, signerTitle, standing.Caption, step.SignatureAnchor, now, version.SignatureCount, image));

        var request = new SigningRequest(document.Id, version.Id, step.Id, current.UserId!.Value, standing.OnBehalfOf, kind, provider.ProviderCode,
            prepared.FieldName, $"signing/{Guid.CreateVersion7():N}.pdf", version.PdfSha256, Convert.ToHexStringLower(prepared.DocumentDigest), now, RequestLifetime);
        using (var stream = new MemoryStream(prepared.Content))
        {
            await storage.UploadAsync(request.PreparedPdfKey, stream, "application/pdf", ct);
        }

        db.SigningRequests.Add(request);
        await db.SaveChangesAsync(ct);

        SigningSession session;
        try
        {
            session = await provider.BeginAsync(new SignRequest(request.Id, current.UserId!.Value, signerName, kind, prepared.DocumentDigest,
                r.ClientCertificate is null ? null : Convert.FromBase64String(r.ClientCertificate)), ct);
        }
        catch (DomainException ex)
        {
            request.Fail(ex.Code, clock.UtcNow);
            await db.SaveChangesAsync(ct);
            return Error.Validation(ex.Code);
        }

        if (session.Status == SigningStatus.Completed && session.Cms is not null)
        {
            return await FinalizeAsync(request, session.Cms, session.Certificate!, r.Comment, ct);
        }

        request.SetProviderInfo(session.DataToSign is null ? null : Convert.ToBase64String(session.DataToSign), session.Message);
        await db.SaveChangesAsync(ct);
        return ToDto(request, provider.Mode);
    }

    /// <summary>Polls a remote-signing request; finalises it once the provider has the signature.</summary>
    public async Task<Result<SigningRequestDto>> StatusAsync(Guid requestId, CancellationToken ct)
    {
        var request = await db.SigningRequests.FirstOrDefaultAsync(x => x.Id == requestId && x.UserId == current.UserId, ct);
        if (request is null)
        {
            return Error.NotFound();
        }

        var provider = providers.First(p => p.ProviderCode == request.Provider);
        if (request.Status != SigningRequestStatus.Pending || provider.Mode != SigningMode.RemoteAsync)
        {
            return ToDto(request, provider.Mode);
        }

        if (!request.IsOpen(clock.UtcNow))
        {
            request.Fail("SIGNING_SESSION_EXPIRED", clock.UtcNow, expired: true);
            await db.SaveChangesAsync(ct);
            return ToDto(request, provider.Mode);
        }

        var session = await provider.GetStatusAsync(request.Id.ToString("N"), ct);
        switch (session.Status)
        {
            case SigningStatus.Completed when session.Cms is not null:
                return await FinalizeAsync(request, session.Cms, session.Certificate!, null, ct);
            case SigningStatus.Failed or SigningStatus.Expired:
                request.Fail(session.Message ?? "SIGNING_FAILED", clock.UtcNow, session.Status == SigningStatus.Expired);
                await db.SaveChangesAsync(ct);
                break;
        }

        request.SetProviderInfo(request.DataToSign, session.Message);
        return ToDto(request, provider.Mode);
    }

    /// <summary>Client-side (USB token) completion with the signature value produced by the CA plugin.</summary>
    public async Task<Result<SigningRequestDto>> CompleteAsync(Guid requestId, CompleteSignRequest r, CancellationToken ct)
    {
        var request = await db.SigningRequests.FirstOrDefaultAsync(x => x.Id == requestId && x.UserId == current.UserId, ct);
        if (request is null)
        {
            return Error.NotFound();
        }

        if (!request.IsOpen(clock.UtcNow))
        {
            return Error.Validation("SIGNING_SESSION_EXPIRED");
        }

        var provider = providers.First(p => p.ProviderCode == request.Provider);
        SignResult result;
        try
        {
            result = await provider.CompleteAsync(request.Id.ToString("N"), new SignCompletion(Convert.FromBase64String(r.SignatureValue)), ct);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Code);
        }
        catch (FormatException)
        {
            return Error.Validation("SIGNING_SIGNATURE_INVALID");
        }

        return await FinalizeAsync(request, result.Cms, result.Certificate, null, ct);
    }

    /// <summary>Ký hàng loạt: server-side providers sign each step in turn; remote ones return pending requests to poll.</summary>
    public async Task<List<BatchSignResultDto>> BatchAsync(BatchSignRequest r, CancellationToken ct)
    {
        var results = new List<BatchSignResultDto>();
        foreach (var stepId in r.StepIds.Distinct())
        {
            var outcome = await BeginAsync(stepId, new BeginSignRequest(r.Provider, null, null), ct);
            results.Add(outcome.IsSuccess
                ? new BatchSignResultDto(stepId, true, outcome.Value, null, null)
                : new BatchSignResultDto(stepId, false, null, outcome.Error!.Code, ErrorMessages.Get(outcome.Error.Code)));
            ((DbContext)db).ChangeTracker.Clear();
        }

        return results;
    }

    /// <summary>Verifies the signatures of a document version (or an uploaded PDF).</summary>
    /// <summary>Signature details of a stored version — for users who may view the document (AUDIT-1 §10.8).</summary>
    public async Task<Result<VerifyResultDto>> VerifyVersionAsync(Guid documentId, Guid versionId, CancellationToken ct)
    {
        if (!await documents.CanViewAsync(documentId, ct))
        {
            return Error.NotFound();
        }

        var key = await db.Documents.Where(d => d.Id == documentId).SelectMany(d => d.Versions)
            .Where(v => v.Id == versionId).Select(v => v.PdfFileKey).FirstOrDefaultAsync(ct);
        return key is null ? Error.NotFound() : Verify(await ReadAllAsync(key, ct));
    }

    /// <summary>Verifies an uploaded PDF: a file that starts like a PDF but cannot be read is the user's error, not a 500.</summary>
    public Result<VerifyResultDto> VerifyUpload(byte[] content)
    {
        if (FileInspector.Detect(content) != FileKind.Pdf)
        {
            return Error.Validation("DOCUMENT_FILE_TYPE_INVALID");
        }

        try
        {
            return Verify(content);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            return Error.Validation("PDF_DAMAGED");
        }
    }

    public VerifyResultDto Verify(byte[] content)
    {
        var signatures = pdf.Verify(content);
        return new VerifyResultDto(signatures.Count, signatures.Count > 0 && signatures.All(s => s.IntegrityValid), signatures);
    }

    private async Task<Result<SigningRequestDto>> FinalizeAsync(SigningRequest request, byte[] cms, SignerCertificate certificate, string? comment, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var document = await db.Documents.Include(d => d.Versions).FirstAsync(d => d.Id == request.DocumentId, ct);
        var version = document.Versions.First(v => v.Id == request.DocumentVersionId);
        if (version.PdfSha256 != request.BasePdfSha256 || document.CurrentVersionId != version.Id)
        {
            request.Fail("DOCUMENT_CHANGED_DURING_SIGNING", now);
            await db.SaveChangesAsync(ct);
            return Error.Conflict("DOCUMENT_CHANGED_DURING_SIGNING");
        }

        var signed = pdf.Embed(await ReadAllAsync(request.PreparedPdfKey, ct), request.FieldName, cms);
        var check = pdf.Verify(signed).FirstOrDefault(s => s.FieldName == request.FieldName);
        if (check is null || !check.IntegrityValid)
        {
            request.Fail("SIGNING_SIGNATURE_INVALID", now);
            await db.SaveChangesAsync(ct);
            return Error.Validation("SIGNING_SIGNATURE_INVALID");
        }

        StoredFile stored;
        using (var stream = new MemoryStream(signed))
        {
            stored = await storage.UploadAsync($"documents/{document.Id:N}/v{version.VersionNumber}/document-s{version.SignatureCount + 1}.pdf", stream, "application/pdf", ct);
        }

        var hashBefore = version.PdfSha256!;
        version.ApplySignedPdf(stored.ObjectKey, stored.Sha256, stored.Size);

        var (wf, step, error) = await workflows.LoadActiveAsync(request.StepInstanceId, ct);
        if (error is not null || step!.Status != StepStatus.Active)
        {
            request.Fail(error?.Code ?? "WORKFLOW_STEP_NOT_ACTIVE", now);
            await db.SaveChangesAsync(ct);
            return error ?? Error.Conflict("WORKFLOW_STEP_NOT_ACTIVE");
        }

        var standing = await access.StandingAsync(wf!, step, ct);
        var signerName = request.Kind == SignatureKind.Organization
            ? await db.Organizations.Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "Bệnh viện"
            : current.FullName ?? current.UserName ?? string.Empty;
        var signature = new Signature(document.Id, version.Id, step.Id, request.UserId, signerName, standing.Title, request.OnBehalfOfUserId,
            standing.Caption, request.Kind, request.Provider, request.FieldName, certificate.SerialNumber, certificate.Subject, certificate.Issuer,
            now, hashBefore, stored.Sha256);
        db.Signatures.Add(signature);
        request.Complete(signature.Id, now);

        var actorName = standing.Caption is null ? current.FullName ?? string.Empty : $"{current.FullName} ({standing.Caption})";
        var outcome = wf!.Act(step.Id, request.UserId, actorName, request.OnBehalfOfUserId, comment, signature.Id, now);
        await workflows.AfterActAsync(wf, outcome, step, comment, ct);
        audit.Log("Sign", nameof(Document), document.Id.ToString(), new
        {
            step = step.Code,
            request.Provider,
            kind = request.Kind.ToString(),
            certificate.SerialNumber,
            hashBefore,
            hashAfter = stored.Sha256,
            onBehalfOf = request.OnBehalfOfUserId,
        });
        await db.SaveChangesAsync(ct);
        return ToDto(request, providers.First(p => p.ProviderCode == request.Provider).Mode);
    }

    private static SigningRequestDto ToDto(SigningRequest r, SigningMode? mode) => new(
        r.Id, r.StepInstanceId, r.DocumentId, r.Provider, mode ?? SigningMode.ServerSide, r.Status, r.DataToSign, r.ProviderMessage,
        r.Error is null ? null : ErrorMessages.IsKnown(r.Error) ? ErrorMessages.Get(r.Error) : r.Error, r.SignatureId);

    private async Task<byte[]> ReadAllAsync(string key, CancellationToken ct)
    {
        await using var stream = await storage.DownloadAsync(key, ct);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, ct);
        return copy.ToArray();
    }
}

/// <summary>Provider selection (bound from the Signing section).</summary>
public sealed record SigningDefaults(string DefaultProvider, bool InitialRequiresCa = false)
{
    /// <summary>Internal provider used for ký nháy when no CA is required.</summary>
    public const string InitialStampProvider = "INITIAL_STAMP";
}
