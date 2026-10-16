using System.Text.Json.Serialization;
using BlueContract.Api.Auth;
using BlueContract.Api.Middleware;
using BlueContract.Application;
using BlueContract.Infrastructure;
using BlueContract.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;
using Hangfire;
using Serilog;
using InfrastructureDI = BlueContract.Infrastructure.DependencyInjection;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddControllers(options =>
        {
            options.Filters.Add<ValidationFilter>();
            options.Filters.Add<PasswordChangeRequiredFilter>();
        })
        .AddJsonOptions(o =>
        {
            o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });

    // OpenAPI schema generation reads the HTTP JSON options: keep enums as strings there too.
    builder.Services.ConfigureHttpJsonOptions(o =>
    {
        o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());

        // Numbers are numbers: otherwise the OpenAPI schema types integers as "number | string".
        o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    });
    builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(o => o.SuppressModelStateInvalidFilter = false);
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        // X-Forwarded-* is believed only from the reverse proxy (AUDIT-1 §10.10): loopback by default, plus the proxy
        // addresses / networks configured in "ForwardedHeaders" (docker compose: the network of the web container).
        // Anyone else could otherwise spoof the HIS's IP past the whitelist or dodge the per-IP rate limits.
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        var section = builder.Configuration.GetSection("ForwardedHeaders");
        foreach (var network in section.GetSection("KnownNetworks").Get<string[]>() ?? [])
        {
            o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }

        foreach (var proxy in section.GetSection("KnownProxies").Get<string[]>() ?? [])
        {
            o.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
        }
    });
    builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "BlueProcure API";
        document.Info.Description = "Cổng Quản lý & Thanh toán Mua sắm Hàng hóa Trực tuyến";
        document.Components ??= new Microsoft.OpenApi.OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, Microsoft.OpenApi.IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new Microsoft.OpenApi.OpenApiSecurityScheme
        {
            Type = Microsoft.OpenApi.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
        };
        return Task.CompletedTask;
    }));
    builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
        ctx.ProblemDetails.Extensions["correlationId"] = ctx.HttpContext.TraceIdentifier);
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddAppAuth(builder.Configuration);

    // Realtime notifications (SignalR) and background job server (Hangfire)
    builder.Services.AddSignalR();
    builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, BlueContract.Api.Realtime.SubjectUserIdProvider>();
    builder.Services.AddScoped<BlueContract.Application.Notifications.IRealtimeNotifier, BlueContract.Api.Realtime.SignalRNotifier>();
    var hangfire = builder.Configuration.GetSection(BlueContract.Infrastructure.Jobs.HangfireOptions.SectionName).Get<BlueContract.Infrastructure.Jobs.HangfireOptions>()
        ?? new BlueContract.Infrastructure.Jobs.HangfireOptions();
    if (hangfire.ServerEnabled && System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name != "GetDocument.Insider")
    {
        builder.Services.AddHangfireServer(o => o.WorkerCount = Math.Max(2, Environment.ProcessorCount));
    }
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

    var app = builder.Build();

    // Skip bootstrap when the build-time OpenAPI generator (GetDocument.Insider) loads the app.
    var isOpenApiGeneration = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    // `dotnet BlueContract.Api.dll --export-templates <dir>`: write the built-in DOCX templates and exit.
    var exportIndex = Array.FindIndex(args, a => a.Equals("--export-templates", StringComparison.OrdinalIgnoreCase));
    if (exportIndex >= 0)
    {
        var target = exportIndex + 1 < args.Length ? args[exportIndex + 1] : "templates";
        foreach (var path in BlueContract.Infrastructure.Persistence.Seeding.TemplateSeeder.Export(target))
        {
            Log.Information("Exported {Path}", path);
        }

        return;
    }

    if (!isOpenApiGeneration && System.Text.Encoding.UTF8.GetByteCount(app.Configuration["Auth:SigningKey"] ?? string.Empty) < 32)
    {
        throw new InvalidOperationException("Auth:SigningKey must be configured with at least 32 bytes (env Auth__SigningKey).");
    }

    var seedOnly = args.Contains("--seed-only", StringComparer.OrdinalIgnoreCase);
    if (!isOpenApiGeneration && (seedOnly || app.Configuration.GetValue("Database:InitializeOnStartup", true)))
    {
        await using var scope = app.Services.CreateAsyncScope();
        var resetDemo = args.Contains("--reset-demo", StringComparer.OrdinalIgnoreCase);
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None, resetDemo);
    }

    // `dotnet BlueContract.Api.dll --seed-only`: migrate + seed, then exit (used by scripts/seed.*).
    if (seedOnly)
    {
        Log.Information("Seed completed; exiting (--seed-only)");
        return;
    }

    app.UseForwardedHeaders();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    app.MapOpenApi("/openapi/{documentName}.json");
    app.MapScalarApiReference("/docs", options => options.WithTitle("BlueProcure API"));

    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        Predicate = _ => false,
        ResponseWriter = HealthResponseWriter.WriteAsync,
    });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = c => c.Tags.Contains(InfrastructureDI.ReadyTag),
        ResponseWriter = HealthResponseWriter.WriteAsync,
    });

    app.UseCors();
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();
    app.MapControllers();
    app.MapHub<BlueContract.Api.Realtime.NotificationHub>(BlueContract.Api.Realtime.NotificationHub.Path);

    if (hangfire.DashboardEnabled)
    {
        app.MapHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = [new BlueContract.Api.Realtime.DevelopmentDashboardAuthorization(app.Environment)],
            DashboardTitle = "BlueContract — tác vụ nền",
        });
    }

    if (hangfire.ServerEnabled && !isOpenApiGeneration)
    {
        app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<BlueContract.Infrastructure.Jobs.WorkflowReminderJob>(
            BlueContract.Infrastructure.Jobs.WorkflowReminderJob.Id, job => job.RunAsync(CancellationToken.None), Cron.Hourly());

        // Spec §6.5: monthly acceptance drafts (daily check at 01:00 Vietnam time against the configured day).
        app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<BlueContract.Infrastructure.Jobs.AcceptanceJob>(
            BlueContract.Infrastructure.Jobs.AcceptanceJob.Id, job => job.RunAsync(CancellationToken.None), "0 1 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh") });

        // Spec §11: daily integrity check of stored files (SHA-256).
        app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<BlueContract.Infrastructure.Jobs.IntegrityCheckJob>(
            BlueContract.Infrastructure.Jobs.IntegrityCheckJob.Id, job => job.RunAsync(CancellationToken.None), "0 2 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh") });

        // Spec §6.4 B1: optional periodic pull of warehouse receipts from the HIS.
        var hisOptions = app.Configuration.GetSection(BlueContract.Infrastructure.Integration.HisOptions.SectionName).Get<BlueContract.Infrastructure.Integration.HisOptions>()
            ?? new BlueContract.Infrastructure.Integration.HisOptions();
        var recurring = app.Services.GetRequiredService<IRecurringJobManager>();
        if (hisOptions.PullEnabled)
        {
            recurring.AddOrUpdate<BlueContract.Infrastructure.Jobs.HisReceiptPullJob>(
                BlueContract.Infrastructure.Jobs.HisReceiptPullJob.Id, job => job.RunAsync(CancellationToken.None), hisOptions.PullCron);
        }
        else
        {
            recurring.RemoveIfExists(BlueContract.Infrastructure.Jobs.HisReceiptPullJob.Id);
        }
    }

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "BlueProcure API terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Entry point; partial so integration tests can use WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program
{
    protected Program()
    {
    }
}
