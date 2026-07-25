namespace EmailService.Services;

/// <summary>
/// Tracks operational metrics for the email queue consumer
/// </summary>
public class QueueMetrics
{
    private long _totalProcessed;
    private long _totalSuccessful;
    private long _totalFailed;
    private long _totalRetries;
    private long _totalDeadLettered;
    private long _currentQueueDepth;

    public long TotalProcessed => _totalProcessed;
    public long TotalSuccessful => _totalSuccessful;
    public long TotalFailed => _totalFailed;
    public long TotalRetries => _totalRetries;
    public long TotalDeadLettered => _totalDeadLettered;
    public long CurrentQueueDepth => _currentQueueDepth;

    public double SuccessRate => _totalProcessed > 0
        ? (double)_totalSuccessful / _totalProcessed
        : 1.0;

    public void IncrementProcessed() => Interlocked.Increment(ref _totalProcessed);
    public void IncrementSuccessful() => Interlocked.Increment(ref _totalSuccessful);
    public void IncrementFailed() => Interlocked.Increment(ref _totalFailed);
    public void IncrementRetries() => Interlocked.Increment(ref _totalRetries);
    public void IncrementDeadLettered() => Interlocked.Increment(ref _totalDeadLettered);
    public void SetQueueDepth(long depth) => Interlocked.Exchange(ref _currentQueueDepth, depth);

    public Dictionary<string, object> GetSnapshot()
    {
        return new Dictionary<string, object>
        {
            ["total_processed"] = TotalProcessed,
            ["total_successful"] = TotalSuccessful,
            ["total_failed"] = TotalFailed,
            ["total_retries"] = TotalRetries,
            ["total_dead_lettered"] = TotalDeadLettered,
            ["current_queue_depth"] = CurrentQueueDepth,
            ["success_rate"] = Math.Round(SuccessRate, 4)
        };
    }
}
