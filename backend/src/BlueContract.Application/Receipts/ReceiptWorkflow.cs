using BlueContract.Application.Common;
using BlueContract.Application.Notifications;
using BlueContract.Application.Proposals;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Receipts;
using BlueContract.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Receipts;

/// <summary>
/// Spec §5.5 / §6.4: an Invalid receipt cannot be signed; from the deliverer (NCC) step on, the invoice is mandatory; other
/// warnings may be accepted but each signer must give a reason (kept as the step comment).
/// </summary>
internal sealed class ReceiptWorkflowGuard(IAppDbContext db) : IWorkflowGuard
{
    public bool Handles(DocumentType type) => type == DocumentType.WarehouseReceipt;

    public async Task<WorkflowGateDto?> EvaluateAsync(WorkflowInstance workflow, WorkflowStepInstance step, Document document, CancellationToken ct)
    {
        var receipt = db.WarehouseReceipts.Local.FirstOrDefault(r => r.DocumentId == document.Id)
            ?? await db.WarehouseReceipts.AsNoTracking().FirstOrDefaultAsync(r => r.DocumentId == document.Id, ct);
        if (receipt is null)
        {
            return null;
        }

        if (receipt.ValidationStatus == ReceiptValidationStatus.Invalid)
        {
            var errors = receipt.Issues.Where(i => i.Severity == ReceiptValidationStatus.Invalid).Select(i => i.Message).ToList();
            return new WorkflowGateDto(true, false, "RECEIPT_INVALID_BLOCKS_SIGNING",
                $"Phiếu không hợp lệ — không thể ký: {string.Join(" ", errors.Take(3))}{(errors.Count > 3 ? $" (và {errors.Count - 3} lỗi khác)" : string.Empty)} Trả lại để HIS gửi phiếu đã sửa.");
        }

        var supplierStep = workflow.Steps.Where(s => s.ActionType == WorkflowActionType.SupplierSign).Select(s => (int?)s.Order).Min();
        if (!receipt.HasInvoice && supplierStep is { } order && step.Order >= order)
        {
            return new WorkflowGateDto(true, false, "RECEIPT_INVOICE_REQUIRED",
                "Phiếu chưa có thông tin hóa đơn. Nhà cung cấp bổ sung hóa đơn (tải XML hóa đơn điện tử) trước khi ký.");
        }

        var warnings = receipt.Issues.Where(i => i.Severity == ReceiptValidationStatus.Warning && i.Code != "INVOICE_MISSING").Select(i => i.Message).ToList();
        return warnings.Count == 0
            ? null
            : new WorkflowGateDto(false, true, "RECEIPT_WARNING_REASON_REQUIRED",
                $"Phiếu có {warnings.Count} cảnh báo: {string.Join(" ", warnings.Take(3))}{(warnings.Count > 3 ? " …" : string.Empty)} Nhập lý do chấp nhận cảnh báo để ký.");
    }
}

/// <summary>Receipt status follows its workflow; completion and returns are announced to the purchasing staff.</summary>
internal sealed class ReceiptWorkflowObserver(IAppDbContext db, ContractTracking tracking, NotificationService notifications, IClock clock) : IWorkflowObserver
{
    public bool Handles(DocumentType type) => type == DocumentType.WarehouseReceipt;

    public async Task OnChangedAsync(WorkflowChange change, CancellationToken ct)
    {
        var receipt = db.WarehouseReceipts.Local.FirstOrDefault(r => r.DocumentId == change.Document.Id)
            ?? await db.WarehouseReceipts.Include(r => r.Lines).FirstOrDefaultAsync(r => r.DocumentId == change.Document.Id, ct);
        if (receipt is null)
        {
            return;
        }

        receipt.ApplyWorkflow(change.Workflow.Status, change.Kind == WorkflowChangeKind.Returned ? change.Note : null, clock.UtcNow);
        if (change.Kind is not (WorkflowChangeKind.Completed or WorkflowChangeKind.Returned) || change.Workflow.Status == WorkflowStatus.Running)
        {
            return;
        }

        var managers = await db.Users
            .Where(u => u.Status == UserStatus.Active && u.UserType == UserType.Internal
                && u.Roles.Any(r => r.Role!.Permissions.Any(p => p.PermissionCode == Permissions.ReceiptManage)))
            .Select(u => u.Id).ToListAsync(ct);
        if (change.Kind == WorkflowChangeKind.Completed)
        {
            await notifications.NotifyAsync(managers, NotificationTypes.ReceiptCompleted, $"{ReceiptProcessing.Title(receipt)} đã ký đủ",
                receipt.ContractNumber is null ? null : $"Hợp đồng {receipt.ContractNumber} — sẵn sàng nghiệm thu.", $"/nhap-kho/{receipt.Id}", email: false, ct);
            tracking.Defer(receipt.Lines.Where(l => l.ContractItemId is not null).Select(l => l.ContractItemId!.Value));
        }
        else
        {
            await notifications.NotifyAsync(managers, NotificationTypes.ReceiptReturned, $"{ReceiptProcessing.Title(receipt)} bị trả lại",
                change.Note, $"/nhap-kho/{receipt.Id}", email: true, ct);
        }
    }
}
