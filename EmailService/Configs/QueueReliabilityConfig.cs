namespace EmailService.Configs;

/// <summary>
/// Configuration for queue reliability features (retry, backoff, DLQ)
/// </summary>
public class QueueReliabilityConfig
{
    /// <summary>
    /// Maximum number of retry attempts before moving to dead letter queue
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay in milliseconds for exponential backoff (first retry)
    /// </summary>
    public int BaseBackoffMs { get; set; } = 1000;

    /// <summary>
    /// Exponential backoff multiplier (2.0 = double the delay each retry)
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Maximum backoff delay in milliseconds
    /// </summary>
    public int MaxBackoffMs { get; set; } = 60000;

    /// <summary>
    /// Email address to send operational alerts (dead letter, degradation)
    /// </summary>
    public string? OperationalAlertEmail { get; set; }

    /// <summary>
    /// Threshold for success rate degradation alert (0-1, e.g., 0.8 = 80%)
    /// </summary>
    public double SuccessRateDegradationThreshold { get; set; } = 0.8;

    /// <summary>
    /// Threshold for dead letter queue size alert
    /// </summary>
    public int DeadLetterQueueSizeThreshold { get; set; } = 10;

    /// <summary>
    /// Messages older than this are dropped instead of sent. A consumer outage
    /// leaves a backlog of notifications that are no longer useful — in August
    /// 2026 the queue held trade-review-complete mail up to 18 days stale — and
    /// delivering them late is worse than not delivering them.
    /// </summary>
    public int MaxMessageAgeHours { get; set; } = 48;
}
