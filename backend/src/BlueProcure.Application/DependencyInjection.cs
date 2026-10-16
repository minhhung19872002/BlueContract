using System.Globalization;
using BlueProcure.Application.Admin;
using BlueProcure.Application.Auth;
using BlueProcure.Application.Documents;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace BlueProcure.Application;

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
        return services;
    }
}
