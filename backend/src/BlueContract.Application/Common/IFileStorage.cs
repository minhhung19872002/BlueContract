namespace BlueContract.Application.Common;

/// <summary>S3-compatible object storage. Every stored object carries its SHA-256 (spec §2, §11).</summary>
public interface IFileStorage
{
    Task<StoredFile> UploadAsync(string objectKey, Stream content, string contentType, CancellationToken ct);

    Task<Stream> DownloadAsync(string objectKey, CancellationToken ct);

    /// <summary>
    /// Short-lived download link given after the permission check: "/api/files/{token}" through the portal itself, or a
    /// presigned S3 URL when a public storage endpoint is configured.
    /// </summary>
    Task<Uri> GetPresignedDownloadUrlAsync(string objectKey, TimeSpan lifetime, CancellationToken ct);

    /// <summary>Media type the object was stored with (null when it does not exist).</summary>
    Task<string?> GetContentTypeAsync(string objectKey, CancellationToken ct);

    Task EnsureBucketAsync(CancellationToken ct);
}

public sealed record StoredFile(string ObjectKey, long Size, string Sha256, string ContentType);
