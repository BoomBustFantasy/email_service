# PRD-18: Queue-Based Email Service Architecture

**Ticket:** #18  
**Parent:** #17  
**Implemented:** 2026-07-25  
**Status:** Complete

---

## Overview

This document describes the transformation of the email_service from a console application to a combined **web API + background queue consumer** with idempotent, multi-consumer-safe processing semantics.

---

## What Changed

### 1. Project Type Transformation

**Before:** Console application (`Microsoft.NET.Sdk`)  
**After:** Web application (`Microsoft.NET.Sdk.Web`)

The service now runs as an ASP.NET Core web host that simultaneously:
- Serves HTTP API endpoints (health checks, future webhook receivers)
- Executes background queue consumer as a hosted service
- Maintains existing Quartz-scheduled jobs (to be migrated in future tickets)

### 2. Queue-Based Processing Model

#### New Database Tables

**OutboundEmailQueue**
```sql
{
    id: BIGSERIAL PRIMARY KEY,
    idempotency_key: TEXT UNIQUE,
    template_key: TEXT,
    recipient_email: TEXT,
    template_variables: JSONB,
    status: 'pending' | 'completed' | 'dead_letter',
    retry_count: INTEGER,
    created_at: TIMESTAMPTZ,
    locked_until: TIMESTAMPTZ,
    last_error: TEXT
}
```

**EmailDeliveryLog**
```sql
{
    id: BIGSERIAL PRIMARY KEY,
    idempotency_key: TEXT,
    queue_message_id: BIGINT,
    template_key: TEXT,
    brevo_template_id: BIGINT,
    recipient_email: TEXT,
    template_variables: JSONB,
    status: 'pending' | 'sent' | 'delivered' | 'bounced' | ...,
    brevo_message_id: TEXT,
    sent_at: TIMESTAMPTZ,
    delivered_at: TIMESTAMPTZ,
    error_message: TEXT,
    ...
}
```

#### Processing Flow

```
Producer (external) 
    ↓
OutboundEmailQueue (status: pending)
    ↓
QueueConsumerService (polls every 5s)
    ↓
Lock message (locked_until = now + 5min)
    ↓
Check idempotency (EmailDeliveryLog by idempotency_key)
    ↓ (if not already processed)
Create delivery log entry
    ↓
[Future tickets: resolve template, validate, send]
    ↓
Mark message complete OR increment retry_count
    ↓ (if retry_count >= 3)
Move to dead_letter status
```

### 3. Multi-Consumer Safety Guarantees

The queue consumer is designed to be **horizontally scalable** from day one:

1. **Optimistic Locking**: Uses `locked_until` with atomic CAS operations
2. **Idempotency**: Every message has a unique `idempotency_key`; processed messages are logged and skipped on retry
3. **Retry Logic**: Failed messages retry up to 3 times before dead-lettering
4. **Lock Expiration**: Locks expire after 5 minutes to prevent stuck messages

Multiple instances can safely consume the same queue without duplicate sends.

### 4. New Components

| Component | Purpose |
|-----------|---------|
| `QueueConsumerService` | Background service that polls queue and processes messages |
| `QueueConsumerHealthCheck` | Health check for monitoring consumer status |
| `OutboundEmailQueue` | Supabase model for queue table |
| `EmailDeliveryLog` | Supabase model for audit/idempotency tracking |

### 5. HTTP API Endpoints

| Endpoint | Purpose |
|----------|---------|
| `GET /` | Service status and version info |
| `GET /health` | Health check (includes queue consumer status) |

---

## Configuration

No new configuration required. The service uses existing:
- `Supabase:Url`
- `Supabase:ServiceRoleKey`
- `App:BaseUrl`

---

## Deployment

### Database Migration

Run the migration script before deploying:
```bash
psql -h [supabase-host] -d postgres -f docs/migration_add_queue_infrastructure.sql
```

### Build & Run

```bash
dotnet build
dotnet run
```

The service will:
1. Initialize Supabase client
2. Start queue consumer background service
3. Start existing Quartz jobs
4. Listen on HTTP port (default: 5000)

### Health Check

```bash
curl http://localhost:5000/health
# Response: Healthy
```

---

## Queue Consumer Behavior

### Polling Configuration

| Setting | Value |
|---------|-------|
| Polling Interval | 5 seconds |
| Batch Size | 10 messages |
| Lock Duration | 5 minutes |
| Max Retries | 3 attempts |

### Status Transitions

```
pending → (lock acquired) → processing
    ↓ (success)
completed

pending → (lock acquired) → processing → (error, retry < 3)
    ↓
pending (retry_count++)

pending → (lock acquired) → processing → (error, retry >= 3)
    ↓
dead_letter
```

