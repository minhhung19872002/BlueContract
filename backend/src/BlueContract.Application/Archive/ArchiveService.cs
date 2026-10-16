using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Payments;
using BlueContract.Domain.Proposals;
using BlueContract.Domain.Receipts;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Archive;

public sealed record ArchiveSearchItemDto(
    Guid ContractId,
    string ContractNumber,
    string ContractName,
    string SupplierName,
    string? BidDecisionNumber,
    ContractStatus Status,
    bool IsArchiveComplete,
    int DocumentCount);

/// <summary>A downloadable file of the dossier; <see cref="Id"/> is an opaque token valid only inside its contract's tree.</summary>
public sealed record ArchiveFileDto(string Id, string FileName, string Kind, string? Sha256, long? Size);

public sealed record ArchiveNodeDto(
    string Key,
    string Title,
    string Kind,
    Guid? DocumentId,
    string? Status,
    DateOnly? Date,
    string? Link,
    IReadOnlyList<ArchiveFileDto> Files,
    IReadOnlyList<ArchiveNodeDto> Children);

public sealed record ArchiveTreeDto(
    Guid ContractId,
    string ContractNumber,
    string ContractName,
    string SupplierName,
    string? BidDecisionNumber,
    ContractStatus Status,
    bool IsArchiveComplete,
    DateTimeOffset? ArchiveCompletedAt,
    string? ArchiveCompletedByName,
    int FileCount,
    IReadOnlyList<ArchiveNodeDto> Folders);

public sealed record ArchiveCompleteRequest(bool Complete);

