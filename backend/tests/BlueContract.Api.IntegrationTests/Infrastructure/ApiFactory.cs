using System.Net.Http.Headers;
using System.Net.Http.Json;
using BlueContract.Application.Auth;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Testcontainers.PostgreSql;

namespace BlueContract.Api.IntegrationTests.Infrastructure;

/// <summary>Boots the real API against throwaway PostgreSQL + SeaweedFS (S3) containers, with demo data seeded.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DemoPassword = "Demo@123";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    private readonly SeaweedFsContainer _storage = new();

    private readonly IContainer _gotenberg = new ContainerBuilder("gotenberg/gotenberg:8.20")
        .WithPortBinding(3000, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(3000).ForPath("/health")))
        .Build();

    public const string HisApiKey = "his-test-key";

    public const string HisSecret = "his-test-secret-0123456789abcdef";

    public const string OnlyOfficeSecret = "integration-onlyoffice-secret-0123456789abcdef";

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _storage.StartAsync(), _gotenberg.StartAsync());
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _storage.DisposeAsync();
        await _gotenberg.DisposeAsync();
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
        builder.UseSetting("ExternalServices:GotenbergUrl", $"http://{_gotenberg.Hostname}:{_gotenberg.GetMappedPublicPort(3000)}");
        builder.UseSetting("OnlyOffice:JwtSecret", OnlyOfficeSecret);
        builder.UseSetting("OnlyOffice:InternalUrl", "http://localhost");
        builder.UseSetting("OnlyOffice:ApiBaseUrlForEditor", "http://localhost/");

        // The fake "document server" downloads edited files from this test server.
        builder.ConfigureTestServices(services => services
            .AddHttpClient(BlueContract.Infrastructure.DependencyInjection.OnlyOfficeClient)
            .ConfigurePrimaryHttpMessageHandler(() => Server.CreateHandler()));

        // Every signature gets an RFC 3161 timestamp from the in-memory TSA (spec §8, AUDIT-1 §11.7).
        builder.UseSetting("Signing:TsaUrl", FakeTimestampAuthority.Url);
        builder.ConfigureTestServices(services => services
            .AddHttpClient("tsa")
            .ConfigurePrimaryHttpMessageHandler(() => new FakeTimestampAuthority()));
        builder.UseSetting("Auth:SigningKey", "integration-test-signing-key-0123456789abcdef");
        builder.UseSetting("Auth:LoginRateLimitPerMinute", "100000");
        builder.UseSetting("Seed:Demo", "true");
        builder.UseSetting("Seed:DemoPassword", DemoPassword);
        builder.UseSetting("Email:Enabled", "false");
        builder.UseSetting("Hangfire:ServerEnabled", "false");
        builder.UseSetting("Signing:MockRemoteDelaySeconds", "0");
        builder.UseSetting("HisInbound:Clients:0:Name", "HIS-test");
        builder.UseSetting("HisInbound:Clients:0:ApiKey", HisApiKey);
        builder.UseSetting("HisInbound:Clients:0:Secret", HisSecret);
        builder.UseSetting("HisInbound:DevEndpoints", "true");
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
