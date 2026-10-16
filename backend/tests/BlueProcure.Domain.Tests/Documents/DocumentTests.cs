using BlueProcure.Domain.Common;
using BlueProcure.Domain.Documents;
using Shouldly;

namespace BlueProcure.Domain.Tests.Documents;

public class DocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 3, 0, 0, TimeSpan.Zero);

    private static Document NewDocument() =>
        new(DocumentType.Contract, "Hợp đồng số 01", "Contract", Guid.NewGuid(), Guid.NewGuid(), null, null);

    private static DocumentVersion NewVersion(Document d) => new(d.Id, "Tạo mới", null, "Tester", Now);

    [Fact]
    public void Versions_are_numbered_sequentially_and_latest_becomes_current()
    {
        var doc = NewDocument();
        var v1 = NewVersion(doc);
        var v2 = NewVersion(doc);

        doc.AddVersion(v1).ShouldBeFalse();
        doc.AddVersion(v2).ShouldBeFalse();

        v1.VersionNumber.ShouldBe(1);
        v2.VersionNumber.ShouldBe(2);
        doc.CurrentVersionId.ShouldBe(v2.Id);
    }

    [Fact]
    public void Signed_version_is_immutable()
    {
        var doc = NewDocument();
        var v1 = NewVersion(doc);
        doc.AddVersion(v1);
        v1.AttachPdf("a.pdf", "hash", 10);

        v1.ApplySignedPdf("a-signed.pdf", "hash2", 12);

        v1.IsLocked.ShouldBeTrue();
        Should.Throw<DomainException>(() => v1.AttachPdf("b.pdf", "x", 1)).Code.ShouldBe("DOCUMENT_VERSION_LOCKED");
        Should.Throw<DomainException>(() => v1.AttachDocx("b.docx", "x", 1)).Code.ShouldBe("DOCUMENT_VERSION_LOCKED");
    }

    [Fact]
    public void New_version_after_signature_requires_workflow_restart()
    {
        var doc = NewDocument();
        var v1 = NewVersion(doc);
        doc.AddVersion(v1);
        v1.AttachPdf("a.pdf", "hash", 10);
        v1.ApplySignedPdf("a-signed.pdf", "hash2", 12);

        doc.AddVersion(NewVersion(doc)).ShouldBeTrue();
    }

    [Fact]
    public void Signing_requires_a_pdf()
    {
        var doc = NewDocument();
        var v1 = NewVersion(doc);
        doc.AddVersion(v1);

        Should.Throw<DomainException>(() => v1.ApplySignedPdf("x.pdf", "h", 1)).Code.ShouldBe("DOCUMENT_PDF_REQUIRED");
    }

    [Fact]
    public void Completed_document_rejects_new_versions()
    {
        var doc = NewDocument();
        doc.AddVersion(NewVersion(doc));
        doc.Complete();

        doc.CurrentVersion!.IsFinal.ShouldBeTrue();
        Should.Throw<DomainException>(() => doc.AddVersion(NewVersion(doc))).Code.ShouldBe("DOCUMENT_CLOSED");
    }

    [Fact]
    public void Version_of_another_document_is_rejected()
    {
        var doc = NewDocument();
        var other = NewDocument();

        Should.Throw<DomainException>(() => doc.AddVersion(NewVersion(other))).Code.ShouldBe("DOCUMENT_VERSION_MISMATCH");
    }

    [Fact]
    public void Template_versions_activate_latest_and_can_roll_back()
    {
        var template = new DocumentTemplate("hd_thuoc", "Hợp đồng thuốc", TemplateKind.Contract, null, true);

        template.AddVersion("v1.docx", "h1", 1, ["{{Contract.Number}}"], null, null, Now);
        template.AddVersion("v2.docx", "h2", 1, ["{{Contract.Number}}"], "Sửa điều 5", null, Now);

        template.Code.ShouldBe("HD_THUOC");
        template.ActiveVersion!.FileKey.ShouldBe("v2.docx");
        template.Activate(1);
        template.ActiveVersion!.FileKey.ShouldBe("v1.docx");
        Should.Throw<DomainException>(() => template.Activate(9)).Code.ShouldBe("TEMPLATE_VERSION_NOT_FOUND");
    }
}
