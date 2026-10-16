using BlueContract.Domain.Common;

namespace BlueContract.Domain.Settings;

/// <summary>
/// Server option set on the admin screen (SMTP, signing), keyed like the configuration (<c>Email:Host</c>); it wins over
/// appsettings / environment. Secrets are stored encrypted (Data Protection).
/// </summary>
public sealed class ConfigOverride : Entity
{
    private ConfigOverride()
    {
        Key = string.Empty;
        Value = string.Empty;
    }

    public ConfigOverride(string key, string value, bool isSecret)
    {
        Key = Guard.Required(key, "CONFIG_KEY_REQUIRED");
        Value = value;
        IsSecret = isSecret;
    }

    public string Key { get; private set; }

    /// <summary>Plain value, or the protected payload when <see cref="IsSecret"/>.</summary>
    public string Value { get; private set; }

    public bool IsSecret { get; private set; }

    public void Update(string value) => Value = value;
}
