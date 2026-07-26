---
name: Developer
description: General-purpose developer agent for the email service application. Use this agent for implementing features, fixing bugs, and writing C# code.
model: Claude Sonnet 4.5 (copilot)
---

# Email Service Developer Guide

## 🎯 Overview

The **email_service** is a production-grade ASP.NET Core application that handles transactional email delivery for the Boom Bust Fantasy Football platform. It combines a **web API** with a **background queue consumer** to provide reliable, idempotent, multi-consumer-safe email processing with retry logic and dead letter queue (DLQ) management.

### Key Capabilities

- **Queue-Based Processing:** Messages are processed from a PostgreSQL-backed queue (pgmq) with automatic retry and DLQ
- **Idempotent Delivery:** Prevents duplicate emails using unique idempotency keys
- **Template Management:** Type-safe template contracts with validation and Brevo integration
- **Webhook Handling:** Receives and processes Brevo delivery events (delivered, bounced, opened, etc.)
- **Health Monitoring:** Metrics, health checks, and operational alerts
- **Multi-Consumer Safe:** Horizontally scalable with optimistic locking

---

## 🏗️ Architecture

### Project Type
**Web Application** (`Microsoft.NET.Sdk.Web`) running on .NET 9.0

The application simultaneously:
1. Serves HTTP API endpoints (health checks, webhooks, admin operations)
2. Executes background queue consumer as a hosted service
3. Provides real-time metrics and monitoring endpoints

### Core Design Pattern: Queue-Based Processing

```
Producer (external system) 
    ↓
OutboundEmailQueue (pgmq)
    ↓
QueueConsumerService (polls every 5s)
    ↓
Lock message (5min visibility timeout)
    ↓
Check idempotency (EmailDeliveryLog)
    ↓
Check suppression list
    ↓
Send via Brevo API
    ↓
Log delivery attempt
    ↓
Archive message OR retry with backoff
    ↓ (if max retries exceeded)
Move to Dead Letter Queue
```

### Reliability Features

1. **Exponential Backoff Retry**
   - Base delay: 1 second
   - Multiplier: 2.0x
   - Max delay: 60 seconds
   - Max attempts: 3

2. **Idempotency**
   - Every message has a unique `idempotency_key`
   - Processed messages are logged and skipped on retry
   - Prevents duplicate sends across multiple consumers

3. **Dead Letter Queue**
   - Messages that fail 3+ times move to DLQ
   - Admin API allows replaying DLQ messages
   - Operational alerts fire when DLQ exceeds threshold

4. **Multi-Consumer Safety**
   - Optimistic locking with `locked_until` timestamp
   - Atomic compare-and-swap operations
   - Multiple service instances can safely consume the same queue

---

## 🛠️ Tech Stack

### Core Framework
- **ASP.NET Core 9.0** - Web host and dependency injection
- **C# 12** - Language features (required properties, record types)

### Dependencies
- **Supabase** - PostgreSQL database client with Postgrest ORM
  - `Supabase` NuGet package
  - Admin Auth for user email lookups
  
- **Brevo** (formerly Sendinblue) - Transactional email provider
  - REST API integration via `HttpClient`
  - Template-based email sending
  - Webhook event processing

- **pgmq** - PostgreSQL Message Queue
  - Managed via Supabase RPC functions
  - Provides visibility timeouts and message archiving

- **BoomBust.Logging** - Custom structured logging
  - File-based logging with rotation
  - Configurable log levels per namespace

### Development Tools
- **xUnit** - Test framework
- **FluentAssertions** - Assertion library for tests

---

## 📁 Project Structure

