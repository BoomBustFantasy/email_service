# Brevo Webhook Integration

## Overview

The email service implements authenticated webhook ingestion from Brevo to track email delivery lifecycle events and maintain suppression lists. This enables real-time delivery status updates and automatic suppression management.

## Architecture

### Webhook Endpoint

**POST `/webhooks/brevo`**

Receives webhook events from Brevo with HMAC-SHA256 signature verification.

### Components

#### 1. **BrevoWebhookController**

Handles incoming webhook requests with signature validation and event processing.

**Key Methods:**
- `HandleBrevoWebhook()` - Main webhook endpoint
- `ValidateWebhookSignature()` - HMAC-SHA256 signature verification
- `ProcessWebhookEventAsync()` - Routes events to appropriate handlers
- `UpdateDeliveryLogAsync()` - Updates EmailDeliveryLog status
- `HandleSuppressionEventAsync()` - Creates EmailSuppression records

#### 2. **EmailSuppression Model**

Tracks email addresses that should not receive future communications.

**Fields:**
- `id` - Primary key
- `email` - Suppressed email address
- `reason` - Suppression type (unsubscribe, hard_bounce, soft_bounce, invalid, spam, blocked)
- `suppressed_at` - Timestamp
- `details` - Additional context from Brevo

**Unique Constraint:** `(email, reason)` - Prevents duplicate suppressions

## Event Mapping

### Status Updates

Brevo webhook events are mapped to EmailDeliveryLog statuses:

| Brevo Event      | Delivery Log Status | Notes                          |
|------------------|---------------------|--------------------------------|
| `request`        | `pending`           | Email queued at Brevo          |
| `delivered`      | `delivered`         | Successfully delivered         |
| `soft_bounce`    | `bounced`           | Temporary delivery failure     |
| `hard_bounce`    | `bounced`           | Permanent delivery failure     |
| `invalid_email`  | `bounced`           | Invalid recipient address      |
| `deferred`       | `pending`           | Delivery delayed               |
| `click`          | `clicked`           | Recipient clicked link         |
| `opened`         | `opened`            | Recipient opened email         |
| `unique_opened`  | `opened`            | First open by recipient        |
| `unsubscribed`   | `unsubscribed`      | Recipient unsubscribed         |
| `complaint`      | `spam`              | Marked as spam                 |
| `blocked`        | `blocked`           | Blocked by recipient server    |
| `error`          | `failed`            | General delivery error         |

### Suppression Events

The following events trigger automatic suppression list updates:

- **hard_bounce** → `hard_bounce`
- **soft_bounce** → `soft_bounce`
- **invalid_email** → `invalid`
- **unsubscribed** → `unsubscribe`
- **complaint** → `spam`
- **blocked** → `blocked`

## Security

### Signature Verification

Webhooks are verified using HMAC-SHA256:

1. Extract `X-Brevo-Signature` header
2. Compute HMAC-SHA256 of request body using `Brevo.WebhookSecret`
3. Compare computed signature with provided signature
4. Reject request if signatures don't match

**Development Mode:** If `Brevo.WebhookSecret` is empty, signature validation is skipped (logs warning).

### Configuration

Add to `appsettings.json`:

```json
{
  "Brevo": {
    "ApiKey": "your-brevo-api-key",
    "FromEmail": "noreply@example.com",
    "FromName": "Your App",
    "WebhookSecret": "your-webhook-secret-from-brevo"
  }
}
```

**Important:** Never commit real webhook secrets to source control. Use environment variables or secret management.

## Database Schema

### EmailSuppression Table

```sql
CREATE TABLE "EmailSuppression" (
    "id" BIGSERIAL PRIMARY KEY,
    "email" TEXT NOT NULL,
    "reason" TEXT NOT NULL CHECK (reason IN (
        'unsubscribe', 'hard_bounce', 'soft_bounce', 
        'invalid', 'spam', 'blocked', 'unknown'
    )),
    "suppressed_at" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "details" TEXT
);

CREATE UNIQUE INDEX "idx_email_suppression_email_reason" 
ON "EmailSuppression" ("email", "reason");
```

### RLS Policy

Service role has full access:

```sql
CREATE POLICY "service_role_all_email_suppression" 
ON "EmailSuppression"
FOR ALL 
TO service_role
USING (true)
WITH CHECK (true);
```

## Brevo Webhook Setup

### 1. Configure Webhook in Brevo Dashboard

1. Go to **Settings → Webhooks** in Brevo
2. Click **Add Webhook**
3. Set URL: `https://your-domain.com/webhooks/brevo`
4. Select events:
   - **Delivered**
   - **Hard bounces**
   - **Soft bounces**
   - **Spam**
   - **Unsubscribe**
   - **Opened**
   - **Clicked**
