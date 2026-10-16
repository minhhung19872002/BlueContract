using BlueProcure.Application.Common;
using Shouldly;

namespace BlueProcure.Application.Tests.Common;

public class ErrorMessagesTests
{
    [Theory]
    [InlineData("NOT_FOUND")]
    [InlineData("FORBIDDEN")]
    [InlineData("CONFLICT")]
    [InlineData("INTERNAL_ERROR")]
    [InlineData("SETTING_VALUE_INVALID")]
    public void Known_codes_have_vietnamese_messages(string code)
    {
        ErrorMessages.IsKnown(code).ShouldBeTrue();
        ErrorMessages.Get(code).ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Unknown_code_falls_back_to_generic_business_error()
    {
        ErrorMessages.Get("SOMETHING_NEW").ShouldBe(ErrorMessages.Get("DOMAIN_ERROR"));
    }
}