```
EmailService/
├── Program.cs                    # Application entry point, DI configuration
├── appsettings.json             # Configuration (gitignored, use template)
├── appsettings.template.json    # Configuration template (committed)
│
├── Configs/                     # Configuration models
│   ├── AppConfig.cs            # Base URL, admin API key
│   ├── BrevoConfig.cs          # API key, webhook secret, sender info
│   ├── QueueReliabilityConfig.cs  # Retry, backoff, alert thresholds
│   └── SupabaseConfig.cs       # URL, API key, service role key
│
├── Controllers/                 # HTTP API endpoints
│   ├── AdminController.cs      # Dead letter replay, metrics
│   └── BrevoWebhookController.cs  # Webhook event receiver
│
├── DTOs/                        # Data transfer objects
│   ├── BrevoWebhookEvent.cs    # Webhook payload model
│   ├── EmailMessage.cs         # Generic email DTO
│   ├── TeamReviewEmailInfo.cs  # Team review notification info
│   ├── TeamReviewNotificationInfo.cs
│   └── TradeEmailInfo.cs       # Trade review notification info
│
├── Filters/                     # ASP.NET filters
│   └── AdminAuthAttribute.cs   # API key authentication filter
│
├── Services/                    # Business logic services
│   ├── IEmailService.cs        # Email sending abstraction
│   ├── BrevoEmailService.cs    # Brevo API implementation
│   ├── ISupabaseService.cs     # Database operations abstraction
│   ├── SupabaseService.cs      # Supabase database implementation
│   ├── QueueConsumerService.cs # Background queue processor (hosted service)
│   ├── QueueConsumerHealthCheck.cs  # Health check for queue
│   ├── QueueMetrics.cs         # Thread-safe metrics tracking
│   └── ReviewEmailFactory.cs   # Factory for review email construction
│
├── SupabaseModels/              # Database table models
│   ├── EmailDeliveryLog.cs     # Delivery attempt tracking
│   ├── EmailOutbox.cs          # Outbound message queue
│   ├── EmailSuppression.cs     # Unsubscribe/bounce suppression
│   ├── TeamReview.cs           # Team review records
│   ├── TradeReview.cs          # Trade review records
│   ├── User.cs                 # User account data
│   └── Enums/
│       └── AdviceStatus.cs     # Review status enumeration
│
└── Templates/                   # Template contract system
    ├── ITemplateContract.cs    # Base contract interface
    ├── ITemplateService.cs     # Template resolution service
    ├── TemplateService.cs      # Implementation
    ├── TemplateConfig.cs       # Template ID mapping configuration
    ├── SenderIdentity.cs       # Email sender info model
    └── Contracts/              # Concrete template implementations
        ├── PurchaseConfirmationContract.cs
        ├── TeamReviewNotificationContract.cs
        └── TradeOfferNotificationContract.cs

EmailService.Tests/
├── BehavioralSpecifications.cs  # Behavioral tests (no mocking)
├── Controllers/                 # (future: controller tests)
├── Services/                    # (future: service tests)
└── Templates/                   # (future: template validation tests)
```

---

## 🔧 Core Components Deep Dive

### 1. Program.cs - Application Bootstrap

**Key Responsibilities:**
- Configure dependency injection
- Register services (Supabase, Brevo, Template Service, Queue Consumer)
- Setup logging with BoomBust.Logging
- Register health checks
- Configure HTTP pipeline
- Map endpoints (health, metrics, status, controllers)

**Critical Services Registered:**
```csharp
// Singleton - Supabase client
builder.Services.AddSingleton<Client>(/* Supabase config */);

// Singleton - Template service, Queue metrics
builder.Services.AddSingleton<ITemplateService, TemplateService>();
builder.Services.AddSingleton<QueueMetrics>();

// Scoped - Database and email services
builder.Services.AddScoped<ISupabaseService, SupabaseService>();
builder.Services.AddScoped<IEmailService, BrevoEmailService>();

// Hosted - Background queue consumer
builder.Services.AddHostedService<QueueConsumerService>();
```

