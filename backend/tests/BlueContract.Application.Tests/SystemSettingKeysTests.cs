using System.Reflection;
using BlueContract.Domain.Settings;
using Shouldly;

namespace BlueContract.Application.Tests.Settings;

/// <summary>AUDIT-1 §11.13: every setting on the "Tham số hệ thống" screen is seeded, range-checked and read by the code.</summary>
public class SystemSettingKeysTests
{
    private static readonly List<(string Name, string Key)> Keys = [.. typeof(SystemSettingKeys)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (f.Name, (string)f.GetRawConstantValue()!))];

    [Fact]
    public void Every_key_has_a_default_and_numbers_have_a_range()
    {
        var defaults = SystemSettingKeys.Defaults();
        defaults.Select(d => d.Key).ShouldBe(Keys.Select(k => k.Key), ignoreOrder: true);
        foreach (var d in defaults.Where(d => d.ValueType is SettingValueType.WholeNumber or SettingValueType.DecimalNumber))
        {
            var range = SystemSettingKeys.RangeOf(d.Key);
            range.ShouldNotBeNull(d.Key);
            decimal.Parse(d.Value, System.Globalization.CultureInfo.InvariantCulture).ShouldBeInRange(range.Value.Min, range.Value.Max, d.Key);
        }
    }

    [Fact]
    public void Every_key_is_read_somewhere_in_the_code()
    {
        var src = SourceRoot();
        var code = string.Join('\n', Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && Path.GetFileName(f) != "SystemSettingKeys.cs")
            .Select(File.ReadAllText));
        var unread = Keys.Where(k => !code.Contains($"SystemSettingKeys.{k.Name}", StringComparison.Ordinal)).Select(k => k.Key).ToList();
        unread.ShouldBeEmpty("settings nothing reads: " + string.Join(", ", unread));
    }

    [Theory]
    [InlineData(60, "0 * * * *")]
    [InlineData(15, "*/15 * * * *")]
    public void Reminder_schedule_is_every_n_minutes(int minutes, string cron) => SystemSettingKeys.ReminderCron(minutes).ShouldBe(cron);

    /// <summary>backend/src, found from the test binaries' folder.</summary>
    private static string SourceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "BlueContract.Domain");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(dir.FullName, "src");
            }
        }

        throw new DirectoryNotFoundException("backend/src not found above " + AppContext.BaseDirectory);
    }
}
