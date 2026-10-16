using System.Linq.Expressions;
using BlueContract.Application.Common;
using BlueContract.Domain.Bids;
using BlueContract.Domain.Catalog;
using BlueContract.Domain.Common;
using BlueContract.Domain.Contracts;
using BlueContract.Domain.Documents;
using BlueContract.Domain.Identity;
using BlueContract.Domain.Integration;
using BlueContract.Domain.Organizations;
using BlueContract.Domain.Proposals;
using BlueContract.Domain.Receipts;
using BlueContract.Domain.Settings;
using BlueContract.Domain.Workflows;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext, IAppDbContext, IDataProtectionKeyContext
{
    public const string SoftDeleteFilter = "SoftDelete";
    public const string SupplierIsolationFilter = "SupplierIsolation";

    private readonly ICurrentUser? _currentUser;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser? currentUser = null)
        : base(options)
    {
        _currentUser = currentUser;
    }

    /// <summary>True when the caller is a supplier user; evaluated per query by the isolation filter.</summary>
    public bool IsSupplierCaller => _currentUser?.IsSupplier == true;

    /// <summary>The caller's supplier id (Guid.Empty for internal callers, which never matches).</summary>
    public Guid CallerSupplierId => _currentUser?.SupplierId ?? Guid.Empty;

    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Facility> Facilities => Set<Facility>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<UserDataScope> UserDataScopes => Set<UserDataScope>();

    public DbSet<SigningDelegation> SigningDelegations => Set<SigningDelegation>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<DepartmentHistory> DepartmentHistories => Set<DepartmentHistory>();

    public DbSet<UserHistory> UserHistories => Set<UserHistory>();

    public DbSet<SupplierHistory> SupplierHistories => Set<SupplierHistory>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();

    public DbSet<DocumentEditSession> DocumentEditSessions => Set<DocumentEditSession>();

    public DbSet<WorkflowTemplate> WorkflowTemplates => Set<WorkflowTemplate>();

    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();

    public DbSet<WorkflowStepInstance> WorkflowSteps => Set<WorkflowStepInstance>();

    public DbSet<WorkflowEvent> WorkflowEvents => Set<WorkflowEvent>();

    public DbSet<Signature> Signatures => Set<Signature>();

    public DbSet<SigningRequest> SigningRequests => Set<SigningRequest>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<SigningCertificate> SigningCertificates => Set<SigningCertificate>();

    public DbSet<ItemCategory> ItemCategories => Set<ItemCategory>();

    public DbSet<Item> Items => Set<Item>();

    public DbSet<BidDecision> BidDecisions => Set<BidDecision>();

    public DbSet<Contract> Contracts => Set<Contract>();

    public DbSet<ContractAppendix> ContractAppendices => Set<ContractAppendix>();

    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();

    public DbSet<Proposal> Proposals => Set<Proposal>();

    public DbSet<ProposalSummary> ProposalSummaries => Set<ProposalSummary>();

    public DbSet<CallOrder> CallOrders => Set<CallOrder>();

    public DbSet<WarehouseReceipt> WarehouseReceipts => Set<WarehouseReceipt>();

    public DbSet<IntegrationLog> IntegrationLogs => Set<IntegrationLog>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            var clr = entityType.ClrType;
            if (!typeof(Entity).IsAssignableFrom(clr))
            {
                continue;
            }

            var builder = modelBuilder.Entity(clr);
            builder.Property(nameof(Entity.Id)).ValueGeneratedNever();

            // PostgreSQL xmin system column as optimistic concurrency token.
            builder.Property(nameof(Entity.RowVersion)).IsRowVersion();

            var parameter = Expression.Parameter(clr, "e");
            builder.HasQueryFilter(
                SoftDeleteFilter,
                Expression.Lambda(Expression.Not(Expression.Property(parameter, nameof(Entity.IsDeleted))), parameter));

            if (typeof(ISupplierScoped).IsAssignableFrom(clr))
            {
                builder.HasQueryFilter(SupplierIsolationFilter, BuildSupplierFilter(clr, nameof(ISupplierScoped.SupplierId)));
            }
        }

        // NOTE: IgnoreQueryFilters() switches the filters off for the WHOLE query, including the root of a projection.
        // Never use it inside a subquery of a supplier-scoped query (ADR-031); load such data in a separate query.

        // Master data with special ownership rules.
        modelBuilder.Entity<Supplier>().HasQueryFilter(SupplierIsolationFilter, s => !IsSupplierCaller || s.Id == CallerSupplierId);
        modelBuilder.Entity<User>().HasQueryFilter(SupplierIsolationFilter, u => !IsSupplierCaller || u.SupplierId == CallerSupplierId);
        modelBuilder.Entity<Document>().HasQueryFilter(SupplierIsolationFilter, d => !IsSupplierCaller || d.SupplierId == CallerSupplierId);
        modelBuilder.Entity<WorkflowInstance>().HasQueryFilter(SupplierIsolationFilter, w => !IsSupplierCaller || w.SupplierId == CallerSupplierId);
        modelBuilder.Entity<WarehouseReceipt>().HasQueryFilter(SupplierIsolationFilter, r => !IsSupplierCaller || r.SupplierId == CallerSupplierId);

        // Join rows follow the soft-delete state of their required principal.
        modelBuilder.Entity<UserRole>().HasQueryFilter(SoftDeleteFilter, ur => !ur.Role!.IsDeleted);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
    }

    private LambdaExpression BuildSupplierFilter(Type clr, string supplierProperty)
    {
        // e => !this.IsSupplierCaller || e.SupplierId == this.CallerSupplierId
        var parameter = Expression.Parameter(clr, "e");
        var context = Expression.Constant(this);
        var body = Expression.OrElse(
            Expression.Not(Expression.Property(context, nameof(IsSupplierCaller))),
            Expression.Equal(Expression.Property(parameter, supplierProperty), Expression.Property(context, nameof(CallerSupplierId))));
        return Expression.Lambda(body, parameter);
    }
}
