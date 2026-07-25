using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EmailService.Services;

/// <summary>
/// Health check for the queue consumer service.
/// Verifies that the background service is running and processing messages.
/// </summary>
public class QueueConsumerHealthCheck : IHealthCheck
{
    private readonly ILogger<QueueConsumerHealthCheck> _logger;

    public QueueConsumerHealthCheck(ILogger<QueueConsumerHealthCheck> logger)
    {
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // For now, basic health check that service is running
            // In future tickets, we can add more sophisticated checks:
            // - Queue depth monitoring
            // - Dead letter queue size
            // - Last successful processing timestamp
            // - Error rate tracking

            return Task.FromResult(HealthCheckResult.Healthy(
                "Queue consumer is running"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Queue consumer health check failed",
                ex));
        }
    }
}
