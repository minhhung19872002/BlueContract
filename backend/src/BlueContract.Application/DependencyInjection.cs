using System.Globalization;
using BlueContract.Application.Admin;
using BlueContract.Application.Auth;
using BlueContract.Application.Documents;
using BlueContract.Application.Notifications;
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
        return services;
    }
}
