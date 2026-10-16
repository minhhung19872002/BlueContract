using System.Reflection;
using System.Security.Cryptography;
using BlueContract.Application.Signing;
using BlueContract.Domain.Workflows;
using iText.Bouncycastle.Cert;
using iText.Bouncycastle.X509;
using iText.Commons.Bouncycastle.Cert;
using iText.Forms.Form.Element;
using iText.IO.Font;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Layout.Borders;
using iText.Signatures;

namespace BlueContract.Infrastructure.Signing;

/// <summary>
/// PAdES signing on top of iText 9 (AGPL — see ADR-025). Every signature is an incremental update, so earlier
/// signatures stay valid. The visible appearance is placed at the template's anchor text ({{SIG_*}}).
/// </summary>
internal sealed class PdfSignatureService : IPdfSignatureService
{
    private const int CmsReservedBytes = 24_000;
    private const float BoxWidth = 175f;
    private const float BoxHeight = 58f;
    private static readonly TimeZoneInfo VietnamZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    private static readonly Lazy<byte[]> RegularFont = new(() => LoadFont("NotoSans-Regular.ttf"));

    public PreparedPdf Prepare(byte[] pdf, SignatureAppearance a)
    {
        using var output = new MemoryStream();
        var reader = new PdfReader(new MemoryStream(pdf));
        var signer = new PdfSigner(reader, output, new StampingProperties().UseAppendMode());

        var fieldName = $"Sig_{a.Kind}_{Guid.CreateVersion7():N}"[..32];
        var (page, rect) = Locate(signer.GetDocument(), a);

        var font = PdfFontFactory.CreateFont(RegularFont.Value, PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED);
        var appearance = new SignatureFieldAppearance(SignerProperties.IGNORED_ID)
            .SetContent(Text(a))
            .SetFont(font)
            .SetFontSize(a.Kind == SignatureKind.Initial ? 6.5f : 7.5f);
        if (a.Kind == SignatureKind.Organization)
        {
            appearance.SetFontColor(new DeviceRgb(200, 16, 46));
            appearance.SetBorder(new SolidBorder(new DeviceRgb(200, 16, 46), 1.2f));
        }
        else
        {
            appearance.SetFontColor(new DeviceRgb(16, 70, 160));
            appearance.SetBorder(new SolidBorder(new DeviceRgb(16, 70, 160), 0.6f));
        }

        signer.SetSignerProperties(new SignerProperties()
            .SetFieldName(fieldName)
            .SetPageNumber(page)
            .SetPageRect(rect)
            .SetSignatureAppearance(appearance)
            .SetReason(a.Kind switch
            {
                SignatureKind.Organization => "Đóng dấu điện tử",
                SignatureKind.Initial => "Ký nháy",
                _ => "Ký duyệt tài liệu",
            })
            .SetLocation("Việt Nam")
            .SetClaimedSignDate(a.SignedAt.UtcDateTime));

        var container = new DigestCapturingContainer();
        signer.SignExternalContainer(container, CmsReservedBytes);
        return new PreparedPdf(output.ToArray(), fieldName, container.Digest ?? throw new InvalidOperationException("Digest not computed."));
    }

    public byte[] Embed(byte[] preparedPdf, string fieldName, byte[] cms)
    {
        if (cms.Length > CmsReservedBytes)
        {
            throw new InvalidOperationException("CMS larger than reserved space.");
        }

        using var output = new MemoryStream();
        PdfSigner.SignDeferred(new PdfReader(new MemoryStream(preparedPdf)), fieldName, output, new FixedContainer(cms));

        return output.ToArray();
    }

    public IReadOnlyList<PdfSignatureInfo> Verify(byte[] pdf)
    {
        using var document = new PdfDocument(new PdfReader(new MemoryStream(pdf)));
        var util = new SignatureUtil(document);
        var names = util.GetSignatureNames();
        var result = new List<PdfSignatureInfo>();
        foreach (var name in names)
        {
            var pkcs7 = util.ReadSignatureData(name);
            bool integrity;
            try
            {
                integrity = pkcs7.VerifySignatureIntegrityAndAuthenticity();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                integrity = false;
            }

            var cert = pkcs7.GetSigningCertificate();
            var subject = cert.GetSubjectDN().ToString() ?? string.Empty;
            var issuer = cert.GetIssuerDN().ToString() ?? string.Empty;
            var signDate = pkcs7.GetSignDate();
            result.Add(new PdfSignatureInfo(
                name,
                util.GetRevision(name),
                util.GetTotalRevisions(),
                util.SignatureCoversWholeDocument(name),
                integrity,
                subject,
                CommonName(subject),
                issuer,
                cert.GetSerialNumber().ToString(16).ToUpperInvariant(),
                subject == issuer,
                signDate == DateTime.MinValue ? null : new DateTimeOffset(DateTime.SpecifyKind(signDate, DateTimeKind.Utc)),
                pkcs7.GetTimeStampDate() != DateTime.MinValue,
                pkcs7.GetReason(),
                pkcs7.GetLocation()));
        }

        return result;
    }