**Endpoints:**
- `GET /` - Service status (name, version, timestamp)
- `GET /health` - Health check endpoint
- `GET /metrics` - Queue processing metrics
- `POST /webhooks/brevo` - Brevo webhook receiver
- `POST /admin/replay-dead-letters` - Admin: replay DLQ messages

---

### 2. QueueConsumerService - Background Processor

**Location:** `Services/QueueConsumerService.cs`

**Pattern:** ASP.NET Core `BackgroundService` (implements `IHostedService`)

**Processing Flow:**
1. **Poll Queue:** Every 5 seconds, read batch of 10 messages from pgmq
2. **Lock Messages:** Each message locked for 5 minutes (visibility timeout)
3. **Idempotency Check:** Skip if already successfully processed
4. **Suppression Check:** Skip if recipient is on suppression list
5. **Send Email:** Call Brevo API via `IEmailService`
6. **Log Attempt:** Record in `EmailDeliveryLog` with status
7. **Archive or Retry:** Archive if success, increment retry count if failure
8. **Dead Letter:** Move to DLQ if retry count ≥ 3

**Key Methods:**
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
// Main loop - runs until app shutdown

private async Task ProcessQueueBatchAsync(CancellationToken cancellationToken)
// Read and process batch of messages

private async Task<List<PgmqMessage>> ReadMessagesFromPgmq(Client supabaseClient, int batchSize)
// Call read_email_queue RPC function

private async Task ProcessMessageAsync(PgmqMessage message, Client supabaseClient, CancellationToken cancellationToken)
// Process single message with idempotency, suppression, retry logic

private async Task CheckOperationalAlertsAsync(Client supabaseClient, CancellationToken cancellationToken)
// Monitor success rate and DLQ size, send alerts if degraded
```

**Metrics Tracking:**
- Total processed
- Total successful
- Total failed
- Total retries
- Total dead lettered
- Current queue depth
- Success rate (calculated)

---

### 3. BrevoEmailService - Email Sending

**Location:** `Services/BrevoEmailService.cs`

**Implements:** `IEmailService`

**Methods:**
1. **`SendEmailAsync(to, subject, body, fromDisplayName)`**
   - Basic email sending (text content)
   - Used for simple notifications
   
2. **`SendTemplateEmailAsync(to, templateId, templateParams)`**
   - Send using Brevo template ID
   - Pass template variables as dictionary
   
3. **`SendTemplateEmailAsync(ITemplateContract contract)`**
   - **Recommended approach** - Type-safe template contracts
   - Validates contract before sending
   - Resolves template ID from internal key
   - Applies sender identity (override or default)

**Brevo API Integration:**
- Base URL: `https://api.brevo.com/v3/smtp/email`
- Authentication: `api-key` header
- Request format: JSON with `sender`, `to`, `templateId`, `params`
- Response: HTTP 201 on success, error details on failure

**Template Contract Flow:**
```csharp
// 1. Create contract
var contract = new TeamReviewNotificationContract
{
    RecipientEmail = "user@example.com",
    RecipientName = "John Doe",
    ReviewerName = "Jane Analyst",
    TeamName = "My Team",
    LeagueName = "My League",
    ReviewUrl = "https://app.com/review/123"
};

// 2. Send (validation happens automatically)
await emailService.SendTemplateEmailAsync(contract);
```

---

### 4. SupabaseService - Database Operations

**Location:** `Services/SupabaseService.cs`

**Implements:** `ISupabaseService`

**Key Responsibilities:**
- Query review records (TradeReview, TeamReview)
- Fetch user emails via Admin Auth
- Mark records as processed (email_sent, reviewer_notified, purchase_confirmed)
- Provide DTOs with joined user email data

