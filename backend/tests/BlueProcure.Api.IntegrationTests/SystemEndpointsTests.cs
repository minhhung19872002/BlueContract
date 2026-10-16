using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueProcure.Api.Controllers;
using BlueProcure.Api.IntegrationTests.Infrastructure;
using BlueProcure.Application.Common;
using BlueProcure.Domain.Settings;
using BlueProcure.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlueProcure.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SystemEndpointsTests(ApiFactory factory)
{
    [Fact]
    public async Task Liveness_returns_healthy()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_reports_all_dependencies_healthy()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var checks = json.RootElement.GetProperty("checks");

        checks.GetProperty("postgres").GetProperty("status").GetString().ShouldBe("Healthy");
        checks.GetProperty("storage").GetProperty("status").GetString().ShouldBe("Healthy");
        checks.GetProperty("gotenberg").GetProperty("status").GetString().ShouldBe("Healthy");
    }

    [Fact]
    public async Task System_info_returns_metadata_and_correlation_header()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/system/info", UriKind.Relative));
        var info = await response.Content.ReadFromJsonAsync<SystemInfoResponse>();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        info!.Name.ShouldBe("BlueProcure");
        response.Headers.Contains("X-Correlation-Id").ShouldBeTrue();
    }

    [Fact]
    public async Task Client_correlation_id_is_echoed()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/system/info");
        request.Headers.Add("X-Correlation-Id", "test-abc-123");

        using var response = await client.SendAsync(request);

        response.Headers.GetValues("X-Correlation-Id").Single().ShouldBe("test-abc-123");
    }

    [Fact]
    public async Task Startup_applies_migrations_and_seeds_settings_idempotently()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();

        // Second run must not duplicate anything.
        await initializer.InitializeAsync(CancellationToken.None);

        var count = await db.SystemSettings.CountAsync();
        count.ShouldBe(SystemSettingKeys.Defaults().Count);
    }

    [Fact]
    public async Task Storage_roundtrip_preserves_content_and_hash()
    {
        using var scope = factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var bytes = "Hợp đồng mẫu"u8.ToArray();

        using var input = new MemoryStream(bytes);
        var stored = await storage.UploadAsync("tests/sample.txt", input, "text/plain", CancellationToken.None);
        await using var downloaded = await storage.DownloadAsync("tests/sample.txt", CancellationToken.None);
        using var copy = new MemoryStream();
        await downloaded.CopyToAsync(copy);

        copy.ToArray().ShouldBe(bytes);
        stored.Sha256.ShouldBe(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
        stored.Size.ShouldBe(bytes.Length);
    }

    [Fact]
    public async Task Presigned_url_downloads_the_object_without_credentials()
    {
        using var scope = factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var bytes = "Phiếu nhập kho"u8.ToArray();
        using var input = new MemoryStream(bytes);
        await storage.UploadAsync("tests/presigned.txt", input, "text/plain", CancellationToken.None);

        var url = await storage.GetPresignedDownloadUrlAsync("tests/presigned.txt", TimeSpan.FromMinutes(5), CancellationToken.None);
        using var http = new HttpClient();
        var downloaded = await http.GetByteArrayAsync(url);

        downloaded.ShouldBe(bytes);
    }

    [Fact]
    public async Task Unknown_api_route_returns_problem_details()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }
}
