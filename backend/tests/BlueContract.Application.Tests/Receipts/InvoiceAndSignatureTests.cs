using System.Text;
using BlueContract.Application.Integration;
using BlueContract.Application.Receipts;
using Shouldly;

namespace BlueContract.Application.Tests.Receipts;

public class InvoiceXmlParserTests
{
    public const string SampleInvoice = """
        <?xml version="1.0" encoding="UTF-8"?>
        <HDon>
          <DLHDon Id="data">
            <TTChung>
              <PBan>2.0.1</PBan>
              <THDon>Hóa đơn giá trị gia tăng</THDon>
              <KHMSHDon>1</KHMSHDon>
              <KHHDon>C26TAA</KHHDon>
              <SHDon>158</SHDon>
              <NLap>2026-10-14</NLap>
              <DVTTe>VND</DVTTe>
            </TTChung>
            <NDHDon>
              <NBan><Ten>Công ty CP Thiết bị Y tế Miền Trung</Ten><MST>0302345678</MST></NBan>
              <NMua><Ten>Bệnh viện Đa khoa TP Vinh</Ten><MST>2900325438</MST></NMua>
              <DSHHDVu><HHDVu><STT>1</STT><THHDVu>Bơm tiêm 5ml</THHDVu><SLuong>1000</SLuong><DGia>809.52</DGia></HHDVu></DSHHDVu>
              <TToan>
                <TgTCThue>809524</TgTCThue>
                <TgTThue>40476</TgTThue>
                <TgTTTBSo>850000</TgTTTBSo>
                <TgTTTBChu>Tám trăm năm mươi nghìn đồng</TgTTTBChu>
              </TToan>
            </NDHDon>
          </DLHDon>
          <MCCQT>00ABC123</MCCQT>
        </HDon>
        """;

    [Fact]
    public void Reads_symbol_number_date_totals_and_parties()
    {
        var parsed = InvoiceXmlParser.TryParse(Encoding.UTF8.GetBytes(SampleInvoice));
        parsed.ShouldNotBeNull();
        parsed.Invoice.Symbol.ShouldBe("1C26TAA");
        parsed.Invoice.Number.ShouldBe("158");
        parsed.Invoice.Date.ShouldBe(new DateOnly(2026, 10, 14));
        parsed.Invoice.AmountBeforeVat.ShouldBe(809_524);
        parsed.Invoice.VatAmount.ShouldBe(40_476);
        parsed.Invoice.TotalAmount.ShouldBe(850_000);
        parsed.Invoice.SellerTaxCode.ShouldBe("0302345678");
        parsed.SellerName.ShouldBe("Công ty CP Thiết bị Y tế Miền Trung");
        parsed.BuyerTaxCode.ShouldBe("2900325438");
    }

    [Fact]
    public void Namespaced_documents_are_supported()
    {
        var xml = SampleInvoice.Replace("<HDon>", "<HDon xmlns=\"http://example.gov.vn/hddt\">", StringComparison.Ordinal);
        InvoiceXmlParser.TryParse(Encoding.UTF8.GetBytes(xml))!.Invoice.TotalAmount.ShouldBe(850_000);
    }

    [Theory]
    [InlineData("<HDon><DLHDon><TTChung><KHHDon>C26</KHHDon></TTChung></DLHDon></HDon>")]
    [InlineData("not xml")]
    [InlineData("<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><HDon>&e;</HDon>")]
    public void Incomplete_invalid_or_dtd_documents_are_rejected(string xml) =>
        InvoiceXmlParser.TryParse(Encoding.UTF8.GetBytes(xml)).ShouldBeNull();
}

public class HisRequestSignatureTests
{
    private const string Secret = "his-secret";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"hisReceiptId\":\"X\"}");

    [Fact]
    public void Signature_covers_method_path_timestamp_and_body()
    {
        var signature = HisRequestSignature.Sign(Secret, "POST", "/api/integration/his/warehouse-receipts", "1760600000", Body);
        signature.Length.ShouldBe(64);
        HisRequestSignature.Verify(Secret, "post", "/api/integration/his/warehouse-receipts", "1760600000", Body, signature.ToUpperInvariant()).ShouldBeTrue();

        HisRequestSignature.Verify("other", "POST", "/api/integration/his/warehouse-receipts", "1760600000", Body, signature).ShouldBeFalse();
        HisRequestSignature.Verify(Secret, "POST", "/api/integration/his/contracts", "1760600000", Body, signature).ShouldBeFalse();
        HisRequestSignature.Verify(Secret, "POST", "/api/integration/his/warehouse-receipts", "1760600001", Body, signature).ShouldBeFalse();
        HisRequestSignature.Verify(Secret, "POST", "/api/integration/his/warehouse-receipts", "1760600000", Encoding.UTF8.GetBytes("{}"), signature).ShouldBeFalse();
        HisRequestSignature.Verify(Secret, "POST", "/api/integration/his/warehouse-receipts", "1760600000", Body, null).ShouldBeFalse();
    }

    [Fact]
    public void Canonical_string_is_documented_format()
    {
        HisRequestSignature.Canonical("get", "/a?b=1", "10", []).ShouldBe("GET\n/a?b=1\n10\ne3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public void Timestamp_must_be_within_five_minutes()
    {
        var now = new DateTimeOffset(2026, 10, 16, 2, 0, 0, TimeSpan.Zero);
        HisRequestSignature.IsFresh(HisRequestSignature.Timestamp(now.AddMinutes(-5)), now).ShouldBeTrue();
        HisRequestSignature.IsFresh(HisRequestSignature.Timestamp(now.AddMinutes(5)), now).ShouldBeTrue();
        HisRequestSignature.IsFresh(HisRequestSignature.Timestamp(now.AddMinutes(-5).AddSeconds(-1)), now).ShouldBeFalse();
        HisRequestSignature.IsFresh("2026-10-16T02:00:00Z", now).ShouldBeFalse();
        HisRequestSignature.IsFresh(null, now).ShouldBeFalse();
        HisRequestSignature.IsFresh("99999999999999999", now).ShouldBeFalse();
    }

    [Fact]
    public void Identical_payloads_hash_identically()
    {
        HisReceiptPayload Payload(decimal qty) => new("H1", "PN1", Domain.Receipts.ReceiptType.Receipt, new DateOnly(2026, 10, 16), "KV01", "126", "0302", null, null, null,
            null, null, null, [new HisReceiptLinePayload("K1", "VT001", "Bơm tiêm", "Cái", null, null, qty, 850, 5)]);
        HisReceiptIngestion.PayloadHash(Payload(1)).ShouldBe(HisReceiptIngestion.PayloadHash(Payload(1)));
        HisReceiptIngestion.PayloadHash(Payload(1)).ShouldNotBe(HisReceiptIngestion.PayloadHash(Payload(2)));
    }
}
