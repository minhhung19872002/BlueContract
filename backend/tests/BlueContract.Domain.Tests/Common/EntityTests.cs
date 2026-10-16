using BlueContract.Domain.Settings;
using Shouldly;

namespace BlueContract.Domain.Tests.Common;

public class EntityTests
{
    [Fact]
    public void New_entity_gets_uuid_v7_id()
    {
        var setting = new SystemSetting("k", "v", SettingValueType.Text, "d");

        setting.Id.ShouldNotBe(Guid.Empty);
        setting.Id.Version.ShouldBe(7);
    }

    [Fact]
    public void Ids_are_time_ordered()
    {
        var first = new SystemSetting("a", "1", SettingValueType.Text, "d");
        Thread.Sleep(2);
        var second = new SystemSetting("b", "2", SettingValueType.Text, "d");

        string.CompareOrdinal(first.Id.ToString(), second.Id.ToString()).ShouldBeLessThan(0);
    }

    [Fact]
    public void MarkDeleted_and_Restore_toggle_soft_delete_flag()
    {
        var setting = new SystemSetting("k", "v", SettingValueType.Text, "d");

        setting.MarkDeleted();
        setting.IsDeleted.ShouldBeTrue();

        setting.Restore();
        setting.IsDeleted.ShouldBeFalse();
    }
}
