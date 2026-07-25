# Customer Notification Jobs Architecture Plan

**Created:** 2026-07-22  
**Status:** Planning  
**Owner:** Architect

---

## 📋 Executive Summary

This document outlines the architectural design for adding two new Quartz jobs to send automated email notifications to customers:

1. **Purchase Confirmation Job** — Sends confirmation emails when customers submit trade or team review requests
2. **Review Completion Job** — Sends completion emails when reviews are finished

---

## 🎯 Objectives

### Primary Goals
- Notify customers immediately when their review request is submitted (purchase confirmation)
- Notify customers when their trade or team review is completed
- Maintain consistency with existing notification architecture
- Support both trade reviews and team reviews through a unified approach

### Non-Goals
- Modifying the Supabase schema (database already has necessary fields)
- Changing the Brevo template system
- Adding new authentication or authorization logic

---

## 📊 Current State Analysis

### Existing Notification Infrastructure

The email service currently has one job:

**`NotifyReviewerOfTeamReviewJob`**
- Purpose: Notifies reviewers when a team review is assigned to them
- Trigger: `reviewer_notified = false AND reviewer_id IS NOT NULL`
- Frequency: Every 1 minute
- Template ID: `5`
- Updates: Sets `reviewer_notified = true` after sending

### Database Schema (Relevant Fields)

Both `TradeReview` and `TeamReview` tables have:
```csharp
- user_id: Guid              // Customer who submitted the request
- reviewer_id: Guid?         // Assigned reviewer
- status: AdviceStatus       // Pending, InProgress, Complete
- email_sent: bool           // Customer notified of completion
- reviewer_notified: bool    // Reviewer notified of assignment
- created_at: DateTime?      // Submission timestamp
- answered_at: DateTime?     // Completion timestamp
```

### Existing Service Methods

**SupabaseService** provides:
- `GetCompletedTradeReviewsWithUsersAsync()` — Fetches completed trades where `email_sent = false`
- `MarkEmailSentAsync(tradeId)` — Sets `email_sent = true`
- `GetPendingTeamReviewEmailsWithYoutubeAsync()` — Fetches team reviews with YouTube links
- `MarkTeamReviewEmailedAsync(teamReviewId)` — Sets `email_sent = true`

**Gaps Identified:**
- No method to fetch newly created reviews for purchase confirmation
- No unified job to process review completion notifications

---

## 🏗️ Proposed Architecture

### Overview

We will add **TWO new Quartz jobs**:

1. **`NotifyCustomerOfPurchaseConfirmationJob`** — Sends confirmation when request is submitted
2. **`NotifyCustomerOfReviewCompletionJob`** — Sends notification when review is complete

Both jobs will handle **trade reviews AND team reviews** to avoid code duplication.

---

## 🔧 Detailed Design

### Job 1: Purchase Confirmation Job

**Purpose:** Send confirmation email when a customer submits a review request.

#### Database Trigger Criteria

New tracking field needed: **`purchase_confirmed: bool`**

This field must be added to both `TradeReview` and `TeamReview` models.

**Query Logic:**
- Trade Reviews: `status = 'Pending' AND purchase_confirmed = false`
- Team Reviews: `status = 'Pending' AND purchase_confirmed = false`

#### Job Implementation

**File:** `EmailService/Jobs/NotifyCustomerOfPurchaseConfirmationJob.cs`

**Responsibilities:**
1. Query Supabase for unconfirmed trade reviews
2. Query Supabase for unconfirmed team reviews
3. Fetch customer email via AdminAuth for each review
4. Send Brevo template email with review details
5. Update `purchase_confirmed = true` on success

**Template Variables:**
```csharp
{
    "review_type": "trade" | "team",
    "review_id": "{id}",
    "review_url": "{baseUrl}/trades/{id}" | "{baseUrl}/team-reviews/{id}",
    "submission_date": "{created_at formatted}"
}
```

**Brevo Template ID:** TBD (must be created in Brevo dashboard)

**Schedule:** Every 30 seconds (to ensure fast confirmation)

#### New Service Methods Required

