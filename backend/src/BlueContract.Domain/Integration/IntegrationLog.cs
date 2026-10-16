using BlueContract.Domain.Common;

namespace BlueContract.Domain.Integration;

public enum IntegrationDirection
{
    /// <summary>HIS gọi cổng.</summary>
    Inbound = 0,

    /// <summary>Cổng gọi HIS (pull job...).</summary>
    Outbound = 1,
}

public enum IntegrationLogStatus
{
    Success = 0,
    Failed = 1,

    /// <summary>Gửi trùng (cùng HisReceiptId và nội dung) — không xử lý lại.</summary>
    Duplicate = 2,

    /// <summary>Bị từ chối do xác thực (API key, chữ ký HMAC, timestamp, IP).</summary>
    Rejected = 3,
}

/// <summary>
/// Nhật ký tích hợp HIS (spec §7.2): request/response of every call, its outcome and the number of attempts. Inbound receipt
/// calls can be re-processed ("Gửi lại") from the admin screen.
/// </summary>
public sealed class IntegrationLog : Entity
{
    public const int MaxBodyLength = 200_000;

    private IntegrationLog()
    {
        System = string.Empty;
        Operation = string.Empty;
        Method = string.Empty;
        Path = string.Empty;
    }

    public IntegrationLog(IntegrationDirection direction, string system, string operation, string method, string path, string? externalId, string? requestBody,
        string? remoteIp, string? clientName, DateTimeOffset now)
    {
        Direction = direction;
        System = Guard.Required(system, "INTEGRATION_SYSTEM_REQUIRED");
        Operation = Guard.Required(operation, "INTEGRATION_OPERATION_REQUIRED");
        Method = method;
        Path = Truncate(path, 1000)!;
        ExternalId = Guard.Optional(externalId);
        RequestBody = Truncate(requestBody, MaxBodyLength);
        RemoteIp = remoteIp;
        ClientName = clientName;
        StartedAt = now;
        LastAttemptAt = now;
        Attempts = 1;
        Status = IntegrationLogStatus.Failed;
    }

    public IntegrationDirection Direction { get; private set; }

    /// <summary>"HIS".</summary>
    public string System { get; private set; }

    /// <summary>Tên nghiệp vụ: PushWarehouseReceipt, GetContracts, PullWarehouseReceipts...</summary>
    public string Operation { get; private set; }

    public string Method { get; private set; }

    public string Path { get; private set; }

    /// <summary>Khóa nghiệp vụ phía HIS (HisReceiptId) để tra cứu.</summary>
    public string? ExternalId { get; private set; }

    public string? RequestBody { get; private set; }

    public int? ResponseStatus { get; private set; }

    public string? ResponseBody { get; private set; }

    public IntegrationLogStatus Status { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>Số lần xử lý (1 + số lần gửi lại).</summary>
    public int Attempts { get; private set; }

    public string? RemoteIp { get; private set; }

    public string? ClientName { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset LastAttemptAt { get; private set; }

    public long DurationMs { get; private set; }

    /// <summary>Phiếu nhập kho tạo/cập nhật bởi lần gọi này.</summary>
    public Guid? ReceiptId { get; private set; }

    public bool CanRetry => Direction == IntegrationDirection.Inbound && Operation == Operations.PushWarehouseReceipt && RequestBody is not null
        && Status is IntegrationLogStatus.Failed;

    public void Complete(IntegrationLogStatus status, int? responseStatus, string? responseBody, string? errorCode, string? errorMessage, long durationMs, Guid? receiptId)
    {
        Status = status;
        ResponseStatus = responseStatus;
        ResponseBody = Truncate(responseBody, MaxBodyLength);
        ErrorCode = errorCode;
        ErrorMessage = Truncate(errorMessage, 2000);
        DurationMs = durationMs;
        ReceiptId = receiptId ?? ReceiptId;
    }

    /// <summary>"Gửi lại": one more attempt on the stored request.</summary>
    public void BeginRetry(DateTimeOffset now)
    {
        if (!CanRetry)
        {
            throw new DomainException("INTEGRATION_RETRY_NOT_ALLOWED");
        }

        Attempts++;
        LastAttemptAt = now;
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];

    public static class Operations
    {
        public const string PushWarehouseReceipt = "PushWarehouseReceipt";
        public const string GetContracts = "GetContracts";
        public const string GetProposalSummaries = "GetProposalSummaries";
        public const string GetItems = "GetItems";
        public const string PullWarehouseReceipts = "PullWarehouseReceipts";
        public const string SimulateReceipt = "SimulateReceipt";
    }
}
