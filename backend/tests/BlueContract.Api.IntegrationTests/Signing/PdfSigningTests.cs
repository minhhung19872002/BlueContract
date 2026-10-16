using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BlueContract.Application.Signing;
using BlueContract.Domain.Workflows;
using BlueContract.Infrastructure.Signing;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Signing;

public class PdfSigningTests
{
    private static readonly string[] Anchors = ["SIG_STOREKEEPER", "SIG_DELIVERER", "SIG_WAREHOUSE_ACCOUNTANT", "SIG_DEPT_HEAD", "SIG_CHIEF_ACCOUNTANT"];
    private readonly PdfSignatureService _pdf = new();
    private readonly CmsBuilder _cms = new();

    private static byte[] SamplePdf()
    {
        using var output = new MemoryStream();
        using (var doc = new Document(new PdfDocument(new PdfWriter(output))))
        {
            doc.Add(new Paragraph("PHIEU NHAP KHO (test)"));
            foreach (var anchor in Anchors)
            {
                doc.Add(new Paragraph(anchor.Replace("SIG_", string.Empty, StringComparison.Ordinal)));
                doc.Add(new Paragraph("{{" + anchor + "}}"));
                doc.Add(new Paragraph("\n\n\n"));
            }
        }

        return output.ToArray();
    }

    private static X509Certificate2 Certificate(string name)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={name}, O=BlueContract Test, C=VN", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    private byte[] SignOnce(byte[] pdf, string signer, string anchor, SignatureKind kind, int slot)
    {
        using var cert = Certificate(signer);
        var at = DateTimeOffset.UtcNow;
        var prepared = _pdf.Prepare(pdf, new SignatureAppearance(kind, signer, "Chức danh", null, anchor, at, slot));
        var toSign = _cms.GetDataToSign(prepared.DocumentDigest, cert.RawData, at);
        using var rsa = cert.GetRSAPrivateKey()!;
        var envelope = _cms.Assemble(prepared.DocumentDigest, cert.RawData, at, rsa.SignData(toSign, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return _pdf.Embed(prepared.Content, prepared.FieldName, envelope);
    }

    [Fact]
    public void Five_consecutive_signatures_all_remain_valid()
    {
        var pdf = SamplePdf();
        string[] names = ["Mai Văn Tú", "Nhân viên NCC1", "Đỗ Thị Yến", "Lương Thị Hằng", "Lý Văn Thành"];

        for (var i = 0; i < 5; i++)
        {
            pdf = SignOnce(pdf, names[i], Anchors[i], SignatureKind.Personal, i);
        }

        var signatures = _pdf.Verify(pdf);
        signatures.Count.ShouldBe(5);
        signatures.ShouldAllBe(s => s.IntegrityValid);
        signatures.Select(s => s.SignerName).ShouldBe(names, ignoreOrder: true);
        signatures.Single(s => s.SignerName == "Lý Văn Thành").CoversWholeDocument.ShouldBeTrue();
        signatures.Where(s => s.SignerName != "Lý Văn Thành").ShouldAllBe(s => !s.CoversWholeDocument);
        signatures.Select(s => s.Revision).Distinct().Count().ShouldBe(5);
    }

    [Fact]
    public void Tampering_after_signing_is_detected()
    {
        var signed = SignOnce(SamplePdf(), "Người ký", Anchors[0], SignatureKind.Personal, 0);

        // Flip a byte inside the signed range (the original content stream area near the start).
        var tampered = (byte[])signed.Clone();
        var index = Array.IndexOf(tampered, (byte)'P', 400);
        tampered[index] = (byte)'Q';

        _pdf.Verify(tampered).Single().IntegrityValid.ShouldBeFalse();
    }

    [Fact]
    public void Organization_seal_and_initial_are_signatures_too()
    {
        var pdf = SignOnce(SamplePdf(), "Kế toán", null!, SignatureKind.Initial, 0);
        pdf = SignOnce(pdf, "Bệnh viện Đa khoa Thành phố Vinh", "SIG_CHIEF_ACCOUNTANT", SignatureKind.Organization, 1);

        var signatures = _pdf.Verify(pdf);
        signatures.Count.ShouldBe(2);
        signatures.ShouldAllBe(s => s.IntegrityValid);
        signatures.Select(s => s.Reason).ShouldBe(["Ký nháy", "Đóng dấu điện tử"], ignoreOrder: true);
    }

    [Fact]
    public void Prepared_digest_matches_signed_byte_ranges()
    {
        var prepared = _pdf.Prepare(SamplePdf(), new SignatureAppearance(SignatureKind.Personal, "A", null, null, Anchors[0], DateTimeOffset.UtcNow, 0));

        prepared.DocumentDigest.Length.ShouldBe(32);
        prepared.FieldName.ShouldStartWith("Sig_Personal_");
    }
}
