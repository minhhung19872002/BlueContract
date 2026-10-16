using System.Diagnostics;
using System.Text.Json;
using BlueContract.Application.Common;
using BlueContract.Application.Receipts;
using BlueContract.Domain.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueContract.Application.Integration;

public sealed record IntegrationLogListItemDto(
    Guid Id,
    IntegrationDirection Direction,
    string System,
    string Operation,
    string Method,
    string Path,
    string? ExternalId,
    IntegrationLogStatus Status,
    int? ResponseStatus,
    string? ErrorCode,
    int Attempts,
    string? RemoteIp,
    string? ClientName,
    DateTimeOffset StartedAt,
    DateTimeOffset LastAttemptAt,
    long DurationMs,
    Guid? ReceiptId,
    bool CanRetry);

public sealed record IntegrationLogDto(
    Guid Id,
    IntegrationDirection Direction,
    string System,
    string Operation,
    string Method,
    string Path,
    string? ExternalId,
    IntegrationLogStatus Status,
    int? ResponseStatus,
    string? ErrorCode,
    string? ErrorMessage,
    int Attempts,
    string? RemoteIp,
    string? ClientName,
    DateTimeOffset StartedAt,
    DateTimeOffset LastAttemptAt,
    long DurationMs,
    Guid? ReceiptId,
    bool CanRetry,
    string? RequestBody,
    string? ResponseBody);

public sealed record IntegrationLogFilter(IntegrationDirection? Direction, IntegrationLogStatus? Status, string? Operation, DateTimeOffset? From, DateTimeOffset? To);

public sealed record PullResultDto(int Received, int Created, int Updated, int Duplicates, int Failed, IReadOnlyList<string> Errors);

/// <summary>
/// Writes integration logs in their own unit of work, so a failed business operation (rolled back) is still logged and
/// a log never commits half of a failed operation.
/// </summary>
public sealed class IntegrationLogWriter(IServiceScopeFactory scopes, IClock clock)
{
    public async Task<Guid> BeginAsync(IntegrationDirection direction, string operation, string method, string path, string? externalId, string? body,
        string? remoteIp, string? clientName, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var log = new IntegrationLog(direction, "HIS", operation, method, path, externalId, body, remoteIp, clientName, clock.UtcNow);
        db.IntegrationLogs.Add(log);
        await db.SaveChangesAsync(ct);
        return log.Id;
    }

    public async Task CompleteAsync(Guid logId, IntegrationLogStatus status, int? responseStatus, string? responseBody, string? errorCode, string? errorMessage,
        long durationMs, Guid? receiptId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var log = await db.IntegrationLogs.FirstAsync(l => l.Id == logId, ct);
        log.Complete(status, responseStatus, responseBody, errorCode, errorMessage, durationMs, receiptId);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Màn hình nhật ký tích hợp HIS + "Gửi lại" (spec §7.2), and the receipt pull (spec §6.4 B1).</summary>
public sealed class IntegrationLogService(
    IAppDbContext db,
    HisReceiptIngestion ingestion,
    IntegrationLogWriter writer,
    IHisClient his,
    IAuditLogger audit,
    IClock clock)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public Task<PagedResult<IntegrationLogListItemDto>> ListAsync(PageQuery q, IntegrationLogFilter f, CancellationToken ct)
    {
        var query = db.IntegrationLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            query = query.Where(l => (l.ExternalId != null && EF.Functions.ILike(l.ExternalId, p)) || EF.Functions.ILike(l.Path, p)
                || (l.ErrorCode != null && EF.Functions.ILike(l.ErrorCode, p)));
        }

        query = f.Direction is { } direction ? query.Where(l => l.Direction == direction) : query;
        query = f.Status is { } status ? query.Where(l => l.Status == status) : query;
        query = string.IsNullOrWhiteSpace(f.Operation) ? query : query.Where(l => l.Operation == f.Operation);
        query = f.From is { } from ? query.Where(l => l.StartedAt >= from) : query;
        query = f.To is { } to ? query.Where(l => l.StartedAt < to) : query;

        return query.OrderByDescending(l => l.StartedAt).ThenByDescending(l => l.Id)
            .Select(l => new IntegrationLogListItemDto(l.Id, l.Direction, l.System, l.Operation, l.Method, l.Path, l.ExternalId, l.Status, l.ResponseStatus,
                l.ErrorCode, l.Attempts, l.RemoteIp, l.ClientName, l.StartedAt, l.LastAttemptAt, l.DurationMs, l.ReceiptId,
                l.Direction == IntegrationDirection.Inbound && l.Operation == IntegrationLog.Operations.PushWarehouseReceipt && l.RequestBody != null
                    && l.Status == IntegrationLogStatus.Failed))
            .ToPagedAsync(q, ct);
    }

