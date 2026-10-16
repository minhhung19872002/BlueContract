using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace BlueProcure.Api.IntegrationTests.Infrastructure;

/// <summary>Throwaway SeaweedFS with its S3 gateway — same image and startup as docker-compose.yml.</summary>
public sealed class SeaweedFsContainer : IAsyncDisposable
{
    public const string AccessKey = "testkey";
    public const string SecretKey = "testsecret123";
    private const int S3Port = 8333;

    private readonly IContainer _container = new ContainerBuilder("chrislusf/seaweedfs:3.97")
        .WithEntrypoint("sh", "-c")
        .WithCommand(
            "printf '{\"identities\":[{\"name\":\"app\",\"credentials\":[{\"accessKey\":\"" + AccessKey + "\",\"secretKey\":\"" + SecretKey + "\"}]," +
            "\"actions\":[\"Admin\",\"Read\",\"Write\",\"List\",\"Tagging\"]}]}' > /tmp/s3.json && " +
            "exec weed server -dir=/data -s3 -s3.port=8333 -s3.config=/tmp/s3.json -master.volumeSizeLimitMB=128 -volume.max=0 -ip.bind=0.0.0.0")
        .WithPortBinding(S3Port, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(S3Port).ForPath("/healthz")))
        .Build();

    public string Endpoint => $"{_container.Hostname}:{_container.GetMappedPublicPort(S3Port)}";

    public Task StartAsync() => _container.StartAsync();

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
