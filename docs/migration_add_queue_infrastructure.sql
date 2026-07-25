-- Migration: Add queue-based email processing infrastructure
-- Created: 2026-07-25
-- Ticket: PRD-18 - Host/runtime upgrade with queue consumer core

-- ============================================================================
-- OutboundEmailQueue Table
-- ============================================================================
-- Stores queued outbound email jobs with retry/lock semantics

CREATE TABLE IF NOT EXISTS "OutboundEmailQueue" (
    "id" BIGSERIAL PRIMARY KEY,
    "idempotency_key" TEXT NOT NULL UNIQUE,
    "template_key" TEXT NOT NULL,
    "recipient_email" TEXT NOT NULL,
    "template_variables" JSONB NOT NULL DEFAULT '{}'::jsonb,
    "status" TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'completed', 'dead_letter')),
    "retry_count" INTEGER NOT NULL DEFAULT 0,
    "created_at" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "locked_until" TIMESTAMPTZ,
    "last_error" TEXT
);

-- Index for efficient queue consumption
CREATE INDEX IF NOT EXISTS "idx_outbound_email_queue_status_lock" 
ON "OutboundEmailQueue" ("status", "locked_until", "retry_count") 
WHERE status = 'pending';

-- Index for idempotency lookups
CREATE INDEX IF NOT EXISTS "idx_outbound_email_queue_idempotency" 
ON "OutboundEmailQueue" ("idempotency_key");

-- Index for dead letter monitoring
CREATE INDEX IF NOT EXISTS "idx_outbound_email_queue_dead_letter" 
ON "OutboundEmailQueue" ("status", "created_at") 
WHERE status = 'dead_letter';

-- ============================================================================
-- EmailDeliveryLog Table
-- ============================================================================
-- Audit log for all email delivery attempts with full variable retention

CREATE TABLE IF NOT EXISTS "EmailDeliveryLog" (
    "id" BIGSERIAL PRIMARY KEY,
    "idempotency_key" TEXT NOT NULL,
    "queue_message_id" BIGINT REFERENCES "OutboundEmailQueue"("id") ON DELETE SET NULL,
    "template_key" TEXT NOT NULL,
    "brevo_template_id" BIGINT,
    "recipient_email" TEXT NOT NULL,
    "template_variables" JSONB NOT NULL DEFAULT '{}'::jsonb,
    "status" TEXT NOT NULL DEFAULT 'pending' CHECK (status IN (
        'pending', 'sent', 'delivered', 'opened', 'clicked', 
        'bounced', 'blocked', 'spam', 'unsubscribed', 'failed'
    )),
    "brevo_message_id" TEXT,
    "sent_at" TIMESTAMPTZ,
    "delivered_at" TIMESTAMPTZ,
    "bounced_at" TIMESTAMPTZ,
    "error_message" TEXT,
    "created_at" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "updated_at" TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Index for idempotency checks
CREATE INDEX IF NOT EXISTS "idx_email_delivery_log_idempotency" 
ON "EmailDeliveryLog" ("idempotency_key", "status");

-- Index for delivery tracking
CREATE INDEX IF NOT EXISTS "idx_email_delivery_log_brevo_message_id" 
ON "EmailDeliveryLog" ("brevo_message_id");

-- Index for recipient history
CREATE INDEX IF NOT EXISTS "idx_email_delivery_log_recipient" 
ON "EmailDeliveryLog" ("recipient_email", "created_at");

-- ============================================================================
-- Row Level Security (RLS)
-- ============================================================================
-- Enable RLS on both tables

ALTER TABLE "OutboundEmailQueue" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "EmailDeliveryLog" ENABLE ROW LEVEL SECURITY;

-- Service role policy for queue operations
CREATE POLICY "service_role_all_outbound_email_queue" 
ON "OutboundEmailQueue"
FOR ALL 
TO service_role
USING (true)
WITH CHECK (true);

-- Service role policy for delivery log operations
CREATE POLICY "service_role_all_email_delivery_log" 
ON "EmailDeliveryLog"
FOR ALL 
TO service_role
USING (true)
WITH CHECK (true);

-- ============================================================================
-- Comments
-- ============================================================================

COMMENT ON TABLE "OutboundEmailQueue" IS 'Queue of outbound email jobs with retry and dead-letter support';
COMMENT ON COLUMN "OutboundEmailQueue"."idempotency_key" IS 'Globally unique key for idempotent message processing';
COMMENT ON COLUMN "OutboundEmailQueue"."template_key" IS 'Internal template key (resolved to Brevo template ID by service)';
COMMENT ON COLUMN "OutboundEmailQueue"."locked_until" IS 'Timestamp until which message is locked by a consumer';
COMMENT ON COLUMN "OutboundEmailQueue"."status" IS 'Message status: pending, completed, or dead_letter';

COMMENT ON TABLE "EmailDeliveryLog" IS 'Comprehensive audit log of all email delivery attempts and outcomes';
COMMENT ON COLUMN "EmailDeliveryLog"."idempotency_key" IS 'Same key as queue message for deduplication';
COMMENT ON COLUMN "EmailDeliveryLog"."template_variables" IS 'Full JSON payload retained for troubleshooting';
COMMENT ON COLUMN "EmailDeliveryLog"."status" IS 'Delivery lifecycle status from pending through final outcome';
