using BlueProcure.Application.Common;
using BlueProcure.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BlueProcure.Infrastructure.Persistence;

/// <summary>
/// First-start bootstrap: applies migrations, seeds defaults, ensures the storage bucket exists.
/// Idempotent — safe to run on every start.
/// </summary>
public sealed class DatabaseInitializer(AppDbContext db, IFileStorage storage, ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct)
    {
        await RetryAsync("database migration", () => db.Database.MigrateAsync(ct), ct);
        await SeedSettingsAsync(ct);
        await RetryAsync("storage bucket", () => storage.EnsureBucketAsync(ct), ct);
    }

    private async Task SeedSettingsAsync(CancellationToken ct)
    {
        var existing = await db.SystemSettings.IgnoreQueryFilters().Select(s => s.Key).ToListAsync(ct);
        var missing = SystemSettingKeys.Defaults().Where(s => !existing.Contains(s.Key)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        db.SystemSettings.AddRange(missing);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} system settings", missing.Count);
    }

    private async Task RetryAsync(string what, Func<Task> action, CancellationToken ct)
    {
        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await action();
                return;
            }
#pragma warning disable CA1031 // Retry on any transient startup failure (dependency not ready yet)
            catch (Exception ex) when (attempt < maxAttempts)
#pragma warning restore CA1031
            {
                logger.LogWarning(ex, "Initialization step '{Step}' failed (attempt {Attempt}/{Max}); retrying", what, attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(attempt * 2, 10)), ct);
            }
        }
    }
}
