using System.Data.Common;
using System.Security.Cryptography;
using BlueContract.Application.Auth;
using BlueContract.Infrastructure.Messaging;
using BlueContract.Infrastructure.Persistence;
using BlueContract.Infrastructure.Signing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlueContract.Infrastructure.Configuration;

/// <summary>
/// SMTP and signing options in effect: appsettings / environment overlaid with the values saved on the admin screen
/// (<c>config_overrides</c>). Every reader of these options goes through here. Overrides are cached for a short time; a
/// save on this instance refreshes them at once, other API instances within <see cref="Lifetime"/>.
/// </summary>
public sealed class RuntimeSettings(IConfiguration configuration, IServiceScopeFactory scopes, ISecretProtector protector, ILogger<RuntimeSettings> logger)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private Snapshot? _snapshot;

    public EmailOptions Email() => Current().Email;

    public SigningOptions Signing() => Current().Signing;

    /// <summary>Saved override values (secrets decrypted), by configuration key.</summary>
    public IReadOnlyDictionary<string, string> Overrides() => Current().Overrides;

    public void Invalidate()
    {
        lock (_gate)
        {
            _snapshot = null;
        }
    }

    private Snapshot Current()
    {
        lock (_gate)
        {
            if (_snapshot is { } s && DateTimeOffset.UtcNow - s.LoadedAt < Lifetime)
            {
                return s;
            }
        }

        var overrides = Load();
        var merged = new ConfigurationBuilder().AddConfiguration(configuration).AddInMemoryCollection(overrides!).Build();
        var email = new EmailOptions();
        merged.GetSection(EmailOptions.SectionName).Bind(email);
        var signing = new SigningOptions();
        merged.GetSection(SigningOptions.SectionName).Bind(signing);
        var snapshot = new Snapshot(DateTimeOffset.UtcNow, overrides, email, signing);
        lock (_gate)
        {
            _snapshot = snapshot;
        }

        return snapshot;
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = db.ConfigOverrides.AsNoTracking().Select(o => new { o.Key, o.Value, o.IsSecret }).ToList();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                if (!row.IsSecret)
                {
                    values[row.Key] = row.Value;
                    continue;
                }

                try
                {
                    values[row.Key] = protector.Unprotect(row.Value);
                }
                catch (CryptographicException)
                {
                    logger.LogWarning("Saved secret {Key} cannot be decrypted (data protection keys changed); it is ignored", row.Key);
                }
            }

            return values;
        }
        catch (DbException e)
        {
            // Before the first migration the table does not exist yet: run on appsettings alone.
            logger.LogWarning(e, "Configuration overrides could not be read; using appsettings only");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed record Snapshot(DateTimeOffset LoadedAt, IReadOnlyDictionary<string, string> Overrides, EmailOptions Email, SigningOptions Signing);
}
