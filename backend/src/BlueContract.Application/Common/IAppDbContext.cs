using BlueContract.Domain.Bids;
using BlueContract.Domain.Catalog;
using BlueContract.Domain.Common;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Integration;
using BlueContract.Domain.Organizations;
using BlueContract.Domain.Payments;
using BlueContract.Domain.Proposals;
using BlueContract.Domain.Receipts;
using BlueContract.Domain.Settings;
using BlueContract.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Application.Common;

/// <summary>Unit of work over the application database. Use cases query through these sets (with projections).</summary>
public interface IAppDbContext
{
    DbSet<SystemSetting> SystemSettings { get; }

    DbSet<EmailTemplate> EmailTemplates { get; }

    DbSet<Organization> Organizations { get; }

    DbSet<Facility> Facilities { get; }

    DbSet<Department> Departments { get; }

    DbSet<Warehouse> Warehouses { get; }

    DbSet<Supplier> Suppliers { get; }

    DbSet<User> Users { get; }

    DbSet<Role> Roles { get; }

    DbSet<UserRole> UserRoles { get; }

    DbSet<RolePermission> RolePermissions { get; }

    DbSet<UserDataScope> UserDataScopes { get; }

    DbSet<SigningDelegation> SigningDelegations { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<AuditLog> AuditLogs { get; }

    DbSet<DepartmentHistory> DepartmentHistories { get; }

    DbSet<UserHistory> UserHistories { get; }

    DbSet<SupplierHistory> SupplierHistories { get; }

    DbSet<Document> Documents { get; }

    DbSet<DocumentVersion> DocumentVersions { get; }

    DbSet<DocumentTemplate> DocumentTemplates { get; }

    DbSet<DocumentEditSession> DocumentEditSessions { get; }

    DbSet<WorkflowTemplate> WorkflowTemplates { get; }

    DbSet<WorkflowInstance> WorkflowInstances { get; }

    DbSet<WorkflowStepInstance> WorkflowSteps { get; }

    DbSet<WorkflowEvent> WorkflowEvents { get; }

    DbSet<Signature> Signatures { get; }

    DbSet<SigningRequest> SigningRequests { get; }

    DbSet<Notification> Notifications { get; }

    DbSet<SigningCertificate> SigningCertificates { get; }

    DbSet<ItemCategory> ItemCategories { get; }

    DbSet<Item> Items { get; }

    DbSet<BidDecision> BidDecisions { get; }

    DbSet<Contract> Contracts { get; }

    DbSet<ContractAppendix> ContractAppendices { get; }

    DbSet<NumberSequence> NumberSequences { get; }

    DbSet<Proposal> Proposals { get; }

    DbSet<ProposalSummary> ProposalSummaries { get; }

    DbSet<CallOrder> CallOrders { get; }

    DbSet<WarehouseReceipt> WarehouseReceipts { get; }

    DbSet<IntegrationLog> IntegrationLogs { get; }

    DbSet<AcceptanceRecord> AcceptanceRecords { get; }

    DbSet<ValuationRecord> ValuationRecords { get; }

    DbSet<LiquidationRecord> LiquidationRecords { get; }

    DbSet<IntegrityCheckRun> IntegrityCheckRuns { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="work"/> in one transaction holding a database lock per key (PostgreSQL advisory locks), so
    /// concurrent requests over the same keys run one after another. Read the guarded rows inside <paramref name="work"/>
    /// (after the lock), never before. Notifications go out only after the commit.
    /// </summary>
    Task<T> SerializedAsync<T>(IEnumerable<string> lockKeys, Func<Task<T>> work, CancellationToken ct);

    /// <summary>
    /// Next value of a per-prefix, per-year document counter, allocated atomically. Outside a transaction the value is
    /// committed at once, so a request that fails afterwards leaves a gap in the numbering (never a duplicate).
    /// </summary>
    Task<int> NextNumberAsync(string prefix, int year, CancellationToken ct);

    /// <summary>
    /// Records a request signature atomically: true the first time, false for a replay — whichever API instance saw it
    /// first. Signatures older than <paramref name="keepFor"/> are purged.
    /// </summary>
    Task<bool> TryUseRequestSignatureAsync(string signature, DateTimeOffset now, TimeSpan keepFor, CancellationToken ct);
}