**ISupabaseService:**
```csharp
Task<List<TradeReviewPurchaseInfo>> GetUnconfirmedTradeReviewPurchasesAsync();
Task<List<TeamReviewPurchaseInfo>> GetUnconfirmedTeamReviewPurchasesAsync();
Task MarkTradePurchaseConfirmedAsync(long tradeId);
Task MarkTeamReviewPurchaseConfirmedAsync(long teamReviewId);
```

**New DTOs:**
```csharp
// DTOs/TradeReviewPurchaseInfo.cs
public class TradeReviewPurchaseInfo
{
    public long TradeId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
}

// DTOs/TeamReviewPurchaseInfo.cs
public class TeamReviewPurchaseInfo
{
    public long Id { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
}
```

---

### Job 2: Review Completion Job

**Purpose:** Notify customers when their review is completed.

#### Database Trigger Criteria

**Existing fields used:**
- `email_sent: bool` — Already exists in both tables
- `status: AdviceStatus` — Must equal `Complete`

**Query Logic:**
- Trade Reviews: `status = 'Complete' AND email_sent = false`
- Team Reviews: `status = 'Complete' AND email_sent = false AND youtube_link IS NOT NULL`

**Note:** Team reviews require a YouTube link to be considered "ready for notification" as the review deliverable is a video.

#### Job Implementation

**File:** `EmailService/Jobs/NotifyCustomerOfReviewCompletionJob.cs`

**Responsibilities:**
1. Query Supabase for completed trade reviews (already exists: `GetCompletedTradeReviewsWithUsersAsync`)
2. Query Supabase for completed team reviews (already exists: `GetPendingTeamReviewEmailsWithYoutubeAsync`)
3. Send Brevo template email for trades (template ID: TBD)
4. Send Brevo template email for team reviews (template ID: TBD)
5. Update `email_sent = true` on success (methods already exist)

**Template Variables for Trade Reviews:**
```csharp
{
    "review_type": "trade",
    "trade_id": "{id}",
    "review_url": "{baseUrl}/trades/{id}",
    "completion_date": "{answered_at formatted}"
}
```

**Template Variables for Team Reviews:**
```csharp
{
    "review_type": "team",
    "team_review_id": "{id}",
    "review_url": "{baseUrl}/team-reviews/{id}",
    "youtube_link": "{youtube_link}",
    "completion_date": "{answered_at formatted}"
}
```

**Brevo Template IDs:** 
- Trade completion: TBD
- Team review completion: TBD

**Schedule:** Every 1 minute (consistent with existing reviewer notification job)

---

## 📁 File Structure Changes

### New Files to Create

```
EmailService/
├── Jobs/
│   ├── NotifyReviewerOfTeamReviewJob.cs          [EXISTS]
│   ├── NotifyCustomerOfPurchaseConfirmationJob.cs [NEW]
│   └── NotifyCustomerOfReviewCompletionJob.cs     [NEW]
├── DTOs/
│   ├── EmailMessage.cs                            [EXISTS]
│   ├── TradeEmailInfo.cs                          [EXISTS]
│   ├── TeamReviewEmailInfo.cs                     [EXISTS]
│   ├── TeamReviewNotificationInfo.cs              [EXISTS]
│   ├── TradeReviewPurchaseInfo.cs                 [NEW]
│   └── TeamReviewPurchaseInfo.cs                  [NEW]
└── SupabaseModels/
    ├── TradeReview.cs                             [MODIFY - add purchase_confirmed]
    └── TeamReview.cs                              [MODIFY - add purchase_confirmed]
```

### Modified Files

**`Program.cs`**
- Add Quartz job schedules for both new jobs

**`Services/ISupabaseService.cs`**
- Add 4 new method signatures (see above)

**`Services/SupabaseService.cs`**
- Implement 4 new methods

**`SupabaseModels/TradeReview.cs`**
- Add `purchase_confirmed` property

**`SupabaseModels/TeamReview.cs`**
- Add `purchase_confirmed` property

---

## 🔄 Data Flow Diagrams

### Purchase Confirmation Flow

