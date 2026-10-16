using System.Security.Cryptography;
using BlueContract.Application.Common;
using BlueContract.Application.Notifications;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BlueContract.Application.Admin;

public sealed record IntegrityRunListItemDto(
    Guid Id,
    string Trigger,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int FilesChecked,
    long BytesChecked,
    int MismatchCount,
    int MissingCount,
    string? Error,
    bool IsHealthy);

public sealed record IntegrityRunDto(IntegrityRunListItemDto Run, IReadOnlyList<IntegrityIssue> Issues);

/// <summary>
/// Kiểm tra toàn vẹn định kỳ (spec §11): every stored file with a recorded SHA-256 (document versions DOCX/PDF, template
/// versions, bid decision attachments) is downloaded again and re-hashed; mismatches or missing objects alert the admins.
/// </summary>
public sealed class IntegrityService(
    IAppDbContext db,
    IFileStorage storage,
    NotificationService notifications,
    IAuditLogger audit,
    IClock clock,
    ILogger<IntegrityService> logger)
{
    private sealed record Target(string Key, string Sha256, string Source, string? Title);

    /// <summary>An unfinished run older than this is treated as crashed and no longer blocks a new one.</summary>
    private static readonly TimeSpan StaleRunAfter = TimeSpan.FromHours(3);

    public async Task<IntegrityRunDto> RunAsync(string trigger, CancellationToken ct)
    {
        // Checked in the database rather than with an in-process lock so it also holds across API replicas.
        var staleBefore = clock.UtcNow - StaleRunAfter;
        if (await db.IntegrityCheckRuns.AnyAsync(r => r.FinishedAt == null && r.StartedAt > staleBefore, ct))
            throw new Domain.Common.DomainException("INTEGRITY_ALREADY_RUNNING");

        var run = new IntegrityCheckRun(trigger, clock.UtcNow);
        db.IntegrityCheckRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var issues = new List<IntegrityIssue>();
        int files = 0;
        long bytes = 0;
        string? error = null;
        try
        {
            foreach (var target in await TargetsAsync(ct))
            {
                files++;
                try
                {
                    await using var stream = await storage.DownloadAsync(target.Key, ct);
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                    {
                        hash.AppendData(buffer, 0, read);
                        bytes += read;
                    }

                    var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
                    if (!string.Equals(actual, target.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new IntegrityIssue(target.Key, target.Source, target.Title, target.Sha256, actual, Missing: false));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Integrity check: cannot read {Key}", target.Key);
                    issues.Add(new IntegrityIssue(target.Key, target.Source, target.Title, target.Sha256, null, Missing: true));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Integrity check failed");
            error = ex.Message;
        }

        run.Finish(files, bytes, issues, clock.UtcNow, error);
        audit.Log("IntegrityCheck", nameof(IntegrityCheckRun), run.Id.ToString(), new { files, run.MismatchCount, run.MissingCount, trigger });
        if (!run.IsHealthy)
        {
            var admins = await db.Users
                .Where(u => u.Status == UserStatus.Active && u.UserType == UserType.Internal
                    && u.Roles.Any(r => r.Role!.Permissions.Any(p => p.PermissionCode == Permissions.SettingManage || p.PermissionCode == Permissions.AuditView)))
                .Select(u => u.Id).ToListAsync(ct);
            await notifications.NotifyAsync(admins, NotificationTypes.IntegrityAlert, "Cảnh báo toàn vẹn tài liệu",
                error ?? $"{run.MismatchCount} tệp sai mã SHA-256, {run.MissingCount} tệp không đọc được trên kho lưu trữ.", "/quan-tri/toan-ven", email: true, ct);
        }

        await db.SaveChangesAsync(ct);
        return ToDto(run);
    }

    public Task<PagedResult<IntegrityRunListItemDto>> ListAsync(PageQuery q, CancellationToken ct) =>
        db.IntegrityCheckRuns.AsNoTracking().OrderByDescending(r => r.StartedAt)
            .Select(r => new IntegrityRunListItemDto(r.Id, r.Trigger, r.StartedAt, r.FinishedAt, r.FilesChecked, r.BytesChecked, r.MismatchCount, r.MissingCount,
                r.Error, r.FinishedAt != null && r.Error == null && r.MismatchCount == 0 && r.MissingCount == 0))
            .ToPagedAsync(q, ct);

    public async Task<Result<IntegrityRunDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var run = await db.IntegrityCheckRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return run is null ? Error.NotFound() : ToDto(run);
    }

    private async Task<List<Target>> TargetsAsync(CancellationToken ct)
    {
        var versions = await db.DocumentVersions.AsNoTracking()
            .Select(v => new { v.DocxFileKey, v.DocxSha256, v.PdfFileKey, v.PdfSha256, v.VersionNumber, Title = db.Documents.Where(d => d.Id == v.DocumentId).Select(d => d.Title).FirstOrDefault() })
            .ToListAsync(ct);
        var targets = new List<Target>();
        foreach (var v in versions)
        {
            if (v.DocxFileKey is not null && v.DocxSha256 is not null)
            {
                targets.Add(new Target(v.DocxFileKey, v.DocxSha256, "Tài liệu (DOCX)", $"{v.Title} — phiên bản {v.VersionNumber}"));
            }

            if (v.PdfFileKey is not null && v.PdfSha256 is not null)
            {
                targets.Add(new Target(v.PdfFileKey, v.PdfSha256, "Tài liệu (PDF)", $"{v.Title} — phiên bản {v.VersionNumber}"));
            }
        }

        targets.AddRange(await db.DocumentTemplates.AsNoTracking().SelectMany(t => t.Versions.Select(v => new Target(v.FileKey, v.Sha256, "Mẫu biểu", t.Name + " v" + v.VersionNumber)))
            .ToListAsync(ct));
        targets.AddRange(await db.BidDecisions.AsNoTracking().Where(b => b.AttachmentKey != null && b.AttachmentSha256 != null)
            .Select(b => new Target(b.AttachmentKey!, b.AttachmentSha256!, "Quyết định thầu", b.Number)).ToListAsync(ct));
        return targets;
    }

    private static IntegrityRunDto ToDto(IntegrityCheckRun r) => new(
        new IntegrityRunListItemDto(r.Id, r.Trigger, r.StartedAt, r.FinishedAt, r.FilesChecked, r.BytesChecked, r.MismatchCount, r.MissingCount, r.Error, r.IsHealthy),
        r.Issues);
}