**Pattern - Two-Step Fetch:**
```csharp
// 1. Query Supabase table for records
var reviews = await _supabase
    .From<TeamReview>()
    .Where(review => review.EmailSent == false)
    .Where(review => review.YoutubeLink != null)
    .Select("id,user_id,youtube_link")
    .Get();

// 2. For each record, fetch user email via Admin Auth
foreach (var review in reviews.Models)
{
    var user = await _adminAuth.GetUserById(review.UserId.ToString());
    
    if (!string.IsNullOrWhiteSpace(user?.Email))
    {
        result.Add(new TeamReviewEmailInfo
        {
            Id = review.Id,
            Email = user.Email,
            YoutubeLink = review.YoutubeLink
        });
    }
}
```

**Admin Auth Requirement:**
User emails are stored in Supabase Auth (not in public tables), so service role key + Admin Auth client are required for email lookups.

---

### 5. Template System

**Location:** `Templates/` directory

**Purpose:** Provide type-safe, validated email template contracts that map to Brevo templates

#### Architecture Components

**a) ITemplateContract** - Base interface
```csharp
public interface ITemplateContract
{
    string TemplateKey { get; }           // Internal key (e.g., "team-review-notification")
    SenderIdentity? SenderOverride { get; init; }  // Optional sender override
    
    bool Validate(out List<string> errors);  // Validation logic
    Dictionary<string, string> ToBrevoParams();  // Convert to Brevo parameters
}
```

**b) Template Contracts** - Concrete implementations
- `TeamReviewNotificationContract` - Notify customer of completed team review
- `TradeOfferNotificationContract` - Notify customer of completed trade review
- `PurchaseConfirmationContract` - Confirm purchase submission

**c) TemplateService** - Resolution and validation
```csharp
public interface ITemplateService
{
    long ResolveTemplateId(string templateKey);  // Map key → Brevo ID
    SenderIdentity GetSenderIdentity(ITemplateContract contract);  // Apply overrides
    void ValidateContract(ITemplateContract contract);  // Validate before send
}
```

**d) TemplateConfig** - Configuration
```json
"Templates": {
    "TemplateIdMap": {
        "team-review-notification": 1,
        "trade-offer-notification": 2,
        "purchase-confirmation": 3
    },
    "DefaultSender": {
        "Email": "noreply@boombustfantasy.com",
        "Name": "Boom Bust Fantasy"
    }
}
```

#### Benefits

1. **Type Safety:** Required fields enforced by C# type system
2. **Validation:** Catch errors before API call (email format, required fields)
3. **Maintainability:** Template changes don't require hunting for magic numbers
4. **Testability:** Contracts can be unit tested independently
5. **Documentation:** Contract properties self-document required variables

---

### 6. Webhook Processing

**Location:** `Controllers/BrevoWebhookController.cs`

**Endpoint:** `POST /webhooks/brevo`

**Flow:**
1. **Validate Signature:** HMAC-SHA256 signature in `X-Brevo-Signature` header
2. **Parse Event:** Deserialize `BrevoWebhookEvent` from request body
3. **Update Delivery Log:** Map event type to status, update `EmailDeliveryLog`
4. **Handle Suppression:** For unsubscribe/bounce/spam events, add to `EmailSuppression` table

**Event Type Mapping:**
```csharp
"request" => "pending"
"delivered" => "delivered"
"soft_bounce" => "bounced"
"hard_bounce" => "bounced"
"opened" => "opened"
"click" => "clicked"
"spam" => "spam"
"unsubscribe" => "unsubscribed"
```

**Suppression Handling:**
When a hard bounce, spam report, or unsubscribe occurs, the email is added to the `EmailSuppression` table. Future messages to that email are skipped by the queue consumer.

---

## 🗄️ Database Schema

### Core Tables

#### email_outbox
**Purpose:** Queue of outbound email messages (pgmq table)

| Column | Type | Description |
|--------|------|-------------|
| id | GUID | Primary key |
| idempotency_key | TEXT | Unique key for deduplication |
| template_key | TEXT | Internal template identifier |
| recipient_email | TEXT | Recipient email address |
| template_variables | JSONB | Template parameters |
| classification | TEXT | "transactional" or "marketing" |
| schema_version | INT | Schema version for migrations |
| published_at | TIMESTAMP | When message was enqueued |
| created_at | TIMESTAMP | Record creation time |