```
[Customer submits review] 
    → [Record created in DB with purchase_confirmed=false]
    → [Job runs every 30s]
    → [Queries Supabase for unconfirmed purchases]
    → [Fetches user email via AdminAuth]
    → [Sends Brevo template email]
    → [Updates purchase_confirmed=true]
    → [Customer receives confirmation]
```

### Review Completion Flow

```
[Reviewer completes review]
    → [Status set to 'Complete', answered_at timestamp set]
    → [For team reviews: youtube_link added]
    → [Job runs every 1 min]
    → [Queries Supabase for completed reviews where email_sent=false]
    → [Fetches user email via AdminAuth]
    → [Sends Brevo template email]
    → [Updates email_sent=true]
    → [Customer receives completion notification]
```

---

## 🗄️ Database Schema Changes

### Required Migration

**Add `purchase_confirmed` column to both tables:**

```sql
-- Migration: Add purchase_confirmed column
ALTER TABLE "TradeReview" 
ADD COLUMN "purchase_confirmed" BOOLEAN NOT NULL DEFAULT FALSE;

ALTER TABLE "TeamReviews" 
ADD COLUMN "purchase_confirmed" BOOLEAN NOT NULL DEFAULT FALSE;

-- Index for performance (optional but recommended)
CREATE INDEX idx_trades_purchase_confirmed 
ON "TradeReview"(purchase_confirmed) 
WHERE purchase_confirmed = FALSE;

CREATE INDEX idx_team_reviews_purchase_confirmed 
ON "TeamReviews"(purchase_confirmed) 
WHERE purchase_confirmed = FALSE;
```

**Note:** This migration should be executed on the Supabase database before deploying the email service changes.

---

## 📧 Brevo Template Requirements

### Templates to Create in Brevo Dashboard

**1. Purchase Confirmation - Trade Review** (ID: TBD)
- Subject: "We've Received Your Trade Review Request"
- Variables: `{{params.trade_id}}`, `{{params.review_url}}`, `{{params.submission_date}}`

**2. Purchase Confirmation - Team Review** (ID: TBD)
- Subject: "We've Received Your Team Review Request"
- Variables: `{{params.team_review_id}}`, `{{params.review_url}}`, `{{params.submission_date}}`

**3. Completion Notification - Trade Review** (ID: TBD)
- Subject: "Your Trade Review is Complete"
- Variables: `{{params.trade_id}}`, `{{params.review_url}}`, `{{params.completion_date}}`

**4. Completion Notification - Team Review** (ID: TBD)
- Subject: "Your Team Review is Ready!"
- Variables: `{{params.team_review_id}}`, `{{params.review_url}}`, `{{params.youtube_link}}`, `{{params.completion_date}}`

**Template IDs must be added to the code as constants once created.**

---

## ⚙️ Configuration Changes

### Program.cs Quartz Configuration

Add two new job schedules:

```csharp
services.AddQuartz(q =>
{
    // Existing job
    q.ScheduleJob<NotifyReviewerOfTeamReviewJob>(trigger => trigger
        .WithIdentity("notifyReviewerOfTeamReviewTrigger", "emailJobs")
        .StartNow()
        .WithSimpleSchedule(x => x
            .WithIntervalInMinutes(1)
            .RepeatForever())
    );

    // NEW: Purchase confirmation - runs every 30 seconds
    q.ScheduleJob<NotifyCustomerOfPurchaseConfirmationJob>(trigger => trigger
        .WithIdentity("notifyCustomerOfPurchaseConfirmationTrigger", "emailJobs")
        .StartNow()
        .WithSimpleSchedule(x => x
            .WithIntervalInSeconds(30)
            .RepeatForever())
    );

    // NEW: Review completion - runs every 1 minute
    q.ScheduleJob<NotifyCustomerOfReviewCompletionJob>(trigger => trigger
        .WithIdentity("notifyCustomerOfReviewCompletionTrigger", "emailJobs")
        .StartNow()
        .WithSimpleSchedule(x => x
            .WithIntervalInMinutes(1)
            .RepeatForever())
    );
});
```

---

## 🚨 Error Handling Strategy

### Consistency with Existing Pattern

Both new jobs should follow the error handling pattern established in `NotifyReviewerOfTeamReviewJob`:

