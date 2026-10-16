using System.Text.Json;
using BlueProcure.Application.Common;
using BlueProcure.Domain.Common;
using BlueProcure.Domain.Identity;
using BlueProcure.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BlueProcure.Infrastructure.Persistence;

/// <summary>
/// One place for cross-cutting persistence concerns, in this order:
/// 1. stamp CreatedAt/By, UpdatedAt/By (UTC) and turn hard deletes into soft deletes;
/// 2. write AuditLog rows (before/after JSON) for every Entity change;
/// 3. append a versioned snapshot for master data marked <see cref="IHasHistory"/>.
/// </summary>
internal sealed class AuditableEntityInterceptor(IClock clock, ICurrentUser currentUser) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> SensitiveProperties = new(StringComparer.Ordinal)
    {
        nameof(User.PasswordHash),
        nameof(User.SecurityStamp),
        nameof(User.TwoFactorSecret),
    };

    private static readonly HashSet<string> IgnoredProperties = new(StringComparer.Ordinal)
    {
        nameof(Entity.RowVersion),
        nameof(Entity.UpdatedAt),
        nameof(Entity.UpdatedBy),
    };

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            await ProcessAsync(eventData.Context, cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            ProcessAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        }

        return base.SavingChanges(eventData, result);
    }

    private async Task ProcessAsync(DbContext context, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var userId = currentUser.UserId;
        var entries = context.ChangeTracker.Entries<Entity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        var audits = new List<AuditLog>();
        var histories = new List<(EntityEntry<Entity> Entry, HistoryChangeType Type)>();

        foreach (var entry in entries)
        {
            string action;
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy ??= userId;
                    action = AuditActions.Create;
                    break;
                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Entity.MarkDeleted();
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userId;
                    action = AuditActions.Delete;
                    break;
                default:
                    if (!entry.Properties.Any(p => p.IsModified && !IgnoredProperties.Contains(p.Metadata.Name)))
                    {
                        continue;
                    }

                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userId;
                    action = entry.Property(nameof(Entity.IsDeleted)).IsModified && entry.Entity.IsDeleted
                        ? AuditActions.Delete
                        : AuditActions.Update;
                    break;
            }

            audits.Add(BuildAudit(entry, action, now));
            if (entry.Entity is IHasHistory)
            {
                histories.Add((entry, action switch
                {
                    AuditActions.Create => HistoryChangeType.Created,
                    AuditActions.Delete => HistoryChangeType.Deleted,
                    _ => HistoryChangeType.Updated,
                }));
            }
        }

        if (audits.Count > 0)
        {
            context.Set<AuditLog>().AddRange(audits);
        }

        foreach (var (entry, type) in histories)
        {
            await AddHistoryAsync(context, entry, type, now, ct);
        }
    }

    private AuditLog BuildAudit(EntityEntry<Entity> entry, string action, DateTimeOffset now)
    {
        Dictionary<string, object?>? oldValues = null;
        Dictionary<string, object?>? newValues = new();

        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (IgnoredProperties.Contains(name))
            {
                continue;
            }

            var sensitive = SensitiveProperties.Contains(name);
            if (entry.State == EntityState.Added)
            {
                newValues[name] = sensitive ? "***" : property.CurrentValue;
            }
            else if (property.IsModified)
            {
                oldValues ??= new();
                oldValues[name] = sensitive ? "***" : property.OriginalValue;
                newValues[name] = sensitive ? "***" : property.CurrentValue;
            }
        }

        return new AuditLog(action, entry.Metadata.ClrType.Name, entry.Entity.Id.ToString())
        {
            OccurredAt = now,
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            IpAddress = currentUser.IpAddress,
            UserAgent = Truncate(currentUser.UserAgent, 500),
            CorrelationId = currentUser.CorrelationId,
            OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues, Json),
            NewValues = newValues.Count == 0 ? null : JsonSerializer.Serialize(newValues, Json),
        };
    }

    private async Task AddHistoryAsync(DbContext context, EntityEntry<Entity> entry, HistoryChangeType type, DateTimeOffset now, CancellationToken ct)
    {
        var snapshot = entry.Properties
            .Where(p => !SensitiveProperties.Contains(p.Metadata.Name) && p.Metadata.Name != nameof(Entity.RowVersion))
            .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
        var json = JsonSerializer.Serialize(snapshot, Json);
        var id = entry.Entity.Id;

        HistoryRecord record = entry.Entity switch
        {
            Department => new DepartmentHistory(),
            User => new UserHistory(),
            Supplier => new SupplierHistory(),
            _ => throw new InvalidOperationException($"No history table for {entry.Metadata.ClrType.Name}"),
        };

        // Version = count of persisted + pending rows for this entity + 1.
        var persisted = record switch
        {
            DepartmentHistory => await context.Set<DepartmentHistory>().CountAsync(h => h.EntityId == id, ct),
            UserHistory => await context.Set<UserHistory>().CountAsync(h => h.EntityId == id, ct),
            _ => await context.Set<SupplierHistory>().CountAsync(h => h.EntityId == id, ct),
        };
        var pending = context.ChangeTracker.Entries<HistoryRecord>().Count(h => h.State == EntityState.Added && h.Entity.EntityId == id);

        record.EntityId = id;
        record.Version = persisted + pending + 1;
        record.ChangeType = type;
        record.ChangedAt = now;
        record.ChangedBy = currentUser.UserId;
        record.ChangedByName = currentUser.FullName ?? currentUser.UserName ?? "Hệ thống";
        record.Snapshot = json;
        context.Add(record);
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
