using BlueContract.Application.Common;
using BlueContract.Application.Notifications;
using BlueContract.Application.Proposals;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Payments;

/// <summary>Biên bản nghiệm thu follows its workflow; once signed, its quantities count as "đã nghiệm thu".</summary>
internal sealed class AcceptanceWorkflowObserver(IAppDbContext db, ContractTracking tracking, NotificationService notifications, IClock clock) : IWorkflowObserver
{
    public bool Handles(DocumentType type) => type == DocumentType.Acceptance;

    public async Task OnChangedAsync(WorkflowChange change, CancellationToken ct)
    {
        var acceptance = await db.AcceptanceRecords.Include(a => a.Items).FirstOrDefaultAsync(a => a.DocumentId == change.Document.Id, ct);
        if (acceptance is null)
        {
            return;
        }

        acceptance.ApplyWorkflow(change.Workflow.Status, change.Kind == WorkflowChangeKind.Returned ? change.Note : null, clock.UtcNow);
        if (change.Kind == WorkflowChangeKind.Completed)
        {
            tracking.Defer(acceptance.Items.Select(i => i.ContractItemId));
            await notifications.NotifyAsync(await PaymentRecipients.ManagersAsync(db, ct), NotificationTypes.PaymentCompleted,
                $"Biên bản nghiệm thu {acceptance.Number} đã ký xong", "Có thể lập biên bản xác định giá trị (08a).", $"/thanh-toan/nghiem-thu/{acceptance.Id}",
                email: false, ct);
        }
    }
}

/// <summary>08a: completion marks its acceptances "đã đưa vào thanh toán" (spec §6.5).</summary>
internal sealed class ValuationWorkflowObserver(IAppDbContext db, ContractTracking tracking, NotificationService notifications, IClock clock) : IWorkflowObserver
{
    public bool Handles(DocumentType type) => type is DocumentType.Valuation or DocumentType.PaymentRequest;

    public async Task OnChangedAsync(WorkflowChange change, CancellationToken ct)
    {
        var docId = change.Document.Id;
        var valuation = await db.ValuationRecords.Include(v => v.Items)
            .FirstOrDefaultAsync(v => v.DocumentId == docId || v.PaymentRequestDocumentId == docId, ct);
        if (valuation is null)
        {
            return;
        }

        if (change.Document.Type == DocumentType.PaymentRequest)
        {
            valuation.ApplyPaymentRequestWorkflow(change.Workflow.Status);
            return;
        }

        valuation.ApplyWorkflow(change.Workflow.Status, change.Kind == WorkflowChangeKind.Returned ? change.Note : null, clock.UtcNow);
        if (change.Kind != WorkflowChangeKind.Completed)
        {
            return;
        }

        foreach (var acceptance in await db.AcceptanceRecords.Where(a => a.ValuationId == valuation.Id).ToListAsync(ct))
        {
            acceptance.ConfirmInValuation(valuation.Id);
        }

        tracking.Defer(valuation.Items.Select(i => i.ContractItemId));
        await notifications.NotifyAsync(await PaymentRecipients.ManagersAsync(db, ct), NotificationTypes.PaymentCompleted,
            $"Biên bản xác định giá trị {valuation.Number} đã ký xong", "Đính kèm Ủy nhiệm chi khi chuyển tiền.", $"/thanh-toan/xac-dinh-gia-tri/{valuation.Id}",
            email: true, ct);
    }
}

/// <summary>Thanh lý ký xong: hợp đồng Đã thanh lý, các khoản chốt công nợ được tính là đã thanh toán.</summary>
internal sealed class LiquidationWorkflowObserver(IAppDbContext db, ContractTracking tracking, NotificationService notifications, IClock clock) : IWorkflowObserver
{
    public bool Handles(DocumentType type) => type == DocumentType.Liquidation;

    public async Task OnChangedAsync(WorkflowChange change, CancellationToken ct)
    {
        var liquidation = await db.LiquidationRecords.Include(l => l.Settlements).FirstOrDefaultAsync(l => l.DocumentId == change.Document.Id, ct);
        if (liquidation is null)
        {
            return;
        }

        liquidation.ApplyWorkflow(change.Workflow.Status, change.Kind == WorkflowChangeKind.Returned ? change.Note : null, clock.UtcNow);
        if (change.Kind != WorkflowChangeKind.Completed)
        {
            return;
        }

        var contract = await db.Contracts.Include(c => c.Items).FirstAsync(c => c.Id == liquidation.ContractId, ct);
        contract.Liquidate(clock.UtcNow);
        var settled = liquidation.Settlements.Where(s => s.ValuationId is not null).Select(s => s.ValuationId!.Value).Distinct().ToList();
        foreach (var valuation in await db.ValuationRecords.Where(v => settled.Contains(v.Id)).ToListAsync(ct))
        {
            valuation.MarkSettled(liquidation.Id);
        }

        tracking.Defer(contract.Items.Select(i => i.Id));

        var supplierUsers = await db.Users.Where(u => u.SupplierId == liquidation.SupplierId && u.Status == UserStatus.Active).Select(u => u.Id).ToListAsync(ct);
        await notifications.NotifyAsync([.. await PaymentRecipients.ManagersAsync(db, ct), .. supplierUsers], NotificationTypes.ContractLiquidated,
            $"Hợp đồng {contract.Number} đã thanh lý", $"Biên bản thanh lý {liquidation.Number} đã ký xong.", $"/hop-dong/{contract.Id}", email: true, ct);
    }
}

internal static class PaymentRecipients
{
    public static Task<List<Guid>> ManagersAsync(IAppDbContext db, CancellationToken ct) =>
        db.Users.Where(u => u.Status == UserStatus.Active && u.UserType == UserType.Internal
                && u.Roles.Any(r => r.Role!.Permissions.Any(p => p.PermissionCode == Permissions.PaymentManage)))
            .Select(u => u.Id).ToListAsync(ct);
}
