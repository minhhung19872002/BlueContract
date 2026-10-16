using System.Globalization;
using BlueContract.Domain.Common;

namespace BlueContract.Domain.Settings;

/// <summary>
/// Runtime-tunable threshold or option (spec §11), editable from the admin UI.
/// </summary>
public sealed class SystemSetting : Entity
{
    private SystemSetting()
    {
        Key = string.Empty;
        Value = string.Empty;
        Description = string.Empty;
    }

    public SystemSetting(string key, string value, SettingValueType valueType, string description)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new DomainException("SETTING_KEY_REQUIRED");
        }

        Key = key.Trim();
        ValueType = valueType;
        Description = description;
        Value = string.Empty;
        UpdateValue(value);
    }

    public string Key { get; private set; }

    public string Value { get; private set; }

    public SettingValueType ValueType { get; private set; }

    public string Description { get; private set; }

    /// <summary>Descriptions follow the code (seeding refreshes them); values stay as the admin set them.</summary>
    public void RefreshDescription(string description) => Description = description;

    public void UpdateValue(string value)
    {
        var ok = ValueType switch
        {
            SettingValueType.WholeNumber => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            SettingValueType.DecimalNumber => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
            SettingValueType.YesNo => bool.TryParse(value, out _),
            _ => value is not null,
        };

        if (!ok)
        {
            throw new DomainException("SETTING_VALUE_INVALID");
        }

        Value = value!;
    }
}

public enum SettingValueType
{
    Text = 0,
    WholeNumber = 1,
    DecimalNumber = 2,
    YesNo = 3,
}
