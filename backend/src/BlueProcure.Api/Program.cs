using BlueProcure.Api.Middleware;
using BlueProcure.Application;
using BlueProcure.Infrastructure;
using BlueProcure.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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

    builder.Services.AddControllers();
    builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "BlueProcure API";
        document.Info.Description = "Cổng Quản lý & Thanh toán Mua sắm Hàng hóa Trực tuyến";
        return Task.CompletedTask;
    }));
    builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx =>
        ctx.ProblemDetails.Extensions["correlationId"] = ctx.HttpContext.TraceIdentifier);
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    var app = builder.Build();

    // Skip bootstrap when the build-time OpenAPI generator (GetDocument.Insider) loads the app.
    var isOpenApiGeneration = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
    var seedOnly = args.Contains("--seed-only", StringComparer.OrdinalIgnoreCase);
    if (!isOpenApiGeneration && (seedOnly || app.Configuration.GetValue("Database:InitializeOnStartup", true)))
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);
    }

    // `dotnet BlueProcure.Api.dll --seed-only`: migrate + seed, then exit (used by scripts/seed.*).
    if (seedOnly)
    {
        Log.Information("Seed completed; exiting (--seed-only)");
        return;
    }

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