#### email_delivery_log
**Purpose:** Log of delivery attempts (success, failure, retry)

| Column | Type | Description |
|--------|------|-------------|
| id | GUID | Primary key |
| outbox_id | GUID | Reference to outbox message |
| idempotency_key | TEXT | Matches outbox idempotency key |
| attempt_number | INT | Retry attempt number (1, 2, 3) |
| status | TEXT | "pending", "sent", "delivered", "bounced", etc. |
| error_message | TEXT | Error details if failed |
| external_id | TEXT | Brevo message ID |
| attempted_at | TIMESTAMP | When attempt was made |

#### email_suppression
**Purpose:** Unsubscribe/bounce/spam suppression list

| Column | Type | Description |
|--------|------|-------------|
| email | TEXT | Suppressed email address (PK) |
| reason | TEXT | "hard_bounce", "spam", "unsubscribe" |
| created_at | TIMESTAMP | When suppression was added |

#### TradeReview
**Purpose:** Trade review records from main app

| Column | Type | Description |
|--------|------|-------------|
| id | BIGINT | Primary key |
| user_id | UUID | User who requested review |
| reviewer_id | UUID | Assigned reviewer |
| status | TEXT | "pending", "complete", etc. |
| email_sent | BOOLEAN | Completion email sent flag |
| reviewer_notified | BOOLEAN | Reviewer assigned notification flag |
| purchase_confirmed | BOOLEAN | Purchase confirmation email sent |

#### TeamReviews
**Purpose:** Team review records from main app

| Column | Type | Description |
|--------|------|-------------|
| id | BIGINT | Primary key |
| user_id | UUID | User who requested review |
| reviewer_id | UUID | Assigned reviewer |
| youtube_link | TEXT | Video link when completed |
| email_sent | BOOLEAN | Completion email sent flag |
| reviewer_notified | BOOLEAN | Reviewer assigned notification flag |
| purchase_confirmed | BOOLEAN | Purchase confirmation email sent |

### RPC Functions (Supabase)

**`read_email_queue(p_batch_size INT, p_visibility_timeout_seconds INT)`**
- Reads batch of messages from pgmq
- Locks messages for visibility timeout duration
- Returns array of `PgmqMessage` objects

**`archive_email_message(p_msg_id BIGINT)`**
- Removes message from queue (successfully processed)

**`move_to_dead_letter(p_msg_id BIGINT)`**
- Moves message to dead letter table
- Called when max retries exceeded

---

## ⚙️ Configuration

### appsettings.json Structure

```json
{
    "App": {
        "BaseUrl": "https://boombustfantasy.com",
        "AdminApiKey": "secure-random-key-here"
    },
    "Brevo": {
        "ApiKey": "your-brevo-api-key",
        "FromEmail": "hello@boombustfantasy.com",
        "FromName": "Boom Bust Fantasy",
        "WebhookSecret": "your-webhook-secret"
    },
    "Templates": {
        "TemplateIdMap": {
            "team-review-notification": 1,
            "trade-offer-notification": 2,
            "purchase-confirmation": 3
        },
        "DefaultSender": {
            "Email": "noreply@boombustfantasy.com",
            "Name": "Boom Bust Fantasy"
        }
    },
    "QueueReliability": {
        "MaxRetryAttempts": 3,
        "BaseBackoffMs": 1000,
        "BackoffMultiplier": 2.0,
        "MaxBackoffMs": 60000,
        "OperationalAlertEmail": "ops@boombustfantasy.com",
        "SuccessRateDegradationThreshold": 0.8,
        "DeadLetterQueueSizeThreshold": 10
    },
    "Supabase": {
        "Url": "https://your-project.supabase.co",
        "ApiKey": "your-anon-key",
        "ServiceRoleKey": "your-service-role-key"
    }
}
```