1. **Top-level try/catch** around Supabase query — log error and return if query fails
2. **Per-item try/catch** around email sending — log error and continue to next item
3. **Email validation** — skip items with missing/empty email addresses
4. **Graceful degradation** — failed items remain unprocessed and will retry on next job execution

### Logging Standards

Use structured logging with context:
- Log level: `Information` for successful sends
- Log level: `Warning` for validation failures (missing email)
- Log level: `Error` for send failures and query exceptions

Example:
```csharp
_logger.LogInformation(
    "Sent purchase confirmation to {Email} for {ReviewType} review {ReviewId}", 
    email, "trade", tradeId);
```

---

## 🔒 Security Considerations

### Email Privacy
- All email fetching uses Supabase AdminAuth (service role) — bypasses RLS
- No email addresses stored in application logs (only in structured log metadata)

### Data Access
- Jobs use scoped `ISupabaseService` — proper DI lifecycle management
- No direct Supabase client access from jobs

### Rate Limiting
- Current approach: None (Brevo API handles rate limits on their end)
- If Brevo rate limits are hit, jobs will log errors and retry on next execution
- **Recommendation:** Monitor Brevo API usage and implement exponential backoff if needed

---

## 📊 Testing Strategy

### Unit Tests (Recommended)

**Test Cases for `NotifyCustomerOfPurchaseConfirmationJob`:**
1. Successfully processes trade review purchase confirmations
2. Successfully processes team review purchase confirmations
3. Skips reviews with missing user email
4. Handles Supabase query failures gracefully
5. Handles email send failures gracefully
6. Marks purchase_confirmed=true only after successful send

**Test Cases for `NotifyCustomerOfReviewCompletionJob`:**
1. Successfully processes completed trade reviews
2. Successfully processes completed team reviews with YouTube links
3. Skips team reviews without YouTube links
4. Skips reviews with missing user email
5. Handles Supabase query failures gracefully
6. Handles email send failures gracefully
7. Marks email_sent=true only after successful send

### Integration Tests (Recommended)

1. End-to-end flow: Create review → Wait for confirmation email
2. End-to-end flow: Complete review → Wait for completion email
3. Verify Supabase state changes (purchase_confirmed, email_sent flags)

### Manual Testing Checklist

- [ ] Create Brevo templates with correct variable names
- [ ] Execute database migration on Supabase
- [ ] Deploy email service to test environment
- [ ] Submit test trade review → verify confirmation email
- [ ] Submit test team review → verify confirmation email
- [ ] Complete test trade review → verify completion email
- [ ] Complete test team review with YouTube link → verify completion email
- [ ] Verify database flags are set correctly after each email

---

## 🚀 Deployment Plan

### Pre-Deployment Checklist

1. **Database Migration**
   - [ ] Execute SQL migration on Supabase staging environment
   - [ ] Verify columns added successfully
   - [ ] Test query performance with new indexes

2. **Brevo Configuration**
   - [ ] Create 4 new email templates in Brevo dashboard
   - [ ] Record template IDs
   - [ ] Test templates with sample data
   - [ ] Update template ID constants in code

3. **Code Review**
   - [ ] All new files follow existing patterns
   - [ ] Error handling consistent with existing jobs
   - [ ] DTOs properly defined
   - [ ] Service interface and implementation complete
   - [ ] Program.cs Quartz configuration correct

### Deployment Sequence

**Stage 1: Database (Execute First)**
```bash
# Run migration on Supabase
psql -h {supabase_host} -U postgres -d postgres -f migration_add_purchase_confirmed.sql
```

**Stage 2: Brevo Templates (Execute Second)**
- Create templates manually in Brevo UI
- Document template IDs

**Stage 3: Email Service Deployment (Execute Last)**
```bash
# Build and deploy email service
dotnet build
dotnet publish
docker build -t email-service:latest .
docker push email-service:latest
# Deploy to hosting environment
```

### Rollback Plan

**If issues occur:**

