using System.Globalization;
using BlueContract.Application.Admin;
using BlueContract.Application.Auth;
using BlueContract.Application.Bids;
using BlueContract.Application.Catalog;
using BlueContract.Application.Contracts;
using BlueContract.Application.Documents;
using BlueContract.Application.Integration;
using BlueContract.Application.Notifications;
using BlueContract.Application.Payments;
using BlueContract.Application.Proposals;
using BlueContract.Application.Receipts;
using BlueContract.Application.Receipts.Validation;
using BlueContract.Application.Signing;
using BlueContract.Application.Workflows;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace BlueContract.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Built-in FluentValidation messages in Vietnamese.
        ValidatorOptions.Global.LanguageManager.Culture = new CultureInfo("vi");
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        services.AddScoped<AuthService>();
        services.AddScoped<OrganizationService>();
        services.AddScoped<FacilityService>();
        services.AddScoped<DepartmentService>();
        services.AddScoped<WarehouseService>();
        services.AddScoped<SupplierService>();
        services.AddScoped<SupplierCompanyService>();
        services.AddScoped<UserService>();
        services.AddScoped<RoleService>();
        services.AddScoped<DelegationService>();
        services.AddScoped<SettingService>();
        services.AddScoped<AuditLogService>();
        services.AddScoped<HistoryService>();
        services.AddScoped<LookupService>();
        services.AddScoped<TemplateService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<WorkflowAccess>();
        services.AddScoped<WorkflowService>();
        services.AddScoped<SigningService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<NotificationOutbox>();
        services.AddScoped<CatalogService>();
        services.AddScoped<ItemImportValidator>();
        services.AddScoped<BidDecisionService>();
        services.AddScoped<ContractDocumentData>();
        services.AddScoped<ContractService>();
        services.AddScoped<IWorkflowObserver, ContractWorkflowObserver>();
        services.AddScoped<IWorkflowObserver, AppendixWorkflowObserver>();
        services.AddScoped<PostCommitWork>();
        services.AddScoped<DocumentNumbers>();
        services.AddScoped<ContractTracking>();
        services.AddScoped<ProposalScope>();
        services.AddScoped<ProposalDocumentData>();
        services.AddScoped<ProposalService>();
        services.AddScoped<ProposalSummaryService>();
        services.AddScoped<CallOrderService>();
        services.AddScoped<IWorkflowObserver, ProposalWorkflowObserver>();
        services.AddScoped<IWorkflowObserver, ProposalSummaryWorkflowObserver>();
        services.AddSingleton(new ReceiptValidator(ReceiptValidator.DefaultRules));
        services.AddScoped<ReceiptValidationLoader>();
        services.AddScoped<ReceiptDocumentData>();
        services.AddScoped<ReceiptProcessing>();
        services.AddScoped<HisReceiptIngestion>();
        services.AddScoped<ReceiptService>();
        services.AddScoped<IWorkflowObserver, ReceiptWorkflowObserver>();
        services.AddScoped<IWorkflowGuard, ReceiptWorkflowGuard>();
        services.AddScoped<HisInboundService>();
        services.AddScoped<IntegrationLogWriter>();
        services.AddScoped<IntegrationLogService>();
        services.AddScoped<HisSimulator>();
        services.AddSingleton<IMockHisReceiptQueue, MockHisReceiptQueue>();
        services.AddScoped<BlueContract.Application.Archive.ArchiveService>();
        services.AddScoped<BlueContract.Application.Reports.DashboardService>();
        services.AddScoped<BlueContract.Application.Reports.ReportService>();
        services.AddScoped<PaymentDocumentData>();
        services.AddScoped<PaymentDocuments>();
        services.AddScoped<AcceptanceService>();
        services.AddScoped<ValuationService>();
        services.AddScoped<LiquidationService>();
        services.AddScoped<IWorkflowObserver, AcceptanceWorkflowObserver>();
        services.AddScoped<IWorkflowObserver, ValuationWorkflowObserver>();
        services.AddScoped<IWorkflowObserver, LiquidationWorkflowObserver>();
        return services;
    }
}
