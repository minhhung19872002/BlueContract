using System.Text.Json;
using BlueContract.Application.Common;
using BlueContract.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Infrastructure.Persistence;

internal sealed class AuditLogger(AppDbContext db, IDbContextFactory<AppDbContext> factory, ICurrentUser currentUser, IClock clock) : IAuditLogger
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Log(string action, string entityType, string? entityId, object? details = null, string? note = null) =>
        db.AuditLogs.Add(Build(action, entityType, entityId, details, note));

    public async Task LogNowAsync(string action, string entityType, string? entityId, object? details, string? note, CancellationToken ct)
    {
        await using var isolated = await factory.CreateDbContextAsync(ct);
        isolated.AuditLogs.Add(Build(action, entityType, entityId, details, note));
        await isolated.SaveChangesAsync(ct);
    }

    private AuditLog Build(string action, string entityType, string? entityId, object? details, string? note) =>
        new(action, entityType, entityId)
        {
            OccurredAt = clock.UtcNow,
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            IpAddress = currentUser.IpAddress,
            UserAgent = currentUser.UserAgent is { Length: > 500 } ua ? ua[..500] : currentUser.UserAgent,
            CorrelationId = currentUser.CorrelationId,
            NewValues = details is null ? null : JsonSerializer.Serialize(details, Json),
            Note = note,
        };
}
