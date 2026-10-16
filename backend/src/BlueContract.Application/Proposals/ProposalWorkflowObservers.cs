using BlueContract.Application.Common;
using BlueContract.Application.Notifications;
using BlueContract.Application.Workflows;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Proposals;
using BlueContract.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Proposals;

/// <summary>Proposal signed by its creator → submitted (counts as "đã đề xuất" on the contract items).</summary>
internal sealed class ProposalWorkflowObserver(IAppDbContext db, ContractTracking tracking, NotificationService notifications, IClock clock) : IWorkflowObserver
{
    public bool Handles(DocumentType type) => type == DocumentType.Proposal;

    public async Task OnChangedAsync(WorkflowChange change, CancellationToken ct)
    {
        var proposal = await db.Proposals.Include(p => p.Lines).FirstOrDefaultAsync(p => p.DocumentId == change.Document.Id, ct);
        if (proposal is null)
        {
            return;
        }

        proposal.ApplyWorkflow(change.Workflow.Status, clock.UtcNow);
        if (proposal.Status != ProposalStatus.Submitted)
        {
            return;
        }

        // Ask the aggregators of the domain to merge it.
        var domain = proposal.Domain.ToString();
        var aggregators = await db.Users
            .Where(u => u.Status == UserStatus.Active && u.UserType == UserType.Internal
                && u.Roles.Any(r => r.Role!.Code == RoleCatalog.Aggregator)
                && (!u.DataScopes.Any(s => s.ScopeType == DataScopeType.Domain) || u.DataScopes.Any(s => s.ScopeType == DataScopeType.Domain && s.Value == domain)))
            .Select(u => u.Id).ToListAsync(ct);
        await notifications.NotifyAsync(aggregators, NotificationTypes.ProposalSubmitted, $"Phiếu đề xuất mới {proposal.Number}",
            $"{ProposalScope.DomainText(proposal.Domain)} — {proposal.Lines.Count} mặt hàng", "/tong-hop-de-xuat/tao-moi", email: false, ct);
        tracking.Defer(proposal.Lines.Select(l => l.ContractItemId));
    }
}

/// <summary>Summary status follows its workflow; once the director signs, approved quantities count on the contract items.</summary>
internal sealed class ProposalSummaryWorkflowObserver(IAppDbContext db, ContractTracking tracking, NotificationService notifications, IClock clock) : IWorkflowObserver
{
    public bool Handles(DocumentType type) => type == DocumentType.ProposalSummary;

    public async Task OnChangedAsync(WorkflowChange change, CancellationToken ct)
    {
        var summary = await db.ProposalSummaries.Include(s => s.Lines).FirstOrDefaultAsync(s => s.DocumentId == change.Document.Id, ct);
        if (summary is null)
        {
            return;
        }

        var wasApproved = summary.Status == ProposalSummaryStatus.Approved;
        summary.ApplyWorkflow(change.Workflow.Status, change.Workflow.ActiveStep, clock.UtcNow);
        if (summary.Status != ProposalSummaryStatus.Approved || wasApproved)
        {
            return;
        }

        var buyers = await db.Users
            .Where(u => u.Status == UserStatus.Active && u.UserType == UserType.Internal
                && u.Roles.Any(r => r.Role!.Permissions.Any(p => p.PermissionCode == Permissions.CallOrderCreate)))
            .Select(u => u.Id).ToListAsync(ct);
        await notifications.NotifyAsync(buyers.Append(summary.AggregatorId), NotificationTypes.SummaryApproved,
            $"Tổng hợp đề xuất {summary.Number} đã được Giám đốc ký duyệt", "Có thể tạo lần gọi hàng.", $"/goi-hang/tao-moi?summary={summary.Id}", email: true, ct);
        tracking.Defer(summary.Lines.Select(l => l.ContractItemId));
    }
}
