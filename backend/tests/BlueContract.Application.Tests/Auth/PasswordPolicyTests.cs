using BlueContract.Application.Auth;
using BlueContract.Application.Common;
using Shouldly;

namespace BlueContract.Application.Tests.Auth;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("Demo@123", true)]
    [InlineData("demo@123", false)]
    [InlineData("DEMO@123", false)]
    [InlineData("Demo1234", false)]
    [InlineData("Demo@abc", false)]
    [InlineData("De@1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Strength_rules(string? password, bool expected) => PasswordPolicy.IsStrong(password).ShouldBe(expected);

    [Fact]
    public void Generated_temporary_passwords_always_satisfy_policy_and_differ()
    {
        var generated = Enumerable.Range(0, 200).Select(_ => PasswordPolicy.GenerateTemporary()).ToList();

        generated.ShouldAllBe(p => PasswordPolicy.IsStrong(p));
        generated.Distinct().Count().ShouldBe(generated.Count);
    }
}

public class PagingTests
{
    [Theory]
    [InlineData(0, 20)]
    [InlineData(50, 50)]
    [InlineData(5000, PageQuery.MaxPageSize)]
    public void Page_size_is_clamped(int requested, int expected) =>
        new PageQuery { PageSize = requested }.SafePageSize.ShouldBe(expected);

    [Fact]
    public void Export_lifts_cap_up_to_export_limit()
    {
        new PageQuery { PageSize = 5000, ForExport = true }.SafePageSize.ShouldBe(5000);
        new PageQuery { PageSize = 50_000, ForExport = true }.SafePageSize.ShouldBe(PageQuery.MaxExportRows);
    }

    [Fact]
    public void Like_pattern_escapes_wildcards() => PagingExtensions.LikePattern("50%_a").ShouldBe("%50\\%\\_a%");
}
