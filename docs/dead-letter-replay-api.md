# Dead Letter Replay API

## Overview

The Admin API provides authenticated operational controls for managing the email service, including the ability to replay messages from the dead letter queue back into the processing queue.

## Authentication

All admin endpoints require API key authentication via the `X-Admin-Api-Key` header.

### Configuration

Set the admin API key in `appsettings.json`:

```json
{
  "App": {
    "BaseUrl": "https://your-domain.com",
    "AdminApiKey": "your-secure-random-api-key"
  }
}
```

**Security Best Practices:**
- Generate a cryptographically random API key (minimum 32 characters)
- Never commit real API keys to source control
- Use environment variables or secret management in production
- Rotate keys periodically
- Restrict access to admin endpoints at the network level when possible

## Replay Dead Letter Messages

**Endpoint:** `POST /admin/replay-dead-letters`

Replays messages from the dead letter queue back into the outbound email queue for reprocessing.

### Request

**Headers:**
```
X-Admin-Api-Key: your-admin-api-key
Content-Type: application/json
```

**Body:**
```json
{
  "messageIds": [123, 456, 789]
}
```

**Parameters:**
- `messageIds` (array of integers, required) - List of dead letter message IDs to replay
  - Minimum: 1 message
  - Maximum: 100 messages per request
  - IDs must be valid DeadLetterEmail primary keys

### Response

**Success (200 OK):**
```json
{
  "total": 3,
  "succeeded": 2,
  "failed": 1,
  "results": [
    {
      "deadLetterMessageId": 123,
      "queueMessageId": 456,
      "success": true,
      "error": null
    },
    {
      "deadLetterMessageId": 456,
      "queueMessageId": 789,
      "success": true,
      "error": null
    },
    {
      "deadLetterMessageId": 789,
      "queueMessageId": null,
      "success": false,
      "error": "Message not found"
    }
  ]
}
```

**Error Responses:**

**401 Unauthorized** - Missing or invalid API key:
```json
{
  "error": "Admin API key required"
}
```
or
```json
{
  "error": "Invalid admin API key"
}
```

**400 Bad Request** - Invalid request:
```json
{
  "error": "No message IDs provided"
}
```
or
```json
{
  "error": "Maximum batch size is 100 messages"
}
```

**500 Internal Server Error** - Server error:
```json
{
  "error": "Internal server error"
}
```

## Replay Behavior

### Strategy 1: Reset Original Message

If the original OutboundEmailQueue message still exists and has status `dead_lettered`:

1. Reset status to `pending`
2. Clear `retry_count` to 0
3. Clear `locked_until`
4. Clear `last_error`
5. Message will be picked up by queue consumer on next poll

**Idempotency:** Original `idempotency_key` is preserved.

### Strategy 2: Create New Message

If the original queue message doesn't exist or has been deleted:

1. Create new OutboundEmailQueue message
2. Copy `template_key`, `recipient_email`, `template_variables` from dead letter
3. Generate new `idempotency_key` with format: `{guid}-replay-{dead_letter_id}`
4. Set status to `pending`
5. Set `retry_count` to 0

**Idempotency:** New key prevents duplicate sends if original was already reprocessed.

### Batch Processing

- Replay operations are processed sequentially (not transactional)
- Partial failures are allowed - some messages may succeed while others fail
- Response includes individual result for each message ID
- Failed replays are logged but don't stop batch processing

## Use Cases

### 1. Recover from Provider Outage

When Brevo or another provider has an outage, messages may accumulate in the dead letter queue. After the outage is resolved:

```bash
curl -X POST https://your-domain.com/admin/replay-dead-letters \
  -H "X-Admin-Api-Key: your-key" \
  -H "Content-Type: application/json" \
  -d '{"messageIds": [1, 2, 3, 4, 5]}'
```

### 2. Fix Template Validation Errors

After fixing a template contract validation bug, replay messages that failed validation:

```bash
# Query dead letter queue for validation errors
SELECT id FROM "DeadLetterEmail" 
WHERE last_error LIKE '%validation%'
ORDER BY moved_to_dlq_at DESC
LIMIT 50;

# Replay those messages
curl -X POST https://your-domain.com/admin/replay-dead-letters \
  -H "X-Admin-Api-Key: your-key" \
  -H "Content-Type: application/json" \
  -d '{"messageIds": [...]}'
```

### 3. Retry Suppressed Emails (Manual Override)

If an email was suppressed incorrectly (e.g., soft bounce that later resolved), manually replay after removing suppression:

```bash
# Remove suppression
DELETE FROM "EmailSuppression" 
WHERE email = 'user@example.com' AND reason = 'soft_bounce';

# Replay the message
curl -X POST https://your-domain.com/admin/replay-dead-letters \
  -H "X-Admin-Api-Key: your-key" \
  -H "Content-Type: application/json" \
  -d '{"messageIds": [123]}'
```

## Monitoring

### Logging

Replay operations are logged with:

```
[Information] Replaying {Count} dead letter messages
[Information] Reset original queue message {MessageId} to pending
[Information] Created new queue message {MessageId} from dead letter {DlqMessageId}
[Information] Replay complete: {SuccessCount} succeeded, {FailCount} failed
[Error] Error replaying dead letter message {MessageId}
[Warning] Dead letter message {MessageId} not found
```

