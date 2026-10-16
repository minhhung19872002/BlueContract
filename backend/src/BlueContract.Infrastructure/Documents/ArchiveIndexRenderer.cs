using System.Globalization;
using System.Text.Json.Nodes;
using BlueContract.Application.Archive;
using BlueContract.Application.Documents;
using BlueContract.Infrastructure.Documents.Templates;

namespace BlueContract.Infrastructure.Documents;

/// <summary><c>muc_luc.pdf</c> of a dossier ZIP: built with the document builder, rendered by the template engine, converted by Gotenberg.</summary>
internal sealed class ArchiveIndexRenderer(IDocumentGenerator generator, IPdfConverter pdf) : IArchiveIndexRenderer
{
    private static readonly byte[] Template = new DocxBuilder().Landscape()
        .NationalHeader("{{OrganizationName | upper}}", null, null, "{{GeneratedAt}}")
        .Title("MỤC LỤC HỒ SƠ HỢP ĐỒNG", "Hợp đồng số {{ContractNumber}}")
        .List(
            "- Tên hợp đồng: {{ContractName}}",
            "- Nhà cung cấp: {{SupplierName}}",
            "- Quyết định thầu: {{BidDecisionNumber}}",
            "- Tình trạng hồ sơ: {{CompletionText}}",
            "- Người tải: {{GeneratedBy}} — {{FileCount}} tệp; kiểm tra toàn vẹn: {{IntegrityText}}")
        .Table("Files",
        [
            new Col("STT", "{{No}}", 5, Align.Center),
            new Col("Nhóm", "{{Folder}}", 15),
            new Col("Tài liệu", "{{Title}}", 22),
            new Col("Tệp trong gói", "{{FileName}}", 22),
            new Col("Dung lượng", "{{Size}}", 8, Align.Right),
            new Col("SHA-256", "{{Sha256}}", 22),
            new Col("Kiểm tra", "{{Check}}", 6, Align.Center),
        ])
        .P("Mã SHA-256 được tính lại khi đóng gói và đối chiếu với giá trị lưu lúc tải lên hệ thống. \"Khớp\": tệp không bị thay đổi.", italic: true)
        .Build();

    public async Task<byte[]> RenderPdfAsync(ArchiveTreeDto tree, string generatedBy, DateTimeOffset generatedAt, IReadOnlyList<ArchiveIndexEntry> entries, CancellationToken ct)
    {
        var vi = CultureInfo.GetCultureInfo("vi-VN");
        var mismatches = entries.Count(e => !e.HashMatches);
        var data = new JsonObject
        {
            ["OrganizationName"] = "Hồ sơ lưu trữ điện tử",
            ["GeneratedAt"] = "Lập ngày " + generatedAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            ["ContractNumber"] = tree.ContractNumber,
            ["ContractName"] = tree.ContractName,
            ["SupplierName"] = tree.SupplierName,
            ["BidDecisionNumber"] = tree.BidDecisionNumber ?? "Ngoài thầu",
            ["CompletionText"] = tree.IsArchiveComplete
                ? $"Đã xác nhận đầy đủ ({tree.ArchiveCompletedByName}, {tree.ArchiveCompletedAt?.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)})"
                : "Chưa xác nhận đầy đủ",
            ["GeneratedBy"] = generatedBy,
            ["FileCount"] = entries.Count,
            ["IntegrityText"] = mismatches == 0 ? "tất cả khớp" : $"{mismatches} tệp KHÔNG KHỚP",
            ["Files"] = new JsonArray([.. entries.Select(e => (JsonNode)new JsonObject
            {
                ["No"] = e.No,
                ["Folder"] = e.Folder,
                ["Title"] = e.Title,
                ["FileName"] = e.FileName,
                ["Size"] = (e.Size / 1024m).ToString("#,##0", vi) + " KB",
                ["Sha256"] = e.Sha256,
                ["Check"] = e.ExpectedSha256 is null ? "—" : e.HashMatches ? "Khớp" : "LỆCH",
            })]),
        };
        return await pdf.ConvertToPdfAsync(generator.Render(Template, data), "muc_luc", ct);
    }
}
