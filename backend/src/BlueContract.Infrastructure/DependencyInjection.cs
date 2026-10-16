using BlueContract.Application.Auth;
using BlueContract.Application.Common;
using BlueContract.Application.Documents;
using BlueContract.Application.Notifications;
using BlueContract.Application.Signing;
using BlueContract.Infrastructure.Documents;
using BlueContract.Infrastructure.Jobs;
using BlueContract.Infrastructure.Signing;
using Hangfire;
using Hangfire.PostgreSql;
using BlueContract.Infrastructure.HealthChecks;
using BlueContract.Infrastructure.Identity;
using BlueContract.Infrastructure.Messaging;
using BlueContract.Infrastructure.Persistence;
using BlueContract.Infrastructure.Persistence.Seeding;
using BlueContract.Infrastructure.Reporting;
using BlueContract.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BlueContract.Infrastructure;

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
        services.AddScoped<NotificationDispatchInterceptor>();

        // Factory (scoped, so interceptors/current user resolve per request) + scoped context from it.
        services.AddDbContextFactory<AppDbContext>(
            (sp, options) => options
                .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>(), sp.GetRequiredService<NotificationDispatchInterceptor>()),
            ServiceLifetime.Scoped);
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IAuditLogger, AuditLogger>();

        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<IdentitySeeder>();
        services.AddScoped<DemoSeeder>();
        services.AddScoped<TemplateSeeder>();
        services.AddScoped<WorkflowSeeder>();

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

        // Digital signing (PAdES via iText) and providers
        services.Configure<SigningOptions>(configuration.GetSection(SigningOptions.SectionName));
        services.AddSingleton(sp =>
        {
            var signing = sp.GetRequiredService<IOptions<SigningOptions>>().Value;
            return new SigningDefaults(signing.DefaultProvider, signing.InitialRequiresCa);
        });
        services.AddSingleton<IPdfSignatureService, PdfSignatureService>();
        services.AddSingleton<ICmsBuilder, CmsBuilder>();
        services.AddDistributedMemoryCache();
        services.AddScoped<CertificateStore>();
        services.AddScoped<ISigningProvider, MockSigningProvider>();
        services.AddScoped<ISigningProvider, MockRemoteSigningProvider>();
        services.AddScoped<ISigningProvider, UsbTokenSigningProvider>();
        services.AddScoped<ISigningProvider, InitialStampProvider>();
        services.AddScoped<ISigningProvider>(sp => new RemoteCaSigningProvider("VNPT_SMARTCA", "VNPT SmartCA (ký số từ xa)", o => o.VnptSmartCa, sp.GetRequiredService<IOptions<SigningOptions>>()));
        services.AddScoped<ISigningProvider>(sp => new RemoteCaSigningProvider("VIETTEL_MYSIGN", "Viettel MySign (ký số từ xa)", o => o.ViettelMySign, sp.GetRequiredService<IOptions<SigningOptions>>()));
        services.AddScoped<ISigningProvider>(sp => new RemoteCaSigningProvider("FPT_CA", "FPT CA (ký số từ xa)", o => o.FptCa, sp.GetRequiredService<IOptions<SigningOptions>>()));

        // Background jobs (Hangfire on PostgreSQL)
        services.Configure<HangfireOptions>(configuration.GetSection(HangfireOptions.SectionName));
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(connectionString), new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));
        services.AddScoped<IBackgroundJobs, HangfireBackgroundJobs>();
        services.AddScoped<EmailJob>();
        services.AddScoped<WorkflowReminderJob>();

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
