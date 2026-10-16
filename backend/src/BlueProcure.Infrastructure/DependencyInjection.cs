using BlueProcure.Application.Common;
using BlueProcure.Infrastructure.HealthChecks;
using BlueProcure.Infrastructure.Persistence;
using BlueProcure.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BlueProcure.Infrastructure;

public static class DependencyInjection
{
    public const string ReadyTag = "ready";
    public const string GotenbergClient = "gotenberg";
    public const string OnlyOfficeClient = "onlyoffice";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'Default' is not configured.");
        }

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<AuditableEntityInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>()));

        services.AddScoped<DatabaseInitializer>();

        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IFileStorage, MinioFileStorage>();

        services.Configure<ExternalServicesOptions>(configuration.GetSection(ExternalServicesOptions.SectionName));
        services.AddHttpClient(GotenbergClient, (sp, c) =>
        {
            c.BaseAddress = sp.GetRequiredService<IOptions<ExternalServicesOptions>>().Value.GotenbergUrl;
            c.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddHttpClient(OnlyOfficeClient, (sp, c) =>
        {
            c.BaseAddress = sp.GetRequiredService<IOptions<ExternalServicesOptions>>().Value.OnlyOfficeInternalUrl;
            c.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddHealthChecks()
            .AddNpgSql(connectionString, name: "postgres", tags: [ReadyTag])
            .AddCheck<StorageHealthCheck>("minio", tags: [ReadyTag])
            .Add(new HealthCheckRegistration(
                "gotenberg",
                sp => new HttpEndpointHealthCheck(sp.GetRequiredService<IHttpClientFactory>(), GotenbergClient, "/health"),
                HealthStatus.Unhealthy,
                [ReadyTag]));

        return services;
    }
}
