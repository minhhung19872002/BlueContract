using BlueContract.Application.Common;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Workflows;

/// <summary>The current user's standing on a step: may act, and whether on behalf of a delegator.</summary>
public sealed record ActorStanding(bool CanAct, Guid? OnBehalfOf, string? Caption, string? Title, string? DenyReason);

/// <summary>
/// Resolves who may act on a workflow step (spec §4.2, §5.7): role + data scope, specific user, the document's supplier,
/// or the creator; signing delegation lets a deputy act for the director ("KT. GIÁM ĐỐC").
/// </summary>
public sealed class WorkflowAccess(IAppDbContext db, ICurrentUser current, IClock clock)
{
    private List<string>? _myRoles;
    private List<(Guid Delegator, string DelegatorTitle, string DelegateTitle, List<string> Roles)>? _delegations;

    public async Task<IReadOnlyList<string>> MyRolesAsync(CancellationToken ct) =>
        _myRoles ??= current.UserId is { } me
            ? await db.UserRoles.Where(ur => ur.UserId == me).Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Code).ToListAsync(ct)
            : [];

    /// <summary>Delegations currently in force where I am the delegate, with the delegator's roles.</summary>
    public async Task<IReadOnlyList<(Guid Delegator, string DelegatorTitle, string DelegateTitle, List<string> Roles)>> MyDelegationsAsync(CancellationToken ct)
    {
        if (_delegations is not null)
        {
            return _delegations;
        }

        if (current.UserId is not { } me || current.IsSupplier)
        {
            return _delegations = [];
        }

        var now = clock.UtcNow;
        var rows = await db.SigningDelegations.AsNoTracking()
            .Where(d => d.DelegateUserId == me && d.RevokedAt == null && d.ValidFrom <= now && d.ValidTo > now)
            .Select(d => new { d.DelegatorUserId, d.DelegatorTitle, d.DelegateTitle })
            .ToListAsync(ct);
        var result = new List<(Guid, string, string, List<string>)>();
        foreach (var r in rows)
        {
            var roles = await db.UserRoles.Where(ur => ur.UserId == r.DelegatorUserId)
                .Join(db.Roles, ur => ur.RoleId, x => x.Id, (_, x) => x.Code).ToListAsync(ct);
            result.Add((r.DelegatorUserId, r.DelegatorTitle, r.DelegateTitle, roles));
        }

        return _delegations = result;
    }

    public async Task<ActorStanding> StandingAsync(WorkflowInstance wf, WorkflowStepInstance step, CancellationToken ct)
    {
        if (current.UserId is not { } me)
        {
            return Deny("UNAUTHORIZED");
        }

        if (!current.HasPermission(Permissions.SigningSign))
        {
            return Deny("FORBIDDEN");
        }

        switch (step.AssigneeType)
        {
            case AssigneeType.User:
                return step.AssigneeUserId == me ? Allow() : Deny("WORKFLOW_NOT_ASSIGNEE");
            case AssigneeType.Creator:
                return wf.StartedBy == me ? Allow() : Deny("WORKFLOW_NOT_ASSIGNEE");
            case AssigneeType.DocumentSupplier:
                return current.IsSupplier && wf.SupplierId is not null && current.SupplierId == wf.SupplierId ? Allow() : Deny("WORKFLOW_NOT_ASSIGNEE");
        }

        if (!InScope(wf))
        {
            return Deny("WORKFLOW_OUT_OF_SCOPE");
        }

        var roles = await MyRolesAsync(ct);
        if (roles.Contains(step.RoleCode!))
        {
            return Allow();
        }

        // The e-seal is never delegated: only a clerk may apply the hospital's seal (spec §8.2).
        if (step.ActionType == WorkflowActionType.OrgSeal)
        {
            return Deny("WORKFLOW_NOT_ASSIGNEE");
        }

        var delegation = (await MyDelegationsAsync(ct)).FirstOrDefault(d => d.Roles.Contains(step.RoleCode!));
        if (delegation.Delegator != Guid.Empty)
        {
            return new ActorStanding(true, delegation.Delegator, $"KT. {delegation.DelegatorTitle.ToUpperInvariant()}", delegation.DelegateTitle, null);
        }

        return Deny("WORKFLOW_NOT_ASSIGNEE");
    }

    /// <summary>Users to notify when a step becomes active.</summary>
    public async Task<List<Guid>> AssigneesAsync(WorkflowInstance wf, WorkflowStepInstance step, CancellationToken ct)
    {
        IQueryable<User> users = db.Users.IgnoreQueryFilters().Where(u => !u.IsDeleted && u.Status == UserStatus.Active);
        switch (step.AssigneeType)
        {
            case AssigneeType.User:
                return step.AssigneeUserId is { } uid ? [uid] : [];
            case AssigneeType.Creator:
                return [wf.StartedBy];
            case AssigneeType.DocumentSupplier:
                return wf.SupplierId is { } sid ? await users.Where(u => u.SupplierId == sid).Select(u => u.Id).ToListAsync(ct) : [];
        }

        var holders = await users.Where(u => u.UserType == UserType.Internal && u.Roles.Any(r => r.Role!.Code == step.RoleCode))
            .Select(u => new { u.Id, Scopes = u.DataScopes.Select(s => new { s.ScopeType, s.Value }).ToList() })
            .ToListAsync(ct);
        return holders.Where(h =>
                (wf.ScopeFacilityId is null || !h.Scopes.Any(s => s.ScopeType == DataScopeType.Facility) || h.Scopes.Any(s => s.ScopeType == DataScopeType.Facility && s.Value == wf.ScopeFacilityId.ToString()))
                && (wf.ScopeDomain is null || !h.Scopes.Any(s => s.ScopeType == DataScopeType.Domain) || h.Scopes.Any(s => s.ScopeType == DataScopeType.Domain && s.Value == wf.ScopeDomain)))
            .Select(h => h.Id).ToList();
    }

    /// <summary>True when the current user started, acted in, or can act now in a workflow over the document.</summary>
    public async Task<bool> IsParticipantAsync(Guid documentId, CancellationToken ct)
    {
        if (current.UserId is not { } me)
        {
            return false;
        }

        var instances = await db.WorkflowInstances.Include("_steps").Where(w => w.DocumentId == documentId).ToListAsync(ct);
        foreach (var wf in instances)
        {
            if (wf.StartedBy == me || wf.Steps.Any(s => s.ActedBy == me))
            {
                return true;
            }

            if (wf.ActiveStep is { } active && (await StandingAsync(wf, active, ct)).CanAct)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Data-scope rule for role steps: a user with facility/domain scopes only handles matching workflows.</summary>
    public bool InScope(WorkflowInstance wf)
    {
        var scopes = current.DataScopes;
        var facilities = scopes.Where(s => s.Type == DataScopeType.Facility).Select(s => s.Value).ToList();
        var domains = scopes.Where(s => s.Type == DataScopeType.Domain).Select(s => s.Value).ToList();
        return (wf.ScopeFacilityId is null || facilities.Count == 0 || facilities.Contains(wf.ScopeFacilityId.Value.ToString()))
            && (wf.ScopeDomain is null || domains.Count == 0 || domains.Contains(wf.ScopeDomain));
    }

    private static ActorStanding Allow() => new(true, null, null, null, null);

    private static ActorStanding Deny(string code) => new(false, null, null, null, code);
}
