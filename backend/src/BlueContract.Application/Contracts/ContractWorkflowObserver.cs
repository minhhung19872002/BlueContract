using BlueContract.Application.Common;
using BlueContract.Application.Notifications;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Contracts;

/// <summary>Keeps the contract status in step with its signing workflow; announces when the contract takes effect.</summary>
internal sealed class ContractWorkflowObserver(IAppDbContext db, NotificationService notifications, IAuditLogger audit, IClock clock) : IWorkflowObserver
{
    public bool Handles(DocumentType type) => type == DocumentType.Contract;

    public async Task OnChangedAsync(WorkflowChange change, CancellationToken ct)
    {
        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.DocumentId == change.Document.Id, ct);
        if (contract is null)
        {
            return;
        }

        var wf = change.Workflow;
        var wasEffective = contract.IsEffective;
        contract.ApplyWorkflow(wf.Status, wf.ActiveStep, change.Kind == WorkflowChangeKind.Returned ? change.Note : null, clock.UtcNow);

        if (contract.IsEffective && !wasEffective)
        {
            audit.Log("ContractEffective", nameof(Contract), contract.Id.ToString(), new { contract.Number, contract.TotalAmount });

            // Spec §6.1 B3-8: email the supplier and the parties involved once the clerk has sealed the contract.
            var supplierUsers = await db.Users.IgnoreQueryFilters()
                .Where(u => u.SupplierId == contract.SupplierId && u.Status == UserStatus.Active && !u.IsDeleted)
                .Select(u => u.Id).ToListAsync(ct);
            var participants = wf.Steps.Where(s => s.ActedBy != null).Select(s => s.ActedBy!.Value);
            await notifications.NotifyAsync(supplierUsers.Concat(participants).Append(wf.StartedBy), NotificationTypes.ContractEffective,
                $"Hợp đồng {contract.Number} đã có hiệu lực", contract.Name, $"/hop-dong/{contract.Id}", email: true, ct);
        }
    }
}

/// <summary>Applies an appendix to its contract once the appendix is fully signed.</summary>
internal sealed class AppendixWorkflowObserver(IAppDbContext db, NotificationService notifications, IAuditLogger audit, IClock clock) : IWorkflowObserver
{
    public bool Handles(DocumentType type) => type == DocumentType.Appendix;

    public async Task OnChangedAsync(WorkflowChange change, CancellationToken ct)
    {
        var appendix = await db.ContractAppendices.Include(a => a.Lines).FirstOrDefaultAsync(a => a.DocumentId == change.Document.Id, ct);
        if (appendix is null)
        {
            return;
        }

        appendix.ApplyWorkflow(change.Workflow.Status);
        if (change.Workflow.Status != WorkflowStatus.Completed)
        {
            return;
        }

        var contract = await db.Contracts.Include(c => c.Items).FirstAsync(c => c.Id == appendix.ContractId, ct);
        appendix.Apply(contract, clock.UtcNow);
        audit.Log("AppendixApplied", nameof(Contract), contract.Id.ToString(), new { appendix.Number, appendix.Type, appendix.ValueChange, contract.TotalAmount });
        await notifications.NotifyAsync([change.Workflow.StartedBy], NotificationTypes.ContractEffective,
            $"Phụ lục {appendix.Number} của hợp đồng {contract.Number} đã có hiệu lực", appendix.Reason, $"/hop-dong/{contract.Id}", email: true, ct);
    }
}
