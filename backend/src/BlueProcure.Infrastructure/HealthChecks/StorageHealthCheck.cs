using BlueProcure.Application.Common;
using BlueProcure.Infrastructure.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BlueProcure.Infrastructure.HealthChecks;

internal sealed class StorageHealthCheck(IFileStorage storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (storage is not S3FileStorage s3)
        {
            return HealthCheckResult.Healthy("Non-S3 storage");
        }

        try
        {
            var bucketExists = await s3.PingAsync(cancellationToken);
            return bucketExists
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Degraded("Bucket chưa được tạo");
        }
#pragma warning disable CA1031 // Health check must report, not throw
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return HealthCheckResult.Unhealthy("Không kết nối được kho lưu trữ tệp (S3)", ex);
        }
    }
}
