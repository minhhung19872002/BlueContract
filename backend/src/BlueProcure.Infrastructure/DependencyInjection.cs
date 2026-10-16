using BlueProcure.Application.Auth;
using BlueProcure.Application.Common;
using BlueProcure.Application.Documents;
using BlueProcure.Infrastructure.Documents;
using BlueProcure.Infrastructure.HealthChecks;
using BlueProcure.Infrastructure.Identity;
using BlueProcure.Infrastructure.Messaging;
using BlueProcure.Infrastructure.Persistence;
using BlueProcure.Infrastructure.Persistence.Seeding;
using BlueProcure.Infrastructure.Reporting;
using BlueProcure.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
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

        // Factory (scoped, so interceptors/current user resolve per request) + scoped context from it.
        services.AddDbContextFactory<AppDbContext>(
            (sp, options) => options
                .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>()),
            ServiceLifetime.Scoped);
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IAuditLogger, AuditLogger>();

        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<IdentitySeeder>();
        services.AddScoped<DemoSeeder>();
        services.AddScoped<TemplateSeeder>();

        // Identity
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddSingleton<IUrlSigner, DataProtectionUrlSigner>();
        var dataProtection = services.AddDataProtection().SetApplicationName("BlueProcure");

        // Keys live in PostgreSQL so every API instance shares them; skipped for the build-time OpenAPI generator (no DB).
        if (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name != "GetDocument.Insider")
        {
            dataProtection.PersistKeysToDbContext<AppDbContext>();
        }

        // Messaging & links
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<AppOptions>(configuration.GetSection(AppOptions.SectionName));
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<IAppLinkBuilder, AppLinkBuilder>();
        services.AddSingleton<IExcelExporter, ClosedXmlExcelExporter>();

        // Documents: template engine, PDF conversion, online editor
        services.AddSingleton<IDocumentGenerator, DocxTemplateEngine>();
        services.AddScoped<IPdfConverter, GotenbergPdfConverter>();
        services.Configure<OnlyOfficeOptions>(configuration.GetSection(OnlyOfficeOptions.SectionName));
        services.AddScoped<IDocumentEditorService, OnlyOfficeEditorService>();

        // Storage
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IFileStorage, S3FileStorage>();

        services.Configure<ExternalServicesOptions>(configuration.GetSection(ExternalServicesOptions.SectionName));
        services.AddHttpClient(GotenbergClient, (sp, c) =>
        {
            c.BaseAddress = sp.GetRequiredService<IOptions<ExternalServicesOptions>>().Value.GotenbergUrl;
            c.Timeout = TimeSpan.FromSeconds(180);
        });
        services.AddHttpClient(OnlyOfficeClient, (sp, c) =>
        {
            c.BaseAddress = sp.GetRequiredService<IOptions<OnlyOfficeOptions>>().Value.InternalUrl;
            c.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddHealthChecks()
            .AddNpgSql(connectionString, name: "postgres", tags: [ReadyTag])
            .AddCheck<StorageHealthCheck>("storage", tags: [ReadyTag])
            .Add(new HealthCheckRegistration(
                "gotenberg",
                sp => new HttpEndpointHealthCheck(sp.GetRequiredService<IHttpClientFactory>(), GotenbergClient, "/health"),
                HealthStatus.Unhealthy,
                [ReadyTag]));

        return services;
    }
}