### Configuration Classes

All configuration sections are bound to strongly-typed classes in `Configs/`:

- **AppConfig** - Application-level settings
- **BrevoConfig** - Email provider configuration
- **TemplateConfig** - Template ID mapping and default sender
- **QueueReliabilityConfig** - Retry, backoff, and alerting thresholds
- **SupabaseConfig** - Database connection settings

---

## 🔄 Development Workflow

### Running Locally

1. **Copy Configuration Template**
   ```powershell
   Copy-Item EmailService/appsettings.template.json EmailService/appsettings.json
   ```

2. **Fill in Configuration Values**
   - Add Brevo API key
   - Add Supabase credentials (URL, service role key)
   - Add admin API key (generate random secure string)

3. **Run Application**
   ```powershell
   cd EmailService
   dotnet run
   ```

4. **Verify Startup**
   - Check logs: `logs/email-service-YYYYMMDD.txt`
   - Hit status endpoint: `http://localhost:5000/`
   - Check health: `http://localhost:5000/health`
   - View metrics: `http://localhost:5000/metrics`

### Testing Workflow

**Run All Tests:**
```powershell
cd EmailService.Tests
dotnet test
```

**Run Specific Test Class:**
```powershell
dotnet test --filter QueueProcessingBehaviorTests
```

**Run With Detailed Output:**
```powershell
dotnet test --logger "console;verbosity=detailed"
```

### Debugging Queue Processing

1. **Check Queue Depth:**
   ```
   GET http://localhost:5000/metrics
   ```
   Look at `current_queue_depth`

2. **View Success Rate:**
   ```json
   {
     "queue_metrics": {
       "total_processed": 45,
       "total_successful": 43,
       "total_failed": 2,
       "success_rate": 0.9556
     }
   }
   ```

3. **Inspect Delivery Logs:**
   Query Supabase `email_delivery_log` table for attempt history

4. **Check Dead Letter Queue:**
   Query `DeadLetterEmail` table for messages that exhausted retries

---

## 🧪 Testing Strategy

### Behavioral Specifications
**Location:** `EmailService.Tests/BehavioralSpecifications.cs`

**Approach:** Test expected behaviors without complex mocking

**Coverage:**
- Exponential backoff calculations
- Dead letter queue triggering logic
- Idempotency key uniqueness
- Webhook event mapping

**Example:**
```csharp
[Theory]
[InlineData(0, 1000)]   // First retry: 1 second
[InlineData(1, 2000)]   // Second retry: 2 seconds
[InlineData(2, 4000)]   // Third retry: 4 seconds
public void ExponentialBackoff_CalculatesCorrectDelay(int retryCount, int expectedDelayMs)
{
    var baseBackoffMs = 1000;
    var backoffMultiplier = 2.0;
    
    var calculatedDelay = (int)(baseBackoffMs * Math.Pow(backoffMultiplier, retryCount));
    
    calculatedDelay.Should().Be(expectedDelayMs);
}
```

### Future Test Coverage (Planned)
- Controller integration tests
- Service unit tests with mocked dependencies
- Template contract validation tests
- End-to-end queue processing tests

---

## 🚨 Operational Monitoring

### Metrics Endpoint
**`GET /metrics`**

Returns real-time queue processing statistics:
```json
{
    "queue_metrics": {
        "total_processed": 1250,
        "total_successful": 1198,
        "total_failed": 52,
        "total_retries": 47,
        "total_dead_lettered": 5,
        "current_queue_depth": 12,
        "success_rate": 0.9584
    },
    "timestamp": "2026-07-26T10:30:00Z"
}
```

### Health Check Endpoint
**`GET /health`**

Returns service health status (used by orchestrators):
```json
{
    "status": "Healthy",
    "results": {
        "queue_consumer": {
            "status": "Healthy",
            "description": "Queue consumer is running"
        }
    }
}
```

