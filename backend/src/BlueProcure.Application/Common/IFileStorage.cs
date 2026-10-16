namespace BlueProcure.Application.Common;

/// <summary>S3-compatible object storage. Every stored object carries its SHA-256 (spec §2, §11).</summary>
public interface IFileStorage
{
    Task<StoredFile> UploadAsync(string objectKey, Stream content, string contentType, CancellationToken ct);

    Task<Stream> DownloadAsync(string objectKey, CancellationToken ct);

    Task<Uri> GetPresignedDownloadUrlAsync(string objectKey, TimeSpan lifetime, CancellationToken ct);

    Task EnsureBucketAsync(CancellationToken ct);
}

public sealed record StoredFile(string ObjectKey, long Size, string Sha256, string ContentType);