1. **Stop Jobs** — Set Quartz intervals to a very high value (e.g., 1 day) via config
2. **Disable Templates** — Archive Brevo templates to prevent sends
3. **Revert Code** — Deploy previous version of email service
4. **Database Rollback** (if necessary) — Drop purchase_confirmed columns

**Database rollback SQL:**
```sql
DROP INDEX IF EXISTS idx_trades_purchase_confirmed;
DROP INDEX IF EXISTS idx_team_reviews_purchase_confirmed;
ALTER TABLE "TradeReview" DROP COLUMN "purchase_confirmed";
ALTER TABLE "TeamReviews" DROP COLUMN "purchase_confirmed";
```

---

## 📈 Monitoring & Observability

### Metrics to Track

1. **Email Send Success Rate**
   - Percentage of successful sends per job execution
   - Brevo API response codes

2. **Job Execution Time**
   - Average time per job run
   - Items processed per execution

3. **Email Queue Backlog**
   - Count of unprocessed purchase confirmations
   - Count of unsent completion notifications

4. **Error Rates**
   - Supabase query failures
   - Email send failures
   - Missing user email occurrences

### Logging Queries (if using structured logging sink)

```sql
-- Count of purchase confirmations sent today
SELECT COUNT(*) 
FROM logs 
WHERE message LIKE '%Sent purchase confirmation%' 
AND timestamp > NOW() - INTERVAL '1 day';

-- Error rate for review completion job
SELECT COUNT(*) 
FROM logs 
WHERE logger LIKE '%NotifyCustomerOfReviewCompletionJob%' 
AND level = 'Error'
AND timestamp > NOW() - INTERVAL '1 day';
```

---

## 🔄 Future Enhancements (Out of Scope)

These items are identified but not part of the initial implementation:

1. **Email Preferences** — Allow users to opt out of certain notification types
2. **Digest Emails** — Batch multiple notifications into daily/weekly digests
3. **SMS Notifications** — Alternative notification channel via Twilio/similar
4. **Retry Logic** — Exponential backoff for failed sends
5. **Webhooks** — Brevo webhook integration for delivery tracking
6. **Template Versioning** — Track template changes over time
7. **A/B Testing** — Test different email copy/subject lines

---

## 🤝 Dependencies & Assumptions

### External Dependencies
- **Supabase** — Must have service role key with admin auth access
- **Brevo** — Must have API key and ability to create templates
- **Quartz.NET** — Already integrated, no changes needed

### Assumptions
1. Database migration will be executed before code deployment
2. Brevo templates will be created and IDs documented
3. Current Brevo account has sufficient email sending quota
4. AdminAuth service role has access to user email addresses
5. Customers have valid email addresses in Supabase Auth

### Breaking Changes
- **None** — This is purely additive functionality
- Existing jobs and email flows are not modified

---

## 📚 Reference Implementation

The new jobs should closely follow the pattern established in:
- **`NotifyReviewerOfTeamReviewJob.cs`** — For job structure and error handling
- **`SupabaseService.cs`** — For query patterns and AdminAuth usage
- **`BrevoEmailService.cs`** — For template email sending

---

## ✅ Acceptance Criteria

### Purchase Confirmation Job
- [ ] Sends confirmation email within 30 seconds of review submission
- [ ] Handles both trade and team reviews
- [ ] Sets purchase_confirmed flag after successful send
- [ ] Does not re-send to already confirmed purchases
- [ ] Logs errors without crashing

### Review Completion Job
- [ ] Sends completion email when trade review is completed
- [ ] Sends completion email when team review has YouTube link
- [ ] Sets email_sent flag after successful send
- [ ] Does not re-send to already notified customers
- [ ] Logs errors without crashing

### General Requirements
- [ ] No code duplication between jobs
- [ ] Consistent error handling with existing jobs
- [ ] Structured logging with context
- [ ] Follows existing naming conventions
- [ ] Passes code review

---

## 📞 Contact & Questions

For questions about this architecture plan, contact:
- **Architect Agent** — Design decisions and trade-offs
- **Developer Agent** — Implementation specifics
- **DBA Agent** — Database migration concerns

---

**Document Version:** 1.0  
**Last Updated:** 2026-07-22  
**Next Review:** After implementation completion
