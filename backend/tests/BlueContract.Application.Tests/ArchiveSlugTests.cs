using BlueContract.Application.Archive;
using Shouldly;

namespace BlueContract.Application.Tests.Archive;

public class ArchiveSlugTests
{
    [Theory]
    [InlineData("Hợp đồng 16/2026/HĐ-BVĐK", "Hop_dong_16-2026-HD-BVDK")]
    [InlineData("Biên bản 08a XD-2026-0001", "Bien_ban_08a_XD-2026-0001")]
    [InlineData("Xác định giá trị (08a), đề nghị thanh toán", "Xac_dinh_gia_tri_08a_de_nghi_thanh_toan")]
    public void Slugs_are_ascii_file_names_without_diacritics(string text, string expected) => ArchiveService.Slug(text).ShouldBe(expected);

    [Fact]
    public void Slugs_are_capped() => ArchiveService.Slug(new string('a', 200)).Length.ShouldBe(80);
}
