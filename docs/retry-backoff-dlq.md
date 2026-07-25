# Retry, Backoff, and Dead Letter Queue (DLQ) System

## Overview

The email service implements production-grade reliability controls for queue processing, including:

1. **3-attempt exponential retry** for failed email deliveries
2. **Dead letter queue (DLQ)** for messages that exhaust retries
3. **Operational alerts** for degradation and DLQ accumulation
4. **Service metrics** for monitoring queue health

## Architecture

### Retry Strategy

When an email delivery fails, the system:

1. **Increments retry count** on the queue message
2. **Applies exponential backoff** before next attempt
3. **Moves to DLQ** after max attempts exhausted

#### Exponential Backoff Formula

```
delay = BaseBackoffMs × (BackoffMultiplier ^ (retryCount - 1))
delay = min(delay, MaxBackoffMs)
```

**Default Configuration:**
- Base delay: 1000ms (1 second)
- Multiplier: 2.0
- Max delay: 60000ms (60 seconds)

**Retry Schedule Example:**
- Attempt 1: Immediate
- Attempt 2: 1s delay
- Attempt 3: 2s delay
- Move to DLQ after attempt 3 fails

### Dead Letter Queue

Messages are moved to the `DeadLetterEmail` table when:
- Retry count reaches `MaxRetryAttempts` (default: 3)
- All retry attempts have failed

**DLQ Entry Contains:**
- Original queue message ID
- Recipient email & template key
- Template parameters (for replay)
- Total attempts made
- Last error message
- Timestamp of DLQ move

### Metrics Tracking

The `QueueMetrics` service tracks:

| Metric | Description |
|--------|-------------|
| `total_processed` | Total messages processed |
| `total_successful` | Successfully delivered messages |
| `total_failed` | Failed delivery attempts |
| `total_retries` | Number of retry attempts |
| `total_dead_lettered` | Messages moved to DLQ |
| `current_queue_depth` | Current pending message count |
| `success_rate` | Ratio of successful/processed |

**Access Metrics:**
```bash
curl http://localhost:5000/metrics
```

**Response Example:**
```json
{
  "queue_metrics": {
    "total_processed": 1542,
    "total_successful": 1520,
    "total_failed": 22,
    "total_retries": 18,
    "total_dead_lettered": 4,
    "current_queue_depth": 12,
    "success_rate": 0.9857
  },
  "timestamp": "2026-07-25T18:30:00Z"
}
```

## Configuration

### appsettings.json

```json
{
  "QueueReliability": {
    "MaxRetryAttempts": 3,
    "BaseBackoffMs": 1000,
    "BackoffMultiplier": 2.0,
    "MaxBackoffMs": 60000,
    "OperationalAlertEmail": "ops@boombustfantasy.com",
    "SuccessRateDegradationThreshold": 0.8,
    "DeadLetterQueueSizeThreshold": 10
  }
}
```

### Configuration Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `MaxRetryAttempts` | int | 3 | Max retries before DLQ |
| `BaseBackoffMs` | int | 1000 | Initial backoff delay (ms) |
| `BackoffMultiplier` | double | 2.0 | Exponential multiplier |
| `MaxBackoffMs` | int | 60000 | Maximum backoff delay (ms) |
| `OperationalAlertEmail` | string | null | Email for ops alerts |
| `SuccessRateDegradationThreshold` | double | 0.8 | Success rate threshold (0-1) |
| `DeadLetterQueueSizeThreshold` | int | 10 | DLQ size alert threshold |

### Environment Variable Overrides

```bash
QueueReliability__MaxRetryAttempts=5
QueueReliability__OperationalAlertEmail=alerts@example.com
QueueReliability__SuccessRateDegradationThreshold=0.9
```

## Operational Alerts

The system monitors for two degradation scenarios:

### 1. Success Rate Degradation

**Trigger:** Success rate drops below configured threshold (default: 80%)

**Conditions:**
- Minimum 20 messages processed
- Success rate < `SuccessRateDegradationThreshold`

**Alert Contains:**
- Current success rate
- Configured threshold
- Recent error patterns

### 2. Dead Letter Queue Accumulation

**Trigger:** DLQ size exceeds configured threshold (default: 10 messages)

**Conditions:**
- DLQ row count >= `DeadLetterQueueSizeThreshold`

**Alert Contains:**
- Current DLQ size
- Most common error types
- Affected templates

## Monitoring & Observability

### Health Check

```bash
curl http://localhost:5000/health
```

Returns queue consumer health status.

### Metrics Endpoint

```bash
curl http://localhost:5000/metrics
```

Returns real-time queue metrics (see example above).

### Logs

Queue consumer emits structured logs:

```
[Info] Processing 5 messages from queue
[Info] Message 123 retry attempt 2 - applying 2000ms backoff
[Info] Successfully processed message 123
[Warning] Message 456 exceeded max retries (3) - moving to dead letter queue
[Error] Message 456 moved to dead letter queue after 3 attempts. Error: Connection timeout
```

## DLQ Management

### Querying Dead Letters

```sql
SELECT 
    id,
    template_key,
    recipient_email,
    attempts,
    last_error,
    moved_to_dlq_at
FROM "DeadLetterEmail"
ORDER BY moved_to_dlq_at DESC
LIMIT 10;
```

### Analyzing Failure Patterns

```sql
SELECT 
    template_key,
    COUNT(*) as failures,
    MAX(moved_to_dlq_at) as last_failure
FROM "DeadLetterEmail"
GROUP BY template_key
ORDER BY failures DESC;
```

### Manual Replay

To manually retry a dead-lettered message:

1. Query the DLQ for the message
2. Insert a new message into `OutboundEmailQueue` with:
   - Same template_key
   - Same recipient_email
   - Same template_variables
   - **New** idempotency_key (to avoid dedup)
3. Delete or mark the DLQ entry as processed

**Example:**
```sql
-- 1. Retrieve DLQ message
SELECT * FROM "DeadLetterEmail" WHERE id = 123;

-- 2. Create new queue entry (with new idempotency key)
INSERT INTO "OutboundEmailQueue" (
    idempotency_key,
    template_key,
    recipient_email,
    template_variables,
    status
) VALUES (
    'manual-retry-' || gen_random_uuid(),
    'team-review-notification',
    'user@example.com',
    '{"recipient_name": "John Doe", ...}'::jsonb,
    'pending'
);

-- 3. Mark DLQ entry as handled (optional)
DELETE FROM "DeadLetterEmail" WHERE id = 123;
```

## Best Practices

### Retry Configuration

- **Conservative retries:** 3 attempts is typically sufficient
- **Backoff tuning:** Adjust based on failure patterns
  - API rate limits → longer backoff
  - Transient errors → shorter backoff
- **Max backoff:** Cap at reasonable limit (60s default)

### Alert Configuration

- **Success threshold:** Set based on baseline performance
  - Start at 95%, adjust down if noisy
- **DLQ threshold:** Set based on expected volume
  - High volume → higher threshold
  - Low volume → 10 is reasonable

### Operational Response

**When success rate degrades:**
1. Check external dependencies (Brevo API status)
2. Review recent error messages
3. Check for configuration changes
4. Validate template mappings

**When DLQ accumulates:**
1. Identify common failure patterns
2. Fix root cause (bad template, config issue)
3. Manually replay affected messages
4. Clear DLQ after resolution

## Future Enhancements

- [ ] Automatic DLQ replay with exponential backoff
- [ ] Per-template retry strategies
- [ ] Circuit breaker for cascading failures
- [ ] Prometheus metrics export
- [ ] DLQ dashboard in admin UI
- [ ] Automated error classification
- [ ] Smart retry based on error type
