using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using BlueContract.Domain.Receipts;

namespace BlueContract.Application.Receipts;

public sealed record ParsedInvoice(InvoiceInfo Invoice, string? SellerName, string? BuyerTaxCode, string? BuyerName);

/// <summary>
/// Reads a Vietnamese e-invoice XML (hóa đơn điện tử theo Nghị định 123/2020/NĐ-CP, Thông tư 78/2021/TT-BTC):
/// HDon/DLHDon/TTChung (KHMSHDon + KHHDon = ký hiệu, SHDon = số, NLap = ngày lập), NDHDon/NBan/MST,
/// NDHDon/TToan (TgTCThue, TgTThue, TgTTTBSo). Namespaces are ignored; DTDs are refused (XXE-safe).
/// </summary>
public static class InvoiceXmlParser
{
    public static ParsedInvoice? TryParse(byte[] xml)
    {
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20_000_000 };
            using var stream = new MemoryStream(xml);
            using var reader = XmlReader.Create(stream, settings);
            doc = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        var general = Find(doc.Root, "TTChung");
        var content = Find(doc.Root, "NDHDon");
        if (general is null || content is null)
        {
            return null;
        }

        var series = Child(general, "KHMSHDon");
        var symbol = Child(general, "KHHDon");
        var number = Child(general, "SHDon");
        var date = Child(general, "NLap");
        var totals = Find(content, "TToan");
        var total = Amount(totals is null ? null : Child(totals, "TgTTTBSo"));
        if (symbol is null || number is null || !DateOnly.TryParse(date?[..Math.Min(10, date.Length)], CultureInfo.InvariantCulture, out var issued) || total is null)
        {
            return null;
        }

        var seller = Find(content, "NBan");
        var buyer = Find(content, "NMua");
        var invoice = new InvoiceInfo(
            (series ?? string.Empty) + symbol,
            number,
            issued,
            Amount(totals is null ? null : Child(totals, "TgTCThue")),
            Amount(totals is null ? null : Child(totals, "TgTThue")),
            total.Value,
            seller is null ? null : Child(seller, "MST"));
        return new ParsedInvoice(invoice, seller is null ? null : Child(seller, "Ten"), buyer is null ? null : Child(buyer, "MST"), buyer is null ? null : Child(buyer, "Ten"));
    }

    private static XElement? Find(XElement? root, string localName) =>
        root?.DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName == localName);

    private static string? Child(XElement parent, string localName)
    {
        var value = parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static decimal? Amount(string? text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
}
