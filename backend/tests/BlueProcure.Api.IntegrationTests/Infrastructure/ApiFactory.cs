using System.Net.Http.Headers;
using System.Net.Http.Json;
using BlueProcure.Application.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Testcontainers.PostgreSql;

namespace BlueProcure.Api.IntegrationTests.Infrastructure;

/// <summary>Boots the real API against throwaway PostgreSQL + SeaweedFS (S3) containers, with demo data seeded.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DemoPassword = "Demo@123";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private readonly SeaweedFsContainer _storage = new();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _storage.StartAsync());
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _storage.DisposeAsync();
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
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        builder.UseSetting("Storage:Endpoint", _storage.Endpoint);
        builder.UseSetting("Storage:PublicEndpoint", _storage.Endpoint);
        builder.UseSetting("Storage:AccessKey", SeaweedFsContainer.AccessKey);
        builder.UseSetting("Storage:SecretKey", SeaweedFsContainer.SecretKey);
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