### Operational Alerts

The queue consumer monitors for degradation and sends alerts via email:

**Alert Conditions:**
1. **Success Rate Degradation**
   - Triggers when success rate drops below threshold (default: 80%)
   - Calculated over all processed messages since startup

2. **Dead Letter Queue Size**
   - Triggers when DLQ size exceeds threshold (default: 10 messages)
   - Indicates systematic delivery failures

**Alert Recipient:** Configured in `QueueReliability.OperationalAlertEmail`

---

## 📊 Key Architectural Patterns

### 1. Idempotency Pattern
**Problem:** Prevent duplicate email sends when messages are retried or processed by multiple consumers

**Solution:**
- Every message has a unique `idempotency_key`
- Before sending, check `EmailDeliveryLog` for existing successful delivery with same key
- If found, skip sending and archive message
- If not found, proceed with send and log attempt

### 2. Optimistic Locking Pattern
**Problem:** Multiple consumers reading from the same queue could process the same message

**Solution:**
- pgmq provides visibility timeouts via `locked_until` timestamp
- When message is read, it's invisible to other consumers for 5 minutes
- If processing fails or times out, lock expires and message becomes visible again
- Idempotency key prevents duplicate sends if message is reprocessed

### 3. Template Contract Pattern
**Problem:** Template IDs are magic numbers, required variables are undocumented, no validation until runtime

**Solution:**
- Create strongly-typed contracts for each template
- Required properties enforced by C# type system (`required` keyword)
- Validation logic in contract (email format, required fields)
- Template key → ID mapping centralized in configuration
- Sender identity can be overridden per-template

### 4. Suppression List Pattern
**Problem:** Continue sending to unsubscribed or hard-bounced emails wastes resources and damages sender reputation

**Solution:**
- Maintain `EmailSuppression` table with suppressed emails
- Queue consumer checks suppression list before sending
- Webhook processor adds to suppression list on bounce/spam/unsubscribe events
- Suppressed messages are skipped and archived (not retried)

### 5. Admin Auth Pattern
**Problem:** User emails stored in Supabase Auth, not accessible via regular API

**Solution:**
- Use service role key to create Admin Auth client
- Fetch user records by UUID via `GetUserById()`
- Join email data with application tables in service layer
- Return DTOs with combined data to consumers

### 6. Background Service Pattern
**Problem:** Need continuous queue processing without blocking HTTP requests

**Solution:**
- Implement `BackgroundService` (ASP.NET Core hosted service)
- Override `ExecuteAsync()` with infinite loop + cancellation token
- Use scoped service provider to create scopes for each batch
- Resolve scoped services (Supabase, email service) per message
- Handle exceptions and log errors without crashing service

---

## 🔐 Security Considerations

### API Key Authentication
- Admin endpoints protected by `AdminAuthAttribute` filter
- Requires `X-Admin-Api-Key` header matching configured value
- Never commit real API keys to source control

### Webhook Signature Validation
- Brevo webhooks validated via HMAC-SHA256 signature
- Signature in `X-Brevo-Signature` header
- Computed using webhook secret + request body
- Prevents spoofed webhook events

### Service Role Key Protection
- Supabase service role key bypasses Row Level Security (RLS)
- Required for Admin Auth user lookups
- Must be kept secret and never exposed to client-side code
- Store in secure configuration (environment variables, Azure Key Vault)

### Email Suppression
- Honors unsubscribe requests via webhook processing
- Prevents sending to hard-bounced addresses
- Maintains GDPR compliance for opt-outs

---

## 📚 Key Documentation

- **Architecture Overview:** `docs/README.md`
- **Queue Architecture (PRD):** `docs/PRD-18-queue-architecture.md`
- **Implementation Checklist:** `docs/implementation-checklist.md`
- **Retry & DLQ System:** `docs/retry-backoff-dlq.md`
- **Template Contracts:** `docs/template-contracts.md`
- **Dead Letter Replay API:** `docs/dead-letter-replay-api.md`
- **Brevo Webhook Integration:** `docs/brevo-webhook-integration.md`

