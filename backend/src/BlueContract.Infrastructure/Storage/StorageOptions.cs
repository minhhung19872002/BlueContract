namespace BlueContract.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>host:port of the S3-compatible endpoint used by the API (e.g. "storage:8333" inside docker).</summary>
    public string Endpoint { get; set; } = "localhost:9000";

    /// <summary>host:port the browser uses for presigned URLs (e.g. "localhost:9000").</summary>
    public string? PublicEndpoint { get; set; }

    public bool UseSsl { get; set; }

    public bool PublicUseSsl { get; set; }

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string Bucket { get; set; } = "blueprocure";
}
