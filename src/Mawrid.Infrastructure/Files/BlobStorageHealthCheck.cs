using Mawrid.Application.Files;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Mawrid.Infrastructure.Files;

/// <summary>Ready only when blob storage answers.</summary>
public sealed class BlobStorageHealthCheck(IFileStorage storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (storage is not AzureBlobFileStorage blob)
        {
            return HealthCheckResult.Healthy();
        }

        try
        {
            // The answer (exists or not) does not matter; getting one proves storage is reachable.
            await blob.Container.ExistsAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Blob storage did not answer.", ex);
        }
    }
}
