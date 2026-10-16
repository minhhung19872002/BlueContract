using System.Text.Json.Serialization;
using BlueProcure.Api.Auth;
using BlueProcure.Api.Middleware;
using BlueProcure.Application;
using BlueProcure.Infrastructure;
using BlueProcure.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;
using Serilog;
using InfrastructureDI = BlueProcure.Infrastructure.DependencyInjection;

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
        .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    // OpenAPI schema generation reads the HTTP JSON options: keep enums as strings there too.
    builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(o => o.SuppressModelStateInvalidFilter = false);
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        // Behind the nginx reverse proxy (web container): trust X-Forwarded-* from the private network.
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
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
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

    var app = builder.Build();

    // Skip bootstrap when the build-time OpenAPI generator (GetDocument.Insider) loads the app.
    var isOpenApiGeneration = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    // `dotnet BlueProcure.Api.dll --export-templates <dir>`: write the built-in DOCX templates and exit.
    var exportIndex = Array.FindIndex(args, a => a.Equals("--export-templates", StringComparison.OrdinalIgnoreCase));
    if (exportIndex >= 0)
    {
        var target = exportIndex + 1 < args.Length ? args[exportIndex + 1] : "templates";
        foreach (var path in BlueProcure.Infrastructure.Persistence.Seeding.TemplateSeeder.Export(target))
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

    // `dotnet BlueProcure.Api.dll --seed-only`: migrate + seed, then exit (used by scripts/seed.*).
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
