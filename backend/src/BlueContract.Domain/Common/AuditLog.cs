namespace BlueContract.Domain.Common;

/// <summary>
/// Append-only audit trail (spec §5.7). UPDATE/DELETE are rejected by a database trigger.
/// </summary>
public sealed class AuditLog
{
    private AuditLog()
    {
        Action = string.Empty;
        EntityType = string.Empty;
    }

    public AuditLog(string action, string entityType, string? entityId)
    {
        Id = Guid.CreateVersion7();
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset OccurredAt { get; set; }

    public Guid? UserId { get; set; }

    public string? UserName { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? CorrelationId { get; set; }

    /// <summary>Create / Update / Delete for data changes, or a business verb (Login, Sign, Return, Download...).</summary>
    public string Action { get; private set; }

    public string EntityType { get; private set; }

    public string? EntityId { get; private set; }

    /// <summary>JSON before the change (null for creates / business actions).</summary>
    public string? OldValues { get; set; }

    /// <summary>JSON after the change or business action details.</summary>
    public string? NewValues { get; set; }

    public string? Note { get; set; }
}

public static class AuditActions
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";
    public const string Login = "Login";
    public const string LoginFailed = "LoginFailed";
    public const string Logout = "Logout";
    public const string LockedOut = "LockedOut";
    public const string PasswordChanged = "PasswordChanged";
    public const string PasswordReset = "PasswordReset";
    public const string TwoFactorEnabled = "TwoFactorEnabled";
    public const string TwoFactorDisabled = "TwoFactorDisabled";
    public const string RefreshTokenReuse = "RefreshTokenReuse";
}