    public async Task<Result<IntegrationLogDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var l = await db.IntegrationLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return l is null
            ? Error.NotFound()
            : new IntegrationLogDto(l.Id, l.Direction, l.System, l.Operation, l.Method, l.Path, l.ExternalId, l.Status, l.ResponseStatus, l.ErrorCode,
                l.ErrorMessage, l.Attempts, l.RemoteIp, l.ClientName, l.StartedAt, l.LastAttemptAt, l.DurationMs, l.ReceiptId, l.CanRetry, l.RequestBody,
                l.ResponseBody);
    }

    /// <summary>"Gửi lại": processes the stored receipt payload again (after fixing master data, e.g. a missing contract).</summary>
    public async Task<Result<HisIngestResultDto>> RetryAsync(Guid id, CancellationToken ct)
    {
        var log = await db.IntegrationLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (log is null)
        {
            return Error.NotFound();
        }

        if (!log.CanRetry)
        {
            return Error.Conflict("INTEGRATION_RETRY_NOT_ALLOWED");
        }

        HisReceiptPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<HisReceiptPayload>(log.RequestBody!, Json);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null)
        {
            return Error.Validation("INTEGRATION_PAYLOAD_INVALID");
        }

        log.BeginRetry(clock.UtcNow);
        audit.Log("IntegrationRetry", nameof(IntegrationLog), log.Id.ToString(), new { log.ExternalId, log.Attempts });
        await db.SaveChangesAsync(ct);

        var watch = Stopwatch.StartNew();
        var result = await IngestSafelyAsync(payload, ct);
        await writer.CompleteAsync(log.Id, result.IsSuccess ? IntegrationLogStatus.Success : IntegrationLogStatus.Failed, result.IsSuccess ? 200 : 400,
            result.IsSuccess ? JsonSerializer.Serialize(result.Value, Json) : null, result.Error?.Code, result.Error is null ? null : ErrorMessages.Get(result.Error.Code),
            watch.ElapsedMilliseconds, result.IsSuccess ? result.Value.ReceiptId : null, ct);
        return result;
    }

    /// <summary>Kéo phiếu từ HIS (pull) for the last days; each receipt goes through the same idempotent ingestion.</summary>
    public async Task<PullResultDto> PullAsync(int lookbackDays, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var from = today.AddDays(-Math.Max(0, lookbackDays));
        var logId = await writer.BeginAsync(IntegrationDirection.Outbound, IntegrationLog.Operations.PullWarehouseReceipts, "GET",
            $"GetWarehouseReceipts?from={from:yyyy-MM-dd}&to={today:yyyy-MM-dd}", null, null, null, his.DisplayName, ct);
        var watch = Stopwatch.StartNew();
        IReadOnlyList<HisReceiptPayload> receipts;
        try
        {
            receipts = await his.GetWarehouseReceiptsAsync(from, today, ct);
        }
        catch (HisUnavailableException ex)
        {
            await writer.CompleteAsync(logId, IntegrationLogStatus.Failed, null, null, "HIS_UNAVAILABLE", ex.Message, watch.ElapsedMilliseconds, null, ct);
            return new PullResultDto(0, 0, 0, 0, 1, [ErrorMessages.Get("HIS_UNAVAILABLE")]);
        }

        int created = 0, updated = 0, duplicates = 0, failed = 0;
        var errors = new List<string>();
        foreach (var payload in receipts)
        {
            var result = await IngestSafelyAsync(payload, ct);
            ((DbContext)db).ChangeTracker.Clear();
            if (!result.IsSuccess)
            {
                failed++;
                errors.Add($"{payload.HisReceiptId}: {ErrorMessages.Get(result.Error!.Code)}");
                continue;
            }

            switch (result.Value.Outcome)
            {
                case HisIngestOutcome.Created:
                    created++;
                    break;
                case HisIngestOutcome.Updated:
                    updated++;
                    break;
                default:
                    duplicates++;
                    break;
            }
        }

        var summary = new PullResultDto(receipts.Count, created, updated, duplicates, failed, errors);
        await writer.CompleteAsync(logId, failed == 0 ? IntegrationLogStatus.Success : IntegrationLogStatus.Failed, 200, JsonSerializer.Serialize(summary, Json),
            failed == 0 ? null : "PULL_PARTIAL", failed == 0 ? null : string.Join("; ", errors.Take(20)), watch.ElapsedMilliseconds, null, ct);
        return summary;
    }

    /// <summary>Business exceptions (domain rules) become a failed result instead of aborting a batch.</summary>
    private async Task<Result<HisIngestResultDto>> IngestSafelyAsync(HisReceiptPayload payload, CancellationToken ct)
    {
        try
        {
            return await ingestion.IngestAsync(payload, ct);
        }
        catch (Domain.Common.DomainException ex)
        {
            ((DbContext)db).ChangeTracker.Clear();
            return Error.Validation(ex.Code);
        }
    }
}
