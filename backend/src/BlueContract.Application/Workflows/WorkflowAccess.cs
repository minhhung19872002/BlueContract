using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Domain.Documents;
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
public sealed class WorkflowAccess(IAppDbContext db, ICurrentUser current, IClock clock, DataScope dataScope)
{
    private List<string>? _myRoles;
    private readonly Dictionary<Guid, DocumentType> _documentTypes = [];
    private readonly Dictionary<string, HashSet<string>> _rolePermissions = [];
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

        var required = DocumentPermissions.Act(await DocumentTypeAsync(wf.DocumentId, ct), step.ActionType);
        var holdsRequired = required.Length == 0 || current.Permissions.Overlaps(required);
        switch (step.AssigneeType)
        {
            case AssigneeType.User:
                return step.AssigneeUserId != me ? Deny("WORKFLOW_NOT_ASSIGNEE") : holdsRequired ? Allow() : Deny("WORKFLOW_PERMISSION_MISSING");
            case AssigneeType.Creator:
                return wf.StartedBy != me ? Deny("WORKFLOW_NOT_ASSIGNEE") : holdsRequired ? Allow() : Deny("WORKFLOW_PERMISSION_MISSING");
            case AssigneeType.DocumentSupplier:
                return !(current.IsSupplier && wf.SupplierId is not null && current.SupplierId == wf.SupplierId) ? Deny("WORKFLOW_NOT_ASSIGNEE")
                    : holdsRequired ? Allow() : Deny("WORKFLOW_PERMISSION_MISSING");
        }

        var roles = await MyRolesAsync(ct);
        var holdsRole = roles.Contains(step.RoleCode!);
        if (holdsRole && DataScope.Covers(await dataScope.MineAsync(ct), wf.ScopeFacilityId, wf.ScopeDomain))
        {
            return holdsRequired ? Allow() : Deny("WORKFLOW_PERMISSION_MISSING");
        }

        // The e-seal is never delegated: only a clerk may apply the hospital's seal (spec §8.2).
        if (step.ActionType == WorkflowActionType.OrgSeal)
        {
            return Deny(holdsRole ? "WORKFLOW_OUT_OF_SCOPE" : "WORKFLOW_NOT_ASSIGNEE");
        }

        // A deputy acts with the delegator's authority and within the delegator's data scope (AUDIT-1 §10.7).
        foreach (var delegation in (await MyDelegationsAsync(ct)).Where(d => d.Roles.Contains(step.RoleCode!)))
        {
            if (!DataScope.Covers(await dataScope.OfUserAsync(delegation.Delegator, ct), wf.ScopeFacilityId, wf.ScopeDomain))
            {
                continue;
            }

            // The delegator's roles must hold the permission.
            if (required.Length > 0 && !(await PermissionsOfAsync(delegation.Roles, ct)).Overlaps(required))
            {
                return Deny("WORKFLOW_PERMISSION_MISSING");
            }

            return new ActorStanding(true, delegation.Delegator, $"KT. {delegation.DelegatorTitle.ToUpperInvariant()}", delegation.DelegateTitle, null);
        }

        return Deny(holdsRole ? "WORKFLOW_OUT_OF_SCOPE" : "WORKFLOW_NOT_ASSIGNEE");
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

    private async Task<DocumentType> DocumentTypeAsync(Guid documentId, CancellationToken ct)
    {
        if (!_documentTypes.TryGetValue(documentId, out var type))
        {
            type = await db.Documents.IgnoreQueryFilters().Where(d => d.Id == documentId).Select(d => d.Type).FirstAsync(ct);
            _documentTypes[documentId] = type;
        }

        return type;
    }

    private async Task<HashSet<string>> PermissionsOfAsync(List<string> roleCodes, CancellationToken ct)
    {
        var key = string.Join(',', roleCodes.Order(StringComparer.Ordinal));
        if (!_rolePermissions.TryGetValue(key, out var permissions))
        {
            permissions = [.. await db.Roles.Where(r => roleCodes.Contains(r.Code)).SelectMany(r => r.Permissions.Select(p => p.PermissionCode)).ToListAsync(ct)];
            _rolePermissions[key] = permissions;
        }

        return permissions;
    }

    private static ActorStanding Allow() => new(true, null, null, null, null);

    private static ActorStanding Deny(string code) => new(false, null, null, null, code);
}