public sealed record ArchiveIndexEntry(int No, string Folder, string Title, string FileName, string Sha256, string? ExpectedSha256, long Size)
{
    public bool HashMatches => ExpectedSha256 is null || string.Equals(ExpectedSha256, Sha256, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Renders <c>muc_luc.pdf</c> (list of every file of the dossier with its SHA-256).</summary>
public interface IArchiveIndexRenderer
{
    Task<byte[]> RenderPdfAsync(ArchiveTreeDto tree, string generatedBy, DateTimeOffset generatedAt, IReadOnlyList<ArchiveIndexEntry> entries, CancellationToken ct);
}

/// <summary>
/// Hồ sơ lưu trữ (spec §9.1-12): tìm theo hợp đồng hoặc số quyết định → cây tài liệu QĐ → HĐ (+phụ lục) → Đề xuất / Tổng
/// hợp / Gọi hàng → Phiếu nhập kho → Nghiệm thu → 08a + UNC → Thanh lý; tải ZIP kèm mục lục và kiểm tra hash.
/// </summary>
public sealed class ArchiveService(IAppDbContext db, IFileStorage storage, IArchiveIndexRenderer indexRenderer, IAuditLogger audit, ICurrentUser current, IClock clock, DataScope dataScope)
{
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(5);

    public async Task<PagedResult<ArchiveSearchItemDto>> SearchAsync(PageQuery q, CancellationToken ct)
    {
        var query = await dataScope.ContractsAsync(db.Contracts.AsNoTracking().Where(c => c.Status != ContractStatus.Draft && c.Status != ContractStatus.Cancelled), ct);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            query = query.Where(c => EF.Functions.ILike(c.Number, p) || EF.Functions.ILike(c.Name, p)
                || db.BidDecisions.Any(b => b.Id == c.BidDecisionId && EF.Functions.ILike(b.Number, p))
                || db.Suppliers.Any(s => s.Id == c.SupplierId && (EF.Functions.ILike(s.Name, p) || EF.Functions.ILike(s.TaxCode, p))));
        }

        return await query.OrderByDescending(c => c.EffectiveAt ?? c.CreatedAt)
            .Select(c => new ArchiveSearchItemDto(
                c.Id, c.Number, c.Name,
                db.Suppliers.Where(s => s.Id == c.SupplierId).Select(s => s.Name).FirstOrDefault() ?? string.Empty,
                db.BidDecisions.Where(b => b.Id == c.BidDecisionId).Select(b => b.Number).FirstOrDefault(),
                c.Status, c.IsArchiveComplete,
                db.Documents.Count(d => d.ContractId == c.Id && d.Status != DocumentStatus.Cancelled)))
            .ToPagedAsync(q, ct);
    }

    public async Task<Result<ArchiveTreeDto>> TreeAsync(Guid contractId, CancellationToken ct)
    {
        var tree = await BuildAsync(contractId, ct);
        if (tree is null)
        {
            return Error.NotFound();
        }

        audit.Log("ArchiveView", nameof(Contract), contractId.ToString(), new { tree.ContractNumber });
        await db.SaveChangesAsync(ct);
        return tree;
    }

    /// <summary>Presigned link of one file of the dossier (the token must belong to this contract's tree).</summary>
    public async Task<Result<FileLinkDto>> FileLinkAsync(Guid contractId, string fileId, CancellationToken ct)
    {
        var tree = await BuildAsync(contractId, ct);
        var file = tree is null ? null : Flatten(tree).SelectMany(n => n.Node.Files).FirstOrDefault(f => f.Id == fileId);
        if (file is null)
        {
            return Error.NotFound();
        }

        audit.Log("ArchiveFileDownload", nameof(Contract), contractId.ToString(), new { file.FileName });
        await db.SaveChangesAsync(ct);
        return new FileLinkDto(await storage.GetPresignedDownloadUrlAsync(KeyOf(file.Id), LinkLifetime, ct), file.FileName, clock.UtcNow.Add(LinkLifetime));
    }

    /// <summary>
    /// Tải toàn bộ hồ sơ dạng ZIP: one folder per stage, every file re-hashed while copied and compared with the SHA-256
    /// recorded when it was stored; <c>muc_luc.pdf</c> lists them all (spec §9.1-12, §11 toàn vẹn tài liệu).
    /// </summary>
    public async Task<Result<(byte[] Content, string FileName)>> ZipAsync(Guid contractId, CancellationToken ct)
    {
        var tree = await BuildAsync(contractId, ct);
        if (tree is null)
        {
            return Error.NotFound();
        }

        var entries = new List<ArchiveIndexEntry>();
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var folderNo = 0;
            foreach (var folder in tree.Folders)
            {
                folderNo++;
                var folderName = $"{folderNo:00}_{Slug(folder.Title)}";
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (node, _) in Flatten(folder))
                {
                    foreach (var file in node.Files)
                    {
                        var name = Unique(used, $"{Slug(node.Title)}{(node.Files.Count > 1 ? "_" + Slug(file.Kind) : string.Empty)}{Path.GetExtension(file.FileName)}");
                        var entry = zip.CreateEntry($"{folderName}/{name}", CompressionLevel.Optimal);
                        await using var source = await storage.DownloadAsync(KeyOf(file.Id), ct);
                        await using var target = entry.Open();
                        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        var chunk = new byte[81920];
                        long size = 0;
                        int read;
                        while ((read = await source.ReadAsync(chunk, ct)) > 0)
                        {
                            hash.AppendData(chunk, 0, read);
                            await target.WriteAsync(chunk.AsMemory(0, read), ct);
                            size += read;
                        }

                        entries.Add(new ArchiveIndexEntry(entries.Count + 1, folder.Title, node.Title, $"{folderName}/{name}",
                            Convert.ToHexStringLower(hash.GetHashAndReset()), file.Sha256, size));
                    }
                }
            }

            var index = await indexRenderer.RenderPdfAsync(tree, current.FullName ?? string.Empty, clock.UtcNow, entries, ct);
            var indexEntry = zip.CreateEntry("muc_luc.pdf", CompressionLevel.Optimal);
            await using (var stream = indexEntry.Open())
            {
                await stream.WriteAsync(index, ct);
            }
        }

