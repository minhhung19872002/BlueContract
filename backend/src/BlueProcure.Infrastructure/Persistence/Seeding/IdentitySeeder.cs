using BlueProcure.Application.Auth;
using BlueProcure.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlueProcure.Infrastructure.Persistence.Seeding;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Seed the demo hospital, suppliers and one account per role (spec §12). Off in production.</summary>
    public bool Demo { get; set; }

    /// <summary>Password for all demo accounts.</summary>
    public string DemoPassword { get; set; } = "Demo@123";

    /// <summary>Initial password for the bootstrap "admin" account when no users exist (non-demo installs).</summary>
    public string? AdminPassword { get; set; }

    /// <summary>Allows `--reset-demo` (wipes business data). Never enable in production.</summary>
    public bool AllowReset { get; set; }
}

/// <summary>Creates missing system roles (with default permissions) and the bootstrap admin. Idempotent.</summary>
internal sealed class IdentitySeeder(AppDbContext db, IPasswordService passwords, IOptions<SeedOptions> options, ILogger<IdentitySeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        var existing = await db.Roles.IgnoreQueryFilters().Select(r => r.Code).ToListAsync(ct);
        var created = 0;
        foreach (var definition in RoleCatalog.All.Where(d => !existing.Contains(d.Code)))
        {
            var role = new Role(definition.Code, definition.Name, definition.Description, isSystem: true);
            role.SetPermissions(definition.Permissions);
            db.Roles.Add(role);
            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Count} system roles", created);
        }

        await EnsureBootstrapAdminAsync(ct);
    }

    private async Task EnsureBootstrapAdminAsync(CancellationToken ct)
    {
        var o = options.Value;

        // Demo installs get a full set of accounts (including "admin") from DemoSeeder.
        if (o.Demo || await db.Users.IgnoreQueryFilters().AnyAsync(ct))
        {
            return;
        }

        var password = o.Demo ? o.DemoPassword : o.AdminPassword;
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("No users exist and Seed:AdminPassword is not set — no administrator account was created.");
            return;
        }

        var roleId = await db.Roles.Where(r => r.Code == RoleCatalog.SysAdmin).Select(r => r.Id).FirstAsync(ct);
        var admin = new User("admin", "Quản trị hệ thống", null, UserType.Internal, null);
        admin.UpdateProfile("Quản trị hệ thống", null, null, "Quản trị hệ thống", null, null);
        admin.SetPasswordHash(passwords.Hash(admin, password), mustChange: !o.Demo);
        admin.SetRoles([roleId]);
        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created bootstrap administrator account 'admin'");
    }
}
