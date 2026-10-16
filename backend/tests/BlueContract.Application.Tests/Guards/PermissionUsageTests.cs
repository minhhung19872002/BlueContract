using System.Reflection;
using BlueContract.Domain.Identity;
using Shouldly;

namespace BlueContract.Application.Tests.Guards;

/// <summary>
/// AUDIT-1 §10.3: every permission in the matrix must be checked somewhere in the backend — a permission that is only
/// seeded is a switch that does nothing when an administrator turns it off.
/// </summary>
public class PermissionUsageTests
{
    [Fact]
    public void Every_permission_is_read_by_the_backend()
    {
        var files = SourceScan.ProductionFiles("PermissionCatalog.cs", "RoleCatalog.cs");
        var constants = typeof(Permissions).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (Name: f.Name, Code: (string)f.GetRawConstantValue()!))
            .ToList();
        constants.ShouldNotBeEmpty();

        var dead = constants.Where(c => !files.Any(f =>
                f.Text.Contains($"Permissions.{c.Name}", StringComparison.Ordinal)
                || f.Text.Contains($"P.{c.Name}", StringComparison.Ordinal)
                || f.Text.Contains($"\"{c.Code}\"", StringComparison.Ordinal)))
            .Select(c => c.Code)
            .ToList();
        dead.ShouldBeEmpty("permissions declared but never checked: " + string.Join(", ", dead));
    }

    [Fact]
    public void Every_permission_is_in_the_catalog_shown_to_administrators()
    {
        var codes = typeof(Permissions).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!);
        codes.ShouldAllBe(c => PermissionCatalog.Exists(c));
    }
}
