using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BlueContract.Infrastructure.HealthChecks;

/// <summary>Healthy when GET {baseUrl}{path} returns 2xx. Used for Gotenberg and ONLYOFFICE.</summary>
internal sealed class HttpEndpointHealthCheck(IHttpClientFactory httpClientFactory, string clientName, string path) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(clientName);
            using var response = await client.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"HTTP {(int)response.StatusCode}");
        }
#pragma warning disable CA1031 // Health check must report, not throw
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return HealthCheckResult.Unhealthy($"Không kết nối được {clientName}", ex);
        }
    }
}