### Metrics

Track replay operations:
- Total replays attempted
- Success/failure rate
- Messages replayed by strategy (reset vs. new)
- Time to process batch

## Operational Workflows

### Daily Dead Letter Review

```bash
#!/bin/bash
# Check dead letter queue size
DLQ_COUNT=$(psql -t -c "SELECT COUNT(*) FROM \"DeadLetterEmail\"")

if [ "$DLQ_COUNT" -gt 10 ]; then
  echo "Dead letter queue has $DLQ_COUNT messages - review needed"
  # Analyze errors
  psql -c "SELECT last_error, COUNT(*) FROM \"DeadLetterEmail\" 
           GROUP BY last_error ORDER BY COUNT(*) DESC LIMIT 10"
fi
```

### Automated Recovery Script

```bash
#!/bin/bash
# Replay messages with specific error pattern after fix is deployed

# Get message IDs
MESSAGE_IDS=$(psql -t -A -c "
  SELECT ARRAY_AGG(id ORDER BY id) 
  FROM \"DeadLetterEmail\" 
  WHERE last_error LIKE '%specific error pattern%'
  LIMIT 100
")

# Replay in batches of 100
curl -X POST https://your-domain.com/admin/replay-dead-letters \
  -H "X-Admin-Api-Key: $ADMIN_API_KEY" \
  -H "Content-Type: application/json" \
  -d "{\"messageIds\": $MESSAGE_IDS}"
```

## Security Considerations

### API Key Security

- **Generate Strong Keys:** Use `openssl rand -base64 32` or similar
- **Environment Variables:** Set via `$env:AdminApiKey` or `export ADMIN_API_KEY`
- **Key Rotation:** Change keys quarterly or after suspected compromise
- **Audit Logging:** Log all admin API access attempts (success and failure)

### Network Security

- **Restrict Access:** Use firewall rules to limit admin endpoint access to trusted IPs
- **VPN/Private Network:** Deploy admin endpoints behind VPN when possible
- **Rate Limiting:** Implement rate limits to prevent abuse (future enhancement)
- **HTTPS Only:** Never expose admin endpoints over HTTP

### Authorization Filter

The `AdminAuthFilter` validates the API key on every request:

```csharp
[AdminAuth]  // This attribute enforces authentication
public async Task<IActionResult> ReplayDeadLetters([FromBody] List<long> messageIds)
```

## Limitations

### Current Limitations

- **Max Batch Size:** 100 messages per request
- **No Pagination:** Must specify exact message IDs
- **Sequential Processing:** Not parallelized (future enhancement)
- **No Transaction:** Partial failures possible in batch operations
- **No Scheduling:** Must trigger manually or via external scheduler

### Future Enhancements

- [ ] Bulk replay by filter criteria (e.g., "all messages with error X")
- [ ] Scheduled automatic retry of transient failures
- [ ] Replay history tracking (who, when, what)
- [ ] Dry-run mode to preview replay without executing
- [ ] Webhook notification on replay completion
- [ ] Rate limiting on admin endpoints
- [ ] Multi-factor authentication for admin operations
- [ ] Audit log export API

## Testing

### Manual Testing

```bash
# Test authentication
curl -X POST https://your-domain.com/admin/replay-dead-letters \
  -H "Content-Type: application/json" \
  -d '{"messageIds": [1]}'
# Expected: 401 Unauthorized

# Test with valid key
curl -X POST https://your-domain.com/admin/replay-dead-letters \
  -H "X-Admin-Api-Key: test-key" \
  -H "Content-Type: application/json" \
  -d '{"messageIds": [1]}'
# Expected: 200 OK or 400 if message doesn't exist

# Test batch size limit
curl -X POST https://your-domain.com/admin/replay-dead-letters \
  -H "X-Admin-Api-Key: test-key" \
  -H "Content-Type: application/json" \
  -d '{"messageIds": [1,2,3,...,101]}'
# Expected: 400 Bad Request - "Maximum batch size is 100 messages"
```

### Integration Tests

TODO: Add automated tests for:
- Admin API key validation (missing, invalid, valid)
- Batch size limits (0, 1, 100, 101)
- Replay strategy selection (original exists vs. create new)
- Idempotency key generation for new messages
- Error handling for missing dead letter messages
- Response format validation

## Troubleshooting

### "Admin API key required"

- Verify `X-Admin-Api-Key` header is present
- Check header name is exact (case-sensitive)
- Ensure header value is not empty

### "Invalid admin API key"

- Verify key matches `App.AdminApiKey` in configuration
- Check for trailing whitespace in config or header
- Confirm appsettings.json is being loaded correctly

### "Message not found"

- Verify dead letter message ID exists in DeadLetterEmail table
- Check if message was already replayed and removed from DLQ
- Confirm database connection is using correct environment

### Partial Batch Failures

- Check individual `results` array for specific error messages
- Verify all message IDs are valid DeadLetterEmail primary keys
- Review logs for detailed error context

## Related Documentation

- [Retry/Backoff/DLQ Pipeline](retry-backoff-dlq.md) - Understanding dead letter queue
- [Brevo Webhook Integration](brevo-webhook-integration.md) - Delivery status tracking
- [Queue Architecture](PRD-18-queue-architecture.md) - Overall queue system design
