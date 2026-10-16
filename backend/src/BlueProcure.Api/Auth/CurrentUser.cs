using System.Security.Claims;
using BlueProcure.Application.Common;
using BlueProcure.Domain.Identity;
using BlueProcure.Infrastructure.Identity;

namespace BlueProcure.Api.Auth;

/// <summary>ICurrentUser backed by the validated JWT of the current HTTP request.</summary>
internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private IReadOnlySet<string>? _permissions;
    private IReadOnlyList<(DataScopeType Type, string Value)>? _scopes;

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => Guid.TryParse(Find(AppClaims.UserId), out var id) ? id : null;

    public string? UserName => Find(AppClaims.UserName);

    public string? FullName => Find(AppClaims.FullName);

    public UserType? UserType => Enum.TryParse<UserType>(Find(AppClaims.UserType), out var t) ? t : null;

    public Guid? SupplierId => Guid.TryParse(Find(AppClaims.SupplierId), out var id) ? id : null;

    public IReadOnlySet<string> Permissions => _permissions ??=
        (Principal?.FindAll(AppClaims.Permission).Select(c => c.Value) ?? []).ToHashSet(StringComparer.Ordinal);

    public IReadOnlyList<(DataScopeType Type, string Value)> DataScopes => _scopes ??= ParseScopes();

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString();

    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;

    private string? Find(string type) => IsAuthenticated ? Principal!.FindFirst(type)?.Value : null;

    private List<(DataScopeType, string)> ParseScopes()
    {
        var result = new List<(DataScopeType, string)>();
        foreach (var claim in Principal?.FindAll(AppClaims.Scope) ?? [])
        {
            var parts = claim.Value.Split(':', 2);
            if (parts.Length == 2 && Enum.TryParse<DataScopeType>(parts[0], out var type))
            {
                result.Add((type, parts[1]));
            }
        }

        return result;
    }
}
