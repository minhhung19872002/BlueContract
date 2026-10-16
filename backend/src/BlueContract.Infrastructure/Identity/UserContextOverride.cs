using BlueContract.Application.Common;
using BlueContract.Domain.Identity;
using BlueContract.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Infrastructure.Identity;

/// <summary>
/// Runs code "as" a given user outside an HTTP request (demo scenario seeding, background jobs that act for a user).
/// Services resolved from a scope created while the override is active see this user as <see cref="ICurrentUser"/>.
/// </summary>
public static class UserContextOverride
{
    private static readonly AsyncLocal<ICurrentUser?> Holder = new();

    public static ICurrentUser? Current => Holder.Value;

    public static IDisposable Use(ICurrentUser user)
    {
        var previous = Holder.Value;
        Holder.Value = user;
        return new Restore(previous);
    }

    private sealed class Restore(ICurrentUser? previous) : IDisposable
    {
        public void Dispose() => Holder.Value = previous;
    }
}

/// <summary>A fully-resolved user identity (permissions and data scopes loaded from the database).</summary>
public sealed class ImpersonatedUser : ICurrentUser
{
    private ImpersonatedUser(User user, IReadOnlySet<string> permissions, IReadOnlyList<(DataScopeType, string)> scopes)
    {
        UserId = user.Id;
        UserName = user.UserName;
        FullName = user.FullName;
        UserType = user.UserType;
        SupplierId = user.SupplierId;
        Permissions = permissions;
        DataScopes = scopes;
    }

    public bool IsAuthenticated => true;

    public Guid? UserId { get; }

    public string? UserName { get; }

    public string? FullName { get; }

    public UserType? UserType { get; }

    public Guid? SupplierId { get; }

    public IReadOnlySet<string> Permissions { get; }

    public IReadOnlyList<(DataScopeType Type, string Value)> DataScopes { get; }

    public string? IpAddress => null;

    public string? UserAgent => "system";

    public string? CorrelationId => null;

    public static async Task<ImpersonatedUser> LoadAsync(AppDbContext db, string userName, CancellationToken ct)
    {
        var user = await db.Users.IgnoreQueryFilters().Include(u => u.DataScopes).FirstAsync(u => u.UserName == userName, ct);
        var permissions = await db.UserRoles.IgnoreQueryFilters().Where(ur => ur.UserId == user.Id)
            .Join(db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (_, rp) => rp.PermissionCode)
            .Distinct().ToListAsync(ct);
        return new ImpersonatedUser(user, permissions.ToHashSet(StringComparer.Ordinal), [.. user.DataScopes.Select(s => (s.ScopeType, s.Value))]);
    }
}
