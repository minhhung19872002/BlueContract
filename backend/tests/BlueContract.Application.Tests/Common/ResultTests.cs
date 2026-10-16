using BlueContract.Application.Common;
using Shouldly;

namespace BlueContract.Application.Tests.Common;

public class ResultTests
{
    [Fact]
    public void Success_exposes_value()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Failure_exposes_error_and_blocks_value()
    {
        Result<int> result = Error.NotFound();

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Kind.ShouldBe(ErrorKind.NotFound);
        Should.Throw<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Non_generic_result_factories()
    {
        Result.Success().IsSuccess.ShouldBeTrue();
        Result.Failure(Error.Conflict()).Error!.Code.ShouldBe("CONFLICT");
    }
}
