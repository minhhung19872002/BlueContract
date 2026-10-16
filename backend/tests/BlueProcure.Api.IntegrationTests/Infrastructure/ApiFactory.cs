using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;

namespace BlueProcure.Api.IntegrationTests.Infrastructure;

/// <summary>Boots the real API against throwaway PostgreSQL + MinIO containers.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
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
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