5. Generate webhook secret and copy to `appsettings.json`
6. Save webhook

### 2. Test Webhook

Send a test event from Brevo dashboard to verify:
- Signature verification passes
- Events are logged
- EmailDeliveryLog is updated

### 3. Monitor Webhook Health

Check logs for:
```
Received Brevo webhook: event={Event}, email={Email}, messageId={MessageId}
Updated delivery log {LogId}: status={Status}, email={Email}
Added email suppression: email={Email}, reason={Reason}
```

## Delivery Status Lifecycle

```
pending → sent → delivered → [opened] → [clicked]
        ↓
        bounced
        spam
        blocked
        failed
        unsubscribed
```

**Timestamps:**
- `sent_at` - Set on `sent` status
- `delivered_at` - Set on `delivered` status
- `bounced_at` - Set on `bounced` status

## Suppression Management

### Checking Suppression Status

To check if an email is suppressed before sending:

```csharp
var suppressed = await supabaseClient
    .From<EmailSuppression>()
    .Where(x => x.Email == recipientEmail)
    .Get();

if (suppressed.Models.Count > 0)
{
    // Email is suppressed, don't send
}
```

### Suppression Reasons

- **unsubscribe** - User explicitly unsubscribed
- **hard_bounce** - Permanent delivery failure (invalid address)
- **soft_bounce** - Temporary delivery failure (full inbox, etc.)
- **invalid** - Email address format invalid
- **spam** - Marked as spam by recipient
- **blocked** - Blocked by recipient's email server

### Handling Suppressions

**Best Practice:** Check EmailSuppression table before queuing emails in OutboundEmailQueue.

**Future Enhancement:** Automatic filtering in QueueConsumerService before calling Brevo API.

## Error Handling

### Invalid Signature

Returns `401 Unauthorized` with message:
```json
{
  "error": "Invalid webhook signature"
}
```

### Missing Message ID

If `messageId` is not provided:
```
Delivery log not found for Brevo message ID: {MessageId}
```

Event is still processed (suppression updates, etc.) but delivery log is not updated.

### Database Errors

Logged but webhook returns `500 Internal Server Error` to trigger Brevo retry.

## Monitoring

### Key Metrics

- Webhook requests received (by event type)
- Signature validation failures
- Delivery log updates
- Suppression additions
- Processing errors

### Logging

All webhook events are logged with:
- Event type
- Recipient email
- Brevo message ID
- Processing outcome

Example:
```
[Information] Received Brevo webhook: event=delivered, email=user@example.com, messageId=abc123
[Information] Updated delivery log 456: status=delivered, email=user@example.com
[Warning] Added email suppression: email=bounced@example.com, reason=hard_bounce
```

## Testing

### Manual Testing

Use Brevo's webhook test feature or simulate with curl:

```bash
curl -X POST https://your-domain.com/webhooks/brevo \
  -H "Content-Type: application/json" \
  -H "X-Brevo-Signature: <computed-signature>" \
  -d '{
    "event": "delivered",
    "email": "test@example.com",
    "messageId": "test-message-123",
    "date": "2026-07-25T10:00:00Z"
  }'
```

### Integration Tests

TODO: Add automated tests for:
- Signature verification (valid/invalid)
- Event type mapping
- Delivery log updates
- Suppression creation
- Duplicate suppression handling

## Troubleshooting

### Webhook Not Receiving Events

1. Check Brevo webhook configuration (URL, events selected)
2. Verify firewall/network allows Brevo IPs
3. Check application logs for errors
4. Test with Brevo's "Send Test" button

### Signature Validation Failing

1. Verify `Brevo.WebhookSecret` matches Brevo dashboard
2. Check for whitespace in secret value
3. Ensure request body is read without modification
4. Verify `X-Brevo-Signature` header is present

### Delivery Logs Not Updating

1. Confirm `messageId` is present in webhook payload
2. Check EmailDeliveryLog for matching `brevo_message_id`
3. Verify Brevo message ID was stored during email sending
4. Check database RLS policies allow service_role updates

### Duplicate Suppressions

Normal behavior - unique constraint prevents duplicates. Logged as debug:
```
Suppression already exists: email={Email}, reason={Reason}
```

## Future Enhancements

- [ ] Automatic suppression filtering in QueueConsumerService
- [ ] Webhook retry handling with exponential backoff
- [ ] Suppression list management API
- [ ] Webhook event replay for failed processing
- [ ] Metrics dashboard for webhook health
- [ ] Rate limiting on webhook endpoint
- [ ] Webhook event batching for high-volume senders
