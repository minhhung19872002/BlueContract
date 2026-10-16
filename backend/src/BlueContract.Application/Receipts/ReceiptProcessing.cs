using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Proposals;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Receipts;
using BlueContract.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Receipts;

/// <summary>Steps shared by receipts from the HIS and from the portal: PDF generation, the 5-signature workflow, tracking.</summary>
public sealed class ReceiptProcessing(
    IAppDbContext db,
    ReceiptDocumentData documentData,
    TemplateService templates,
    DocumentService documents,
    WorkflowService workflows,
    ContractTracking tracking,
    ICurrentUser current,
    IClock clock)
{
    public const string OwnerType = "WarehouseReceipt";

    /// <summary>Service account shown as "người gửi" of receipts that arrive from the HIS (no interactive login).</summary>
    public const string HisServiceUserName = "his.integration";

    public static string Title(WarehouseReceipt r) =>
        $"{(r.IsVirtual ? "Phiếu xác nhận khối lượng" : r.Type == ReceiptType.Return ? "Phiếu hoàn trả" : "Phiếu nhập kho")} {r.Number}";

    /// <summary>Renders the receipt PDF: a new document, or a new version while nobody has signed yet.</summary>
    public async Task<Result> GenerateDocumentAsync(WarehouseReceipt r, string reason, CancellationToken ct)
    {
        if (r.SupplierId is null)
        {
            return Result.Success(); // unknown supplier: Invalid receipt, nothing to sign
        }

        Document? document = null;
        if (r.DocumentId is { } docId)
        {
            document = await db.Documents.Include(d => d.Versions).FirstAsync(d => d.Id == docId, ct);
            if (document.CurrentVersion?.IsLocked == true || document.Status is DocumentStatus.Completed or DocumentStatus.Cancelled)
            {
                return Result.Success(); // signed versions are immutable (spec §5.7); invoice data stays on the receipt
            }
        }

        var template = await db.DocumentTemplates.AsNoTracking().Where(t => t.Kind == TemplateKind.WarehouseReceipt && t.IsActive).OrderBy(t => t.Code).FirstOrDefaultAsync(ct);
        if (template is null)
        {
            return Result.Failure(Error.Validation("RECEIPT_TEMPLATE_MISSING"));
        }

        var rendered = await templates.RenderAsync(template.Id, await documentData.BuildAsync(r, ct), ct);
        if (!rendered.IsSuccess)
        {
            return Result.Failure(rendered.Error!);
        }

        if (document is null)
        {
            document = await documents.CreateFromDocxAsync(DocumentType.WarehouseReceipt, Title(r), OwnerType, r.Id, r.SupplierId, r.ContractId, template.Id,
                rendered.Value, reason, ct);
            r.AttachDocument(document.Id);
        }
        else
        {
            if (document.SupplierId is null)
            {
                document.SetSupplierParty(r.SupplierId.Value);
            }

            document.Rename(Title(r));
            await documents.AddDocxVersionAsync(document, rendered.Value, reason, current.UserId, current.FullName ?? "Hệ thống HIS", ct);
        }

        if (r.IsVirtual)
        {
            await documents.PrepareForSigningAsync(document, finalize: false, ct); // PDF only, no signing
        }

        return Result.Success();
    }

    /// <summary>Starts the receipt workflow (or re-submits it after a return). Saved by the caller.</summary>
    public async Task<Result> StartSigningAsync(WarehouseReceipt r, Guid startedBy, string startedByName, CancellationToken ct)
    {
        if (r.DocumentId is not { } docId)
        {
            return Result.Failure(Error.Validation("RECEIPT_SUPPLIER_UNKNOWN"));
        }

        var document = await db.Documents.Include(d => d.Versions).FirstAsync(d => d.Id == docId, ct);
        var existing = await db.WorkflowInstances.Include("_steps").Include("_events")
            .Where(w => w.DocumentId == docId && (w.Status == WorkflowStatus.Running || w.Status == WorkflowStatus.Returned))
            .FirstOrDefaultAsync(ct);
        if (existing?.Status == WorkflowStatus.Returned)
        {
            await workflows.ResubmitForModuleAsync(existing, document, current.FullName ?? startedByName, ct);
            return Result.Success();
        }

        if (existing is not null)
        {
            return Result.Success(); // already running (no signature yet): the new version is what will be signed
        }

        var facilityId = r.WarehouseId is { } wid ? await db.Warehouses.Where(w => w.Id == wid).Select(w => (Guid?)w.FacilityId).FirstOrDefaultAsync(ct) : null;
        var started = await workflows.StartForSystemAsync(document, startedBy, startedByName, facilityId, null, ct);
        return started.IsSuccess ? Result.Success() : Result.Failure(started.Error!);
    }

    public async Task<bool> HasSignaturesAsync(WarehouseReceipt r, CancellationToken ct) =>
        r.DocumentId is { } docId && await db.Signatures.AnyAsync(s => s.DocumentId == docId, ct);

    /// <summary>Recomputes "đã nhập kho" of the contract items and "đã giao" of the call orders touched (after SaveChanges).</summary>
    public async Task RecalculateAsync(IEnumerable<Guid> contractItemIds, IEnumerable<Guid?> callOrderIds, CancellationToken ct)
    {
        await tracking.RecalculateAsync([.. contractItemIds.Distinct()], ct);
        await tracking.RecalculateDeliveriesAsync([.. callOrderIds.Where(x => x is not null).Select(x => x!.Value).Distinct()], ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The HIS service account (created on first use, inactive: it can never sign in).</summary>
    public async Task<(Guid Id, string Name)> HisServiceUserAsync(CancellationToken ct)
    {
        var user = await db.Users.IgnoreQueryFilters().Where(u => u.UserName == HisServiceUserName).Select(u => new { u.Id, u.FullName }).FirstOrDefaultAsync(ct);
        if (user is not null)
        {
            return (user.Id, user.FullName);
        }

        var account = new User(HisServiceUserName, "Hệ thống HIS (tích hợp)", null, UserType.Internal, null);
        account.UpdateProfile("Hệ thống HIS (tích hợp)", null, null, "Tài khoản dịch vụ tích hợp HIS", null, null);
        account.SetStatus(UserStatus.Inactive);
        db.Users.Add(account);
        await db.SaveChangesAsync(ct);
        return (account.Id, account.FullName);
    }

    public DateTimeOffset Now => clock.UtcNow;
}