        audit.Log("ArchiveDownload", nameof(Contract), contractId.ToString(),
            new { tree.ContractNumber, files = entries.Count, mismatches = entries.Count(e => !e.HashMatches) });
        await db.SaveChangesAsync(ct);
        return (buffer.ToArray(), $"ho-so-{Slug(tree.ContractNumber)}.zip");
    }

    public async Task<Result<ArchiveTreeDto>> SetCompleteAsync(Guid contractId, ArchiveCompleteRequest r, CancellationToken ct)
    {
        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract is null || !await dataScope.CanSeeContractAsync(contractId, ct))
        {
            return Error.NotFound();
        }

        contract.SetArchiveComplete(r.Complete, current.FullName, clock.UtcNow);
        audit.Log(r.Complete ? "ArchiveComplete" : "ArchiveReopen", nameof(Contract), contractId.ToString(), new { contract.Number });
        await db.SaveChangesAsync(ct);
        return (await BuildAsync(contractId, ct))!;
    }

    private async Task<ArchiveTreeDto?> BuildAsync(Guid contractId, CancellationToken ct)
    {
        var contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract is null || !await dataScope.CanSeeContractAsync(contractId, ct))
        {
            return null;
        }

        var supplier = await db.Suppliers.AsNoTracking().Where(s => s.Id == contract.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        var bid = contract.BidDecisionId is { } bidId ? await db.BidDecisions.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bidId, ct) : null;
        var documents = await db.Documents.AsNoTracking().Include(d => d.Versions)
            .Where(d => d.ContractId == contractId && d.Status != DocumentStatus.Cancelled).ToListAsync(ct);

        // Đề xuất / tổng hợp / gọi hàng are shared by several contracts: take those with a line of this contract.
        var proposals = await db.Proposals.AsNoTracking().Where(p => p.Status != ProposalStatus.Cancelled && p.Lines.Any(l => l.ContractId == contractId))
            .OrderBy(p => p.CreatedAt).Select(p => new { p.Number, p.DocumentId, p.Status, p.CreatedAt }).ToListAsync(ct);
        var summaries = await db.ProposalSummaries.AsNoTracking().Where(s => s.Status != ProposalSummaryStatus.Cancelled && s.Lines.Any(l => l.ContractId == contractId))
            .OrderBy(s => s.CreatedAt).Select(s => new { s.Number, s.DocumentId, s.Status, s.CreatedAt }).ToListAsync(ct);
        var calls = await db.CallOrders.AsNoTracking().Where(o => o.Status != CallOrderStatus.Cancelled && o.Lines.Any(l => l.ContractId == contractId))
            .OrderBy(o => o.CallDate).Select(o => new { o.Id, o.Number, o.DocumentId, o.Status, o.CallDate }).ToListAsync(ct);
        var sharedIds = proposals.Select(p => p.DocumentId).Concat(summaries.Select(s => s.DocumentId)).Concat(calls.Select(c => c.DocumentId))
            .Where(id => id is not null).Select(id => id!.Value).ToList();
        documents.AddRange(await db.Documents.AsNoTracking().Include(d => d.Versions)
            .Where(d => sharedIds.Contains(d.Id) && d.ContractId != contractId).ToListAsync(ct));
        var byId = documents.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());

        var appendices = await db.ContractAppendices.AsNoTracking().Where(a => a.ContractId == contractId)
            .OrderBy(a => a.CreatedAt).Select(a => new { a.Id, a.Number, a.DocumentId, a.Status }).ToListAsync(ct);
        var receipts = await db.WarehouseReceipts.AsNoTracking().Where(r => r.ContractId == contractId && r.Status != ReceiptStatus.Cancelled)
            .OrderBy(r => r.ReceiptDate).ThenBy(r => r.Number).ToListAsync(ct);
        var acceptances = await db.AcceptanceRecords.AsNoTracking().Where(a => a.ContractId == contractId && a.Status != PaymentRecordStatus.Cancelled)
            .OrderBy(a => a.PeriodFrom).ToListAsync(ct);
        var valuations = await db.ValuationRecords.AsNoTracking().Include(v => v.PaymentOrders)
            .Where(v => v.ContractId == contractId && v.Status != PaymentRecordStatus.Cancelled).OrderBy(v => v.PaymentSequence).ToListAsync(ct);
        var liquidations = await db.LiquidationRecords.AsNoTracking().Include(l => l.Settlements)
            .Where(l => l.ContractId == contractId && l.Status != PaymentRecordStatus.Cancelled).ToListAsync(ct);

        ArchiveNodeDto Doc(Guid? documentId, string title, string? status, DateOnly? date, string? link, params ArchiveFileDto[] extra)
        {
            var document = documentId is { } id ? byId.GetValueOrDefault(id) : null;
            var version = document?.CurrentVersion;
            var files = new List<ArchiveFileDto>();
            if (version?.PdfFileKey is { } pdf)
            {
                files.Add(new ArchiveFileDto(Token(pdf), $"{title}.pdf", version.SignatureCount > 0 ? $"PDF đã ký ({version.SignatureCount} chữ ký)" : "PDF",
                    version.PdfSha256, version.PdfSize));
            }

            files.AddRange(extra);
            return new ArchiveNodeDto("doc-" + (documentId?.ToString("N") ?? Guid.NewGuid().ToString("N")), title, "Document", document?.Id, status, date, link, files, []);
        }

        static DateOnly Local(DateTimeOffset at) => DateOnly.FromDateTime(at.ToOffset(TimeSpan.FromHours(7)).DateTime);

        var folders = new List<ArchiveNodeDto>
        {
            Folder("bid", "Quyết định thầu", bid is null
                ? []
                : [new ArchiveNodeDto("bid-" + bid.Id.ToString("N"), $"Quyết định {bid.Number}", "Document", null, bid.Status.ToString(), bid.SignedDate, $"/quyet-dinh-thau/{bid.Id}",
                    bid.AttachmentKey is { } key ? [new ArchiveFileDto(Token(key), bid.AttachmentName ?? $"quyet-dinh{Path.GetExtension(key)}", "Văn bản quyết định", bid.AttachmentSha256, null)] : [], [])]),
            Folder("contract", "Hợp đồng và phụ lục",
            [
                Doc(contract.DocumentId, $"Hợp đồng {contract.Number}", contract.Status.ToString(), contract.SignedDate, $"/hop-dong/{contract.Id}"),
                .. appendices.Select(a => Doc(a.DocumentId, $"Phụ lục {a.Number}", a.Status.ToString(), null, $"/hop-dong/{contract.Id}?tab=appendices")),
            ]),
            Folder("proposal", "Đề xuất – Tổng hợp – Gọi hàng",
            [
                .. proposals.Select(p => Doc(p.DocumentId, $"Phiếu đề xuất {p.Number}", p.Status.ToString(), Local(p.CreatedAt), null)),
                .. summaries.Select(s => Doc(s.DocumentId, $"Tổng hợp đề xuất {s.Number}", s.Status.ToString(), Local(s.CreatedAt), null)),
                .. calls.Select(o => Doc(o.DocumentId, $"Đơn gọi hàng {o.Number}", o.Status.ToString(), o.CallDate, $"/goi-hang/{o.Id}")),
            ]),
            Folder("receipt", "Phiếu nhập kho",
            [
                .. receipts.Select(r => Doc(r.DocumentId, $"{(r.IsVirtual ? "Phiếu xác nhận khối lượng" : r.Type == ReceiptType.Return ? "Phiếu hoàn trả" : "Phiếu nhập kho")} {r.Number}",
                    r.Status.ToString(), r.ReceiptDate, $"/nhap-kho/{r.Id}",
                    [
                        .. r.InvoiceXmlFileKey is { } xml ? [new ArchiveFileDto(Token(xml), $"hoa-don-{r.InvoiceNumber}.xml", "Hóa đơn điện tử (XML)", null, null)] : Array.Empty<ArchiveFileDto>(),
                        .. r.InvoicePdfFileKey is { } pdf ? [new ArchiveFileDto(Token(pdf), $"hoa-don-{r.InvoiceNumber}.pdf", "Hóa đơn (PDF)", null, null)] : Array.Empty<ArchiveFileDto>(),
                    ])),
            ]),
            Folder("acceptance", "Nghiệm thu",
                [.. acceptances.Select(a => Doc(a.DocumentId, $"Biên bản nghiệm thu {a.Number}", a.Status.ToString(), a.PeriodTo, $"/thanh-toan/nghiem-thu/{a.Id}"))]),
            Folder("valuation", "Xác định giá trị (08a), đề nghị thanh toán, ủy nhiệm chi",
            [
                .. valuations.SelectMany(v => new[]
                {
                    Doc(v.DocumentId, $"Biên bản 08a {v.Number}", v.Status.ToString(), v.CompletedAt is { } c ? Local(c) : null, $"/thanh-toan/xac-dinh-gia-tri/{v.Id}",
                        [.. v.PaymentOrders.Where(p => p.FileKey is not null).Select(p => new ArchiveFileDto(Token(p.FileKey!), p.FileName ?? $"unc-{p.Number}.pdf", $"Ủy nhiệm chi {p.Number}", null, null))]),
                    Doc(v.PaymentRequestDocumentId, $"Đề nghị thanh toán {v.PaymentRequestNumber}", v.PaymentRequestStatus.ToString(), null, $"/thanh-toan/xac-dinh-gia-tri/{v.Id}"),
                }),
            ]),
            Folder("liquidation", "Thanh lý",
                [.. liquidations.Select(l => Doc(l.DocumentId, $"Biên bản thanh lý {l.Number}", l.Status.ToString(), l.CompletedAt is { } c ? Local(c) : null, $"/thanh-toan/thanh-ly/{l.Id}",
                    [.. l.Settlements.Where(s => s.FileKey is not null).Select(s => new ArchiveFileDto(Token(s.FileKey!), s.FileName ?? "chung-tu.pdf", $"Chứng từ chốt công nợ {s.VoucherNumber}", null, null))]))]),
        };

        return new ArchiveTreeDto(contract.Id, contract.Number, contract.Name, supplier ?? string.Empty, bid?.Number, contract.Status, contract.IsArchiveComplete,
            contract.ArchiveCompletedAt, contract.ArchiveCompletedByName, folders.SelectMany(f => f.Children).Sum(n => n.Files.Count), folders);
    }

    private static ArchiveNodeDto Folder(string key, string title, IReadOnlyList<ArchiveNodeDto> children) =>
        new("folder-" + key, title, "Folder", null, null, null, null, [], children);

    private static IEnumerable<(ArchiveNodeDto Node, int Depth)> Flatten(ArchiveTreeDto tree) => tree.Folders.SelectMany(Flatten);

    private static IEnumerable<(ArchiveNodeDto Node, int Depth)> Flatten(ArchiveNodeDto node)
    {
        var stack = new Stack<(ArchiveNodeDto, int)>([(node, 0)]);
        while (stack.Count > 0)
        {
            var (n, depth) = stack.Pop();
            yield return (n, depth);
            foreach (var child in n.Children.Reverse())
            {
                stack.Push((child, depth + 1));
            }
        }
    }

    private static string Token(string key) => Convert.ToBase64String(Encoding.UTF8.GetBytes(key)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string KeyOf(string token)
    {
        var base64 = token.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    /// <summary>ASCII file/folder names without Vietnamese diacritics ("Hợp đồng 16/2026" → "Hop_dong_16-2026").</summary>
    public static string Slug(string text)
    {
        var normalized = text.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '.' ? ch : ch is '/' or '\\' ? '-' : '_');
        }

        var slug = string.Join('_', sb.ToString().Split('_', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length > 80 ? slug[..80] : slug;
    }

    private static string Unique(HashSet<string> used, string name)
    {
        var candidate = name;
        for (var i = 2; !used.Add(candidate); i++)
        {
            candidate = $"{Path.GetFileNameWithoutExtension(name)}_{i}{Path.GetExtension(name)}";
        }

        return candidate;
    }
}

