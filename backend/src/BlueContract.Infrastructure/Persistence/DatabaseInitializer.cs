using BlueContract.Application.Common;
using BlueContract.Domain.Settings;
using BlueContract.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlueContract.Infrastructure.Persistence;

/// <summary>
/// First-start bootstrap: applies migrations, seeds defaults/roles/demo data, ensures the storage bucket exists.
/// Idempotent — safe to run on every start.
/// </summary>
public sealed class DatabaseInitializer(
    AppDbContext db,
    IFileStorage storage,
    IServiceProvider services,
    IOptions<SeedOptions> seedOptions,
    ILogger<DatabaseInitializer> logger)
{
    // Tables never wiped by --reset-demo.
    private static readonly HashSet<string> PreservedTables = new(StringComparer.Ordinal)
    {
        "__ef_migrations_history",
        "system_settings",
        "data_protection_keys",
    };

    public async Task InitializeAsync(CancellationToken ct, bool resetDemo = false)
    {
        await RetryAsync("database migration", () => db.Database.MigrateAsync(ct), ct);

        if (resetDemo)
        {
            await ResetDemoAsync(ct);
        }

        // Storage first: template seeding uploads files.
        await RetryAsync("storage bucket", () => storage.EnsureBucketAsync(ct), ct);
        await SeedSettingsAsync(ct);
        await services.GetRequiredService<IdentitySeeder>().SeedAsync(ct);
        if (seedOptions.Value.Demo)
        {
            await services.GetRequiredService<DemoSeeder>().SeedAsync(ct);
        }

        await services.GetRequiredService<TemplateSeeder>().SeedAsync(ct);
        await services.GetRequiredService<WorkflowSeeder>().SeedAsync(ct);
    }

    private async Task ResetDemoAsync(CancellationToken ct)
    {
        if (!seedOptions.Value.AllowReset)
        {
            throw new InvalidOperationException("--reset-demo is disabled. Set Seed:AllowReset=true (development only).");
        }

        var tables = await db.Database
            .SqlQueryRaw<string>("SELECT tablename AS \"Value\" FROM pg_tables WHERE schemaname = 'public'")
            .ToListAsync(ct);
        var targets = tables.Where(t => !PreservedTables.Contains(t) && !t.StartsWith("hangfire", StringComparison.Ordinal)).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        // TRUNCATE bypasses the row-level append-only trigger on audit_logs by design (dev reset only).
        var sql = "TRUNCATE TABLE " + string.Join(", ", targets.Select(t => "\"" + t.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"")) + " CASCADE";
#pragma warning disable EF1002 // Table names come from pg_tables, not user input.
        await db.Database.ExecuteSqlRawAsync(sql, ct);
#pragma warning restore EF1002
        db.ChangeTracker.Clear();
        logger.LogWarning("Demo data reset: truncated {Count} tables", targets.Count);
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