    private static string Text(SignatureAppearance a)
    {
        var local = TimeZoneInfo.ConvertTime(a.SignedAt, VietnamZone).ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        return a.Kind switch
        {
            SignatureKind.Organization => $"ĐÃ ĐÓNG DẤU ĐIỆN TỬ\n{a.SignerName}\nThời gian: {local}",
            SignatureKind.Initial => $"Ký nháy: {a.SignerName}\n{local}",
            _ => string.Join('\n', new[]
            {
                a.Caption,
                $"Ký bởi: {a.SignerName}",
                a.SignerTitle,
                $"Thời gian: {local}",
            }.Where(s => !string.IsNullOrWhiteSpace(s))),
        };
    }

    /// <summary>Finds the anchor text on any page; otherwise a slot near the bottom of the last page.</summary>
    private static (int Page, Rectangle Rect) Locate(PdfDocument doc, SignatureAppearance a)
    {
        var width = a.Kind == SignatureKind.Initial ? 110f : BoxWidth;
        var height = a.Kind == SignatureKind.Initial ? 30f : BoxHeight;
        if (!string.IsNullOrEmpty(a.Anchor))
        {
            var pattern = System.Text.RegularExpressions.Regex.Escape("{{" + a.Anchor + "}}");
            for (var p = doc.GetNumberOfPages(); p >= 1; p--)
            {
                var strategy = new RegexBasedLocationExtractionStrategy(pattern);
                new PdfCanvasProcessor(strategy).ProcessPageContent(doc.GetPage(p));
                var hit = strategy.GetResultantLocations().FirstOrDefault();
                if (hit is null)
                {
                    continue;
                }

                var r = hit.GetRectangle();
                var page = doc.GetPage(p).GetPageSize();
                var x = Math.Clamp(r.GetX() + (r.GetWidth() / 2) - (width / 2), page.GetLeft() + 10, page.GetRight() - width - 10);
                var y = Math.Clamp(r.GetY() - height + 6, page.GetBottom() + 10, page.GetTop() - height - 10);
                return (p, new Rectangle(x, y, width, height));
            }
        }

        // No anchor: tile signatures across the bottom of the last page.
        var last = doc.GetNumberOfPages();
        var size = doc.GetPage(last).GetPageSize();
        var perRow = Math.Max(1, (int)((size.GetWidth() - 60) / (width + 10)));
        var col = a.FallbackSlot % perRow;
        var row = a.FallbackSlot / perRow;
        return (last, new Rectangle(size.GetLeft() + 30 + (col * (width + 10)), size.GetBottom() + 30 + (row * (height + 10)), width, height));
    }

    private static string? CommonName(string dn)
    {
        foreach (var part in dn.Split(','))
        {
            var kv = part.Trim();
            if (kv.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                return kv[3..];
            }
        }

        return null;
    }

    private static byte[] LoadFont(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().First(n => n.EndsWith(name, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Captures SHA-256 of the signed byte ranges and leaves the signature contents empty.</summary>
    private sealed class DigestCapturingContainer : IExternalSignatureContainer
    {
        public byte[]? Digest { get; private set; }

        public byte[] Sign(Stream data)
        {
            Digest = SHA256.HashData(data);
            return [];
        }

        public void ModifySigningDictionary(PdfDictionary signDic)
        {
            signDic.Put(PdfName.Filter, PdfName.Adobe_PPKLite);
            signDic.Put(PdfName.SubFilter, PdfName.ETSI_CAdES_DETACHED);
        }
    }

    private sealed class FixedContainer(byte[] cms) : IExternalSignatureContainer
    {
        public byte[] Sign(Stream data) => cms;

        public void ModifySigningDictionary(PdfDictionary signDic)
        {
        }
    }
}

/// <summary>CAdES-detached CMS (PAdES baseline B) assembled with iText's PdfPKCS7 from an external signature value.</summary>
internal sealed class CmsBuilder : ICmsBuilder
{
    public byte[] GetDataToSign(byte[] documentDigest, byte[] certificate, DateTimeOffset signingTime) =>
        Create(certificate, signingTime).GetAuthenticatedAttributeBytes(documentDigest, PdfSigner.CryptoStandard.CADES, null, null);

    public byte[] Assemble(byte[] documentDigest, byte[] certificate, DateTimeOffset signingTime, byte[] signatureValue)
    {
        var pkcs7 = Create(certificate, signingTime);
        pkcs7.GetAuthenticatedAttributeBytes(documentDigest, PdfSigner.CryptoStandard.CADES, null, null);
        pkcs7.SetExternalSignatureValue(signatureValue, null, "RSA");
        return pkcs7.GetEncodedPKCS7(documentDigest, PdfSigner.CryptoStandard.CADES, null, null, null);
    }

    private static PdfPKCS7 Create(byte[] certificate, DateTimeOffset signingTime)
    {
        var bc = new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(certificate);
        IX509Certificate[] chain = [new X509CertificateBC(bc)];
        var pkcs7 = new PdfPKCS7(null, chain, "SHA-256", false);
        pkcs7.SetSignDate(signingTime.UtcDateTime);
        return pkcs7;
    }
}