---

## 🎯 Developer Best Practices

### When Adding New Email Templates

1. **Create Brevo template** in Brevo UI
2. **Note numeric template ID** assigned by Brevo
3. **Create contract class** in `Templates/Contracts/`
   - Implement `ITemplateContract`
   - Add required properties with `required` keyword
   - Implement `Validate()` with business rules
   - Implement `ToBrevoParams()` to map properties

4. **Add template key mapping** in `appsettings.json`
   ```json
   "Templates": {
       "TemplateIdMap": {
           "new-template-key": 999
       }
   }
   ```

5. **Update producer** (external system) to publish messages with new template key

6. **Test end-to-end** with staging environment

### When Adding New Database Queries

1. **Define method signature** in `ISupabaseService`
2. **Implement in SupabaseService**
3. **Create DTO** if returning joined data
4. **Test query** in Supabase SQL editor first
5. **Add try-catch** with appropriate error logging

### When Modifying Queue Processing Logic

1. **Consider idempotency** - Will change break deduplication?
2. **Consider multi-consumer safety** - Could race conditions occur?
3. **Update metrics** if adding new counters
4. **Update health check** if adding new failure modes
5. **Test with high message volume** to ensure no performance regression

### When Changing Retry/Backoff Behavior

1. **Review operational impact** - More retries = higher load
2. **Update configuration documentation**
3. **Add behavioral test** for new logic
4. **Verify DLQ threshold** still makes sense
5. **Consider alert thresholds** may need adjustment

---

## 🚀 Future Enhancements (Planned)

### Short Term
- [ ] Purchase confirmation job (email on review submission)
- [ ] Review completion job (email when review is ready)
- [ ] Enhanced health checks (last processed timestamp, error rate)
- [ ] Admin UI for DLQ management

### Medium Term
- [ ] Scheduled batch email support (marketing campaigns)
- [ ] Email preview API (test templates before sending)
- [ ] Delivery analytics dashboard
- [ ] Template variable schema validation

### Long Term
- [ ] Multi-tenant support (multiple Brevo accounts)
- [ ] Email rendering service (HTML generation)
- [ ] A/B testing framework for email content
- [ ] Rate limiting per recipient (prevent spamming)

---

## 📞 Support & Resources

### Internal Resources
- **Repository:** https://github.com/BoomBustFantasy/email_service
- **Supabase Dashboard:** https://supabase.com/dashboard/project/[project-id]
- **Brevo Dashboard:** https://app.brevo.com/

### External Documentation
- **ASP.NET Core:** https://docs.microsoft.com/en-us/aspnet/core/
- **Supabase C# Client:** https://supabase.com/docs/reference/csharp/introduction
- **Brevo API:** https://developers.brevo.com/reference/getting-started-1

### Getting Help
- Check logs in `EmailService/logs/` directory
- Query `email_delivery_log` table for delivery attempt history
- Review metrics endpoint for queue health
- Check DLQ (`DeadLetterEmail` table) for stuck messages
- Review webhook processing in Brevo dashboard

---

## ✅ Development Checklist

When starting work on this codebase:

- [ ] Clone repository and checkout branch
- [ ] Copy `appsettings.template.json` to `appsettings.json`
- [ ] Fill in Brevo API key and Supabase credentials
- [ ] Run `dotnet restore` to install dependencies
- [ ] Run `dotnet build` to verify compilation
- [ ] Run `dotnet test` to verify tests pass
- [ ] Run `dotnet run` and verify service starts
- [ ] Check `/health` endpoint returns healthy
- [ ] Check `/metrics` endpoint returns valid JSON
- [ ] Review recent logs for any startup warnings
- [ ] Read relevant documentation in `docs/` folder

**You're now ready to develop! 🎉**
