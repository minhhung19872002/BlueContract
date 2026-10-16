using BlueContract.Domain.Common;
using BlueContract.Domain.Settings;
using Shouldly;

namespace BlueContract.Domain.Tests.Settings;

public class SystemSettingTests
{
    [Theory]
    [InlineData(SettingValueType.WholeNumber, "15")]
    [InlineData(SettingValueType.DecimalNumber, "1000.5")]
    [InlineData(SettingValueType.YesNo, "true")]
    [InlineData(SettingValueType.Text, "bất kỳ")]
    public void Accepts_values_matching_type(SettingValueType type, string value)
    {
        var setting = new SystemSetting("key", value, type, "d");

        setting.Value.ShouldBe(value);
    }

    [Theory]
    [InlineData(SettingValueType.WholeNumber, "1.5")]
    [InlineData(SettingValueType.WholeNumber, "abc")]
    [InlineData(SettingValueType.DecimalNumber, "1,000.5x")]
    [InlineData(SettingValueType.YesNo, "có")]
    public void Rejects_values_not_matching_type(SettingValueType type, string value)
    {
        var ex = Should.Throw<DomainException>(() => new SystemSetting("key", value, type, "d"));

        ex.Code.ShouldBe("SETTING_VALUE_INVALID");
    }

    [Fact]
    public void Rejects_blank_key()
    {
        var ex = Should.Throw<DomainException>(() => new SystemSetting("  ", "1", SettingValueType.Text, "d"));

        ex.Code.ShouldBe("SETTING_KEY_REQUIRED");
    }

    [Fact]
    public void UpdateValue_validates_against_original_type()
    {
        var setting = new SystemSetting("key", "1", SettingValueType.WholeNumber, "d");

        setting.UpdateValue("30");
        setting.Value.ShouldBe("30");
        Should.Throw<DomainException>(() => setting.UpdateValue("ba mươi"));
        setting.Value.ShouldBe("30");
    }

    [Fact]
    public void Defaults_have_unique_keys_and_valid_values()
    {
        var defaults = SystemSettingKeys.Defaults();

        defaults.ShouldNotBeEmpty();
        defaults.Select(d => d.Key).Distinct().Count().ShouldBe(defaults.Count);
        defaults.ShouldAllBe(d => !string.IsNullOrWhiteSpace(d.Description));
    }
}
