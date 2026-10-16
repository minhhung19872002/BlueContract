using System.Net.Http.Headers;
using System.Net.Http.Json;
using BlueProcure.Application.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;

namespace BlueProcure.Api.IntegrationTests.Infrastructure;

/// <summary>Boots the real API against throwaway PostgreSQL + MinIO containers, with demo data seeded.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DemoPassword = "Demo@123";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private readonly MinioContainer _minio = new MinioBuilder("minio/minio:RELEASE.2025-04-22T22-12-26Z")
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _minio.StartAsync());
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _minio.DisposeAsync();
        await base.DisposeAsync();
    }

    /// <summary>Client with its own cookie jar, logged in as <paramref name="userName"/>.</summary>
    public async Task<HttpClient> LoginAsync(string userName, string password = DemoPassword)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password));
        response.IsSuccessStatusCode.ShouldBeTrue($"login {userName} failed: {await response.Content.ReadAsStringAsync()}");
        var result = await response.Content.ReadFromJsonAsync<LoginResult>(JsonDefaults.Options);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result!.AccessToken);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var minioUri = new Uri(_minio.GetConnectionString());
        var minioEndpoint = $"{minioUri.Host}:{minioUri.Port}";

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        builder.UseSetting("Storage:Endpoint", minioEndpoint);
        builder.UseSetting("Storage:PublicEndpoint", minioEndpoint);
        builder.UseSetting("Storage:AccessKey", _minio.GetAccessKey());
        builder.UseSetting("Storage:SecretKey", _minio.GetSecretKey());
        builder.UseSetting("Storage:Bucket", "blueprocure-test");
        builder.UseSetting("ExternalServices:GotenbergUrl", "http://127.0.0.1:1");
        builder.UseSetting("Auth:SigningKey", "integration-test-signing-key-0123456789abcdef");
        builder.UseSetting("Auth:LoginRateLimitPerMinute", "100000");
        builder.UseSetting("Seed:Demo", "true");
        builder.UseSetting("Seed:DemoPassword", DemoPassword);
        builder.UseSetting("Email:Enabled", "false");
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

public static class JsonDefaults
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
