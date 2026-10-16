using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using BlueContract.Application.Common;
using BlueContract.Application.Integration;
using BlueContract.Domain.Integration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BlueContract.Api.Integration;

public sealed class HisInboundClient
{
    public string Name { get; set; } = "HIS";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Shared secret of the HMAC-SHA256 signature (never sent on the wire).</summary>
    public string Secret { get; set; } = string.Empty;
}

/// <summary>Inbound HIS API settings (spec §7.2) — section "HisInbound", secrets from environment variables.</summary>
public sealed class HisInboundOptions
{
    public const string SectionName = "HisInbound";

    public List<HisInboundClient> Clients { get; set; } = [];

    /// <summary>IP addresses or CIDR ranges allowed to call (empty = any; set it in production).</summary>
    public List<string> AllowedIps { get; set; } = [];

    public int RateLimitPerMinute { get; set; } = 120;

    public int MaxBodyBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Enables POST /api/dev/his/simulate-receipt (demo / test environments only).</summary>
    public bool DevEndpoints { get; set; }

    public bool Enabled => Clients.Any(c => !string.IsNullOrWhiteSpace(c.ApiKey) && !string.IsNullOrWhiteSpace(c.Secret));
}

/// <summary>
/// Authenticates HIS calls: IP whitelist → X-Api-Key → X-Timestamp ±5 min → X-Signature (HMAC-SHA256 over method, path,
/// timestamp and body hash) → anti-replay cache. Every call — accepted or rejected — is written to IntegrationLog with its
/// request and response.
/// </summary>
internal sealed class HisInboundFilter(
    IOptions<HisInboundOptions> options,
    IMemoryCache cache,
    IntegrationLogWriter logs,
    IClock clock,
    ILogger<HisInboundFilter> logger) : IAsyncResourceFilter
{
    public const string ClientItem = "his.client";
    public const string ReceiptItem = "his.receipt";
    public const string OutcomeItem = "his.outcome";
    private const int MaxLoggedResponse = 20_000;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var o = options.Value;
        var watch = Stopwatch.StartNew();
        var pathAndQuery = http.Request.PathBase + http.Request.Path + http.Request.QueryString;
        var operation = Operation(http.Request.Method, http.Request.Path);
        var ip = http.Connection.RemoteIpAddress;

        http.Request.EnableBuffering();
        byte[] body;
        using (var buffer = new MemoryStream())
        {
            await http.Request.Body.CopyToAsync(buffer, http.RequestAborted);
            body = buffer.ToArray();
            http.Request.Body.Position = 0;
        }

        var bodyText = body.Length == 0 ? null : Encoding.UTF8.GetString(body);
        var externalId = ExternalId(bodyText);
        var apiKey = http.Request.Headers["X-Api-Key"].ToString();
        var client = o.Clients.FirstOrDefault(c => !string.IsNullOrEmpty(c.ApiKey) && string.Equals(c.ApiKey, apiKey, StringComparison.Ordinal));

        var rejection = !o.Enabled ? ("HIS_INBOUND_DISABLED", StatusCodes.Status503ServiceUnavailable)
            : !IsAllowed(ip, o.AllowedIps) ? ("HIS_AUTH_IP_NOT_ALLOWED", StatusCodes.Status403Forbidden)
            : body.Length > o.MaxBodyBytes ? ("FILE_TOO_LARGE", StatusCodes.Status413PayloadTooLarge)
            : client is null ? ("HIS_AUTH_API_KEY_INVALID", StatusCodes.Status401Unauthorized)
            : !HisRequestSignature.IsFresh(http.Request.Headers["X-Timestamp"], clock.UtcNow) ? ("HIS_AUTH_TIMESTAMP_INVALID", StatusCodes.Status401Unauthorized)
            : !HisRequestSignature.Verify(client.Secret, http.Request.Method, pathAndQuery, http.Request.Headers["X-Timestamp"].ToString(), body,
                http.Request.Headers["X-Signature"]) ? ("HIS_AUTH_SIGNATURE_INVALID", StatusCodes.Status401Unauthorized)
            : !HttpMethods.IsGet(http.Request.Method) && !RegisterSignature(http.Request.Headers["X-Signature"].ToString()) ? ("HIS_AUTH_REPLAY", StatusCodes.Status401Unauthorized)
            : ((string Code, int Status)?)null;

        var logId = await logs.BeginAsync(IntegrationDirection.Inbound, operation, http.Request.Method, pathAndQuery, externalId, bodyText, ip?.ToString(),
            client?.Name, http.RequestAborted);
        if (rejection is { } r)
        {
            logger.LogWarning("HIS inbound call rejected: {Code} from {Ip}", r.Code, ip);
            var problem = Problem(http, r.Status, r.Code);
            context.Result = new ObjectResult(problem) { StatusCode = r.Status, ContentTypes = { "application/problem+json" } };
            await logs.CompleteAsync(logId, IntegrationLogStatus.Rejected, r.Status, JsonSerializer.Serialize(problem), r.Code, ErrorMessages.Get(r.Code),
                watch.ElapsedMilliseconds, null, CancellationToken.None);
            return;
        }

        http.Items[ClientItem] = client!.Name;
        var originalBody = http.Response.Body;
        await using var captured = new MemoryStream();
        http.Response.Body = captured;
        IntegrationLogStatus status;
        string? errorCode = null;
        try
        {
            var executed = await next();
            if (executed.Exception is { } failure && !executed.ExceptionHandled)
            {
                (executed.ExceptionDispatchInfo ?? System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure)).Throw();
            }

            status = http.Response.StatusCode < 300
                ? (http.Items[OutcomeItem] is HisIngestOutcome.Duplicate ? IntegrationLogStatus.Duplicate : IntegrationLogStatus.Success)
                : IntegrationLogStatus.Failed;
        }
        catch (Exception ex)
        {
            http.Response.Body = originalBody;
            var code = ex is Domain.Common.DomainException domain ? domain.Code : "INTERNAL_ERROR";
            await logs.CompleteAsync(logId, IntegrationLogStatus.Failed, ex is Domain.Common.DomainException ? 400 : 500, null, code, ErrorMessages.Get(code),
                watch.ElapsedMilliseconds, null, CancellationToken.None);
            throw;
        }

        http.Response.Body = originalBody;
        captured.Position = 0;
        var responseText = Encoding.UTF8.GetString(captured.ToArray());
        await captured.CopyToAsync(originalBody, http.RequestAborted);
        if (status == IntegrationLogStatus.Failed)
        {
            errorCode = ProblemCode(responseText);
        }

        await logs.CompleteAsync(logId, status, http.Response.StatusCode, responseText.Length > MaxLoggedResponse ? responseText[..MaxLoggedResponse] + "…" : responseText,
            errorCode, errorCode is null ? null : ErrorMessages.Get(errorCode), watch.ElapsedMilliseconds, http.Items[ReceiptItem] as Guid?, CancellationToken.None);
    }

    internal static bool IsAllowed(IPAddress? ip, IReadOnlyCollection<string> allowed)
    {
        var entries = allowed.SelectMany(a => a.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();
        if (entries.Count == 0)
        {
            return true;
        }

        if (ip is null)
        {
            return false;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        foreach (var entry in entries)
        {
            if (entry.Contains('/', StringComparison.Ordinal) ? IPNetwork.TryParse(entry, out var network) && network.Contains(ip)
                : IPAddress.TryParse(entry, out var single) && single.Equals(ip))
            {
                return true;
            }
        }

        return false;
    }

    private static string Operation(string method, PathString path)
    {
        var last = path.Value?.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;
        return (method, last) switch
        {
            ("POST", "warehouse-receipts") => IntegrationLog.Operations.PushWarehouseReceipt,
            (_, "contracts") => IntegrationLog.Operations.GetContracts,
            (_, "proposal-summaries") => IntegrationLog.Operations.GetProposalSummaries,
            (_, "items") => IntegrationLog.Operations.GetItems,
            _ => method + " " + last,
        };
    }

    private static string? ExternalId(string? body)
    {
        if (body is null)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("hisReceiptId", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ProblemCode(string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : "VALIDATION_FAILED";
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A write request's signature is accepted once within the freshness window (anti-replay); reads are idempotent.</summary>
    private bool RegisterSignature(string signature)
    {
        var key = "his-sig:" + signature.Trim().ToLowerInvariant();
        if (cache.TryGetValue(key, out _))
        {
            return false;
        }

        cache.Set(key, true, HisRequestSignature.AllowedSkew * 2);
        return true;
    }

    private static ProblemDetails Problem(HttpContext http, int status, string code)
    {
        var problem = new ProblemDetails { Status = status, Title = ErrorMessages.Get(code), Type = $"https://blueprocure.local/errors/{code}" };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = http.TraceIdentifier;
        return problem;
    }
}
