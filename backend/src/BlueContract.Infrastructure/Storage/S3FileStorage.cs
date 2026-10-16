using System.Security.Cryptography;
using BlueContract.Application.Common;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace BlueContract.Infrastructure.Storage;

/// <summary>
/// S3-compatible object storage (SeaweedFS in docker; any S3 endpoint in production) via the MinIO .NET client.
/// </summary>
internal sealed class S3FileStorage : IFileStorage
{
    private const string ShaMetadataKey = "x-amz-meta-sha256";

    /// <summary>Path of the API endpoint that serves signed download links (FilesController).</summary>
    public const string LinkPath = "/api/files/";

    private readonly IMinioClient _client;
    private readonly IMinioClient? _publicClient;
    private readonly StorageOptions _options;
    private readonly IUrlSigner _signer;

    public S3FileStorage(IOptions<StorageOptions> options, IUrlSigner signer)
    {
        _options = options.Value;
        _signer = signer;
        _client = Build(_options.Endpoint, _options.UseSsl);
        _publicClient = string.IsNullOrWhiteSpace(_options.PublicEndpoint) ? null : Build(_options.PublicEndpoint, _options.PublicUseSsl);
    }

    public async Task<StoredFile> UploadAsync(string objectKey, Stream content, string contentType, CancellationToken ct)
    {
        // Buffer to compute SHA-256 and size before upload; documents are bounded by upload limits.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(buffer, ct));
        buffer.Position = 0;

        var args = new PutObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(objectKey)
            .WithStreamData(buffer)
            .WithObjectSize(buffer.Length)
            .WithContentType(contentType)
            .WithHeaders(new Dictionary<string, string> { [ShaMetadataKey] = sha });
        await _client.PutObjectAsync(args, ct);

        return new StoredFile(objectKey, buffer.Length, sha, contentType);
    }

    public async Task<Stream> DownloadAsync(string objectKey, CancellationToken ct)
    {
        var result = new MemoryStream();
        var args = new GetObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(objectKey)
            .WithCallbackStream((stream, token) => stream.CopyToAsync(result, token));
        await _client.GetObjectAsync(args, ct);
        result.Position = 0;
        return result;
    }

    public async Task<Uri> GetPresignedDownloadUrlAsync(string objectKey, TimeSpan lifetime, CancellationToken ct)
    {
        if (_publicClient is null)
        {
            // Through the portal (same host and TLS as the web app, behind the same nginx): works from every computer of
            // the hospital, unlike the storage's own address.
            return new Uri(LinkPath + _signer.Sign(objectKey, lifetime), UriKind.Relative);
        }

        var args = new PresignedGetObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(objectKey)
            .WithExpiry((int)lifetime.TotalSeconds);
        var url = await _publicClient.PresignedGetObjectAsync(args);
        return new Uri(url);
    }

    public async Task<string?> GetContentTypeAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            var stat = await _client.StatObjectAsync(new StatObjectArgs().WithBucket(_options.Bucket).WithObject(objectKey), ct);
            return stat.ContentType;
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            return null;
        }
    }

    public async Task EnsureBucketAsync(CancellationToken ct)
    {
        var exists = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_options.Bucket), ct);
        if (!exists)
        {
            await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_options.Bucket), ct);
        }
    }

    internal Task<bool> PingAsync(CancellationToken ct) =>
        _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_options.Bucket), ct);

    private IMinioClient Build(string endpoint, bool ssl) =>
        new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(_options.AccessKey, _options.SecretKey)
            .WithSSL(ssl)
            .Build();
}
