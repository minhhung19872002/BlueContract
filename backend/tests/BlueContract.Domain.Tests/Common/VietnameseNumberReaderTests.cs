using BlueContract.Domain.Common;
using Shouldly;

namespace BlueContract.Domain.Tests.Common;

public class VietnameseNumberReaderTests
{
    [Theory]
    [InlineData(0, "Không đồng")]
    [InlineData(1, "Một đồng")]
    [InlineData(5, "Năm đồng")]
    [InlineData(10, "Mười đồng")]
    [InlineData(11, "Mười một đồng")]
    [InlineData(14, "Mười bốn đồng")]
    [InlineData(15, "Mười lăm đồng")]
    [InlineData(20, "Hai mươi đồng")]
    [InlineData(21, "Hai mươi mốt đồng")]
    [InlineData(24, "Hai mươi tư đồng")]
    [InlineData(25, "Hai mươi lăm đồng")]
    [InlineData(55, "Năm mươi lăm đồng")]
    [InlineData(100, "Một trăm đồng")]
    [InlineData(101, "Một trăm linh một đồng")]
    [InlineData(105, "Một trăm linh năm đồng")]
    [InlineData(110, "Một trăm mười đồng")]
    [InlineData(115, "Một trăm mười lăm đồng")]
    [InlineData(1000, "Một nghìn đồng")]
    [InlineData(1001, "Một nghìn không trăm linh một đồng")]
    [InlineData(1010, "Một nghìn không trăm mười đồng")]
    [InlineData(1100, "Một nghìn một trăm đồng")]
    [InlineData(10_000, "Mười nghìn đồng")]
    [InlineData(15_500, "Mười lăm nghìn năm trăm đồng")]
    [InlineData(100_000, "Một trăm nghìn đồng")]
    [InlineData(1_000_000, "Một triệu đồng")]
    [InlineData(1_000_001, "Một triệu không trăm linh một đồng")]
    [InlineData(1_050_000, "Một triệu không trăm năm mươi nghìn đồng")]
    [InlineData(2_500_000, "Hai triệu năm trăm nghìn đồng")]
    [InlineData(12_345_678, "Mười hai triệu ba trăm bốn mươi lăm nghìn sáu trăm bảy mươi tám đồng")]
    [InlineData(1_000_000_000, "Một tỷ đồng")]
    [InlineData(1_205_030_000, "Một tỷ hai trăm linh năm triệu không trăm ba mươi nghìn đồng")]
    [InlineData(2_000_000_005, "Hai tỷ không trăm linh năm đồng")]
    [InlineData(25_000_000_000, "Hai mươi lăm tỷ đồng")]
    [InlineData(1_000_000_000_000, "Một nghìn tỷ đồng")]
    [InlineData(1_234_567_890_123, "Một nghìn hai trăm ba mươi tư tỷ năm trăm sáu mươi bảy triệu tám trăm chín mươi nghìn một trăm hai mươi ba đồng")]
    public void Reads_money_in_vietnamese(long amount, string expected) =>
        VietnameseNumberReader.ReadMoney(amount).ShouldBe(expected);

    [Fact]
    public void Spec_example() =>
        VietnameseNumberReader.ReadMoney(1_205_030_000m).ShouldBe("Một tỷ hai trăm linh năm triệu không trăm ba mươi nghìn đồng");

    [Theory]
    [InlineData(1999.4, "Một nghìn chín trăm chín mươi chín đồng")]
    [InlineData(1999.5, "Hai nghìn đồng")]
    public void Rounds_to_whole_dong(double amount, string expected) =>
        VietnameseNumberReader.ReadMoney((decimal)amount).ShouldBe(expected);

    [Fact]
    public void Negative_numbers_are_prefixed() =>
        VietnameseNumberReader.Read(-1_500).ShouldBe("âm một nghìn năm trăm");
}
