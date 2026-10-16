using BlueProcure.Domain.Identity;

namespace BlueProcure.Application.Common;

/// <summary>Who is calling, resolved from the access token. Background jobs see an unauthenticated (system) user.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    string? UserName { get; }

    string? FullName { get; }

    UserType? UserType { get; }

    /// <summary>Set only for supplier users; drives the supplier isolation query filter.</summary>
    Guid? SupplierId { get; }

    bool IsSupplier => UserType == Domain.Identity.UserType.Supplier;

    IReadOnlySet<string> Permissions { get; }

    IReadOnlyList<(DataScopeType Type, string Value)> DataScopes { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }

    string? CorrelationId { get; }

    bool HasPermission(string permission) => Permissions.Contains(permission);
}
