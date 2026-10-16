using BlueContract.Domain.Common;

namespace BlueContract.Domain.Documents;

/// <summary>Một tệp không đạt khi kiểm tra toàn vẹn: mất trên kho lưu trữ hoặc SHA-256 khác giá trị đã lưu.</summary>
public sealed record IntegrityIssue(string ObjectKey, string Source, string? Title, string ExpectedSha256, string? ActualSha256, bool Missing);

/// <summary>
/// Lần kiểm tra toàn vẹn tài liệu (spec §11): mọi tệp có SHA-256 trong cơ sở dữ liệu được tải lại từ kho lưu trữ và băm lại.
/// </summary>
public sealed class IntegrityCheckRun : Entity
{
    private List<IntegrityIssue> _issues = [];

    private IntegrityCheckRun()
    {
        Trigger = string.Empty;
    }

    public IntegrityCheckRun(string trigger, DateTimeOffset startedAt)
    {
        Trigger = Guard.Required(trigger, "INTEGRITY_TRIGGER_REQUIRED");
        StartedAt = startedAt;
    }

    /// <summary>"Lịch định kỳ" hoặc tên người chạy tay.</summary>
    public string Trigger { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public int FilesChecked { get; private set; }

    public long BytesChecked { get; private set; }

    public int MismatchCount { get; private set; }

    public int MissingCount { get; private set; }

    public string? Error { get; private set; }

    public IReadOnlyList<IntegrityIssue> Issues => _issues;

    public bool IsHealthy => FinishedAt is not null && Error is null && MismatchCount == 0 && MissingCount == 0;

    public void Finish(int filesChecked, long bytesChecked, IReadOnlyCollection<IntegrityIssue> issues, DateTimeOffset now, string? error = null)
    {
        FilesChecked = filesChecked;
        BytesChecked = bytesChecked;
        _issues = [.. issues.Take(500)];
        MissingCount = issues.Count(i => i.Missing);
        MismatchCount = issues.Count(i => !i.Missing);
        FinishedAt = now;
        Error = error;
    }
}