### Idempotency Guarantees

Before processing any message, the consumer:
1. Checks `EmailDeliveryLog` for existing `idempotency_key` with status `sent` or `delivered`
2. If found, marks queue message as `completed` without re-sending
3. If not found, proceeds with processing

This ensures **exactly-once delivery semantics** even if:
- Messages are retried due to transient failures
- Multiple consumers process the same message (lock prevents this, but idempotency is the final safeguard)
- Producer accidentally enqueues duplicate messages

---

## What's NOT Implemented Yet

The following are **intentionally deferred to subsequent tickets** per PRD #17:

- ❌ Template key → Brevo template ID resolution (PRD-19)
- ❌ Template variable validation (PRD-19)
- ❌ Actual Brevo email sending (PRD-19)
- ❌ Retry with exponential backoff (PRD-20)
- ❌ Dead-letter alerting (PRD-20)
- ❌ Brevo webhook receiver (PRD-21)
- ❌ Replay API endpoint (PRD-22)

**Current implementation establishes the foundation:**
- ✅ Web host + worker runtime
- ✅ Queue consumption loop
- ✅ Idempotency tracking
- ✅ Retry/dead-letter state machine
- ✅ Multi-consumer-safe locking
- ✅ Health check endpoint

---

## Testing

### Manual Queue Test

Enqueue a test message:
```sql
INSERT INTO "OutboundEmailQueue" 
    (idempotency_key, template_key, recipient_email, template_variables)
VALUES 
    ('test-' || gen_random_uuid(), 'test_template', 'test@example.com', '{"foo": "bar"}');
```

Watch logs:
```
Processing message 123: template=test_template, recipient=test@example.com
Successfully processed message 123
```

Verify database:
```sql
SELECT status, retry_count FROM "OutboundEmailQueue" WHERE id = 123;
-- status: completed, retry_count: 0

SELECT * FROM "EmailDeliveryLog" WHERE queue_message_id = 123;
-- idempotency_key: test-<uuid>, status: pending
```

### Health Check Test

```bash
curl http://localhost:5000/health
# Expected: HTTP 200 "Healthy"

curl http://localhost:5000/
# Expected: {"service":"email_service","version":"1.0.0","status":"running",...}
```

---

## Future Enhancements (Next Tickets)

| Ticket | Enhancement |
|--------|-------------|
| PRD-19 | Template contract engine + key mapping + sender overrides |
| PRD-20 | Exponential backoff retry + DLQ alerting + metrics |
| PRD-21 | Brevo webhook ingestion + signature verification |
| PRD-22 | Authenticated replay API |
| PRD-23 | Automated tests |

---

## Architecture Decision Records

### Why polling instead of push-based queue?

Supabase does not have a native message queue with push semantics. We use a polling pattern with optimistic locking, which is:
- Simple to implement
- Safe for horizontal scaling
- Sufficient for current volume (low hundreds of emails/day)

If volume increases significantly, we can:
1. Reduce polling interval
2. Add more consumer instances (already multi-consumer safe)
3. Migrate to dedicated queue (RabbitMQ, AWS SQS, etc.) with minimal code changes (adapter pattern)

### Why idempotency_key instead of relying on queue table PK?

- **Replayability**: Dead-letter messages may be re-enqueued with new IDs, but same idempotency key
- **Producer flexibility**: Producers can use natural keys (e.g., `trade_review_{id}_completion`)
- **Multi-source safety**: Different producers can use different key strategies without collision risk

### Why JSONB for template_variables?

- **Flexibility**: No schema changes needed for new template types
- **Auditability**: Full payload retained for troubleshooting
- **Simplicity**: No need for complex DTO serialization in database layer

---

## Operational Notes

### Monitoring

Watch for:
- Queue depth: `SELECT COUNT(*) FROM "OutboundEmailQueue" WHERE status = 'pending';`
- Dead letters: `SELECT COUNT(*) FROM "OutboundEmailQueue" WHERE status = 'dead_letter';`
- Health check failures: `GET /health` should always return 200

### Troubleshooting

**Consumer not processing messages?**
1. Check service logs for errors
2. Verify Supabase connection
3. Check `locked_until` - may need to unlock: `UPDATE "OutboundEmailQueue" SET locked_until = NULL WHERE status = 'pending';`

**Duplicate emails?**
- Check `EmailDeliveryLog` for duplicate `idempotency_key` entries with different statuses
- Should not happen if idempotency logic is working

**Messages stuck in pending?**
- Check `retry_count` - may need manual dead-lettering
- Check `locked_until` - may need to unlock
- Check service logs for errors

---

**Document Version:** 1.0  
**Last Updated:** 2026-07-25
