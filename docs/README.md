# Customer Notification System - Architecture Overview

**Created:** 2026-07-22  
**Status:** Architecture Complete - Ready for Implementation  
**Architect:** System Architect Agent

---

## 📌 Quick Reference

- **Full Architecture:** [customer-notification-jobs-architecture.md](./customer-notification-jobs-architecture.md)
- **Implementation Guide:** [implementation-checklist.md](./implementation-checklist.md)

---

## 🎯 What We're Building

Two new automated email notification jobs to improve customer experience:

### 1. Purchase Confirmation Job
**Sends confirmation emails when customers submit review requests**

- **Trigger:** New trade or team review created
- **Timing:** Within 30 seconds of submission
- **Purpose:** Immediate acknowledgment that request was received

### 2. Review Completion Job  
**Sends completion emails when reviews are finished**

- **Trigger:** Trade review completed OR team review completed with YouTube link
- **Timing:** Within 1 minute of completion
- **Purpose:** Notify customer their review is ready to view

---

## 🏗️ Technical Approach

### Architecture Pattern
Following the existing `NotifyReviewerOfTeamReviewJob` pattern:
- Quartz scheduled jobs (time-based polling)
- Supabase queries with AdminAuth for email lookup
- Brevo template emails
- Database flags for idempotency (`purchase_confirmed`, `email_sent`)

### Database Changes
Single migration adding `purchase_confirmed` column to both review tables:
```sql
ALTER TABLE "TradeReview" ADD COLUMN "purchase_confirmed" BOOLEAN DEFAULT FALSE;
ALTER TABLE "TeamReviews" ADD COLUMN "purchase_confirmed" BOOLEAN DEFAULT FALSE;
```

### Code Structure
```
Jobs/
├── NotifyCustomerOfPurchaseConfirmationJob.cs   [NEW]
└── NotifyCustomerOfReviewCompletionJob.cs       [NEW]

DTOs/
├── TradeReviewPurchaseInfo.cs                   [NEW]
└── TeamReviewPurchaseInfo.cs                    [NEW]

Services/
├── ISupabaseService.cs                          [MODIFY - 4 new methods]
└── SupabaseService.cs                           [MODIFY - 4 implementations]

SupabaseModels/
├── TradeReview.cs                               [MODIFY - add purchase_confirmed]
└── TeamReview.cs                                [MODIFY - add purchase_confirmed]

Program.cs                                       [MODIFY - add Quartz schedules]
```

---

## 📧 Email Flow

### Purchase Confirmation Flow
```
Customer submits review
    ↓
Database: purchase_confirmed = false
    ↓
Job runs (every 30s) → Queries Supabase
    ↓
Sends Brevo template email
    ↓
Database: purchase_confirmed = true
    ✓
Customer receives confirmation
```

### Completion Notification Flow
```
Reviewer completes review
    ↓
Database: status = 'Complete', email_sent = false
    ↓
Job runs (every 1min) → Queries Supabase
    ↓
Sends Brevo template email
    ↓
Database: email_sent = true
    ✓
Customer receives completion notice
```

---

## 🚦 Implementation Phases

### ✅ Phase 1: Architecture & Planning (COMPLETE)
- [x] System design complete
- [x] Architecture document created
- [x] Implementation checklist created

### 🔄 Phase 2: Database Migration (BLOCKED - Needs DBA)
- [ ] Create migration SQL
- [ ] Execute on Supabase staging
- [ ] Verify columns and indexes

### 🔄 Phase 3: Code Implementation (READY - Developer Agent)
- [ ] Update models (TradeReview, TeamReview)
- [ ] Create DTOs (purchase info classes)
- [ ] Extend services (4 new Supabase methods)
- [ ] Create jobs (2 new job classes)
- [ ] Update Program.cs (Quartz schedules)

### 🔄 Phase 4: Brevo Templates (BLOCKED - Needs Marketing/Manual)
- [ ] Create 4 email templates in Brevo dashboard
- [ ] Document template IDs
- [ ] Update code with template IDs

### 🔄 Phase 5: Testing & Deployment (READY after Phase 2-4)
- [ ] Unit tests
- [ ] Integration tests on staging
- [ ] Production deployment

---

## 📋 External Dependencies

### Before Code Implementation
1. **Brevo Templates** — Must create 4 templates and get IDs
2. **Database Migration** — Must execute on Supabase before deploying code

### Resources Needed
- Brevo dashboard access (to create templates)
- Supabase admin access (to run migration)
- Staging environment (for testing)

---

## 🎯 Success Criteria

### Functional Requirements
✅ Customers receive confirmation email within 60 seconds of submitting a review  
✅ Customers receive completion email within 2 minutes of review being finished  
✅ No duplicate emails sent for the same review  
✅ System handles errors gracefully without crashing  

### Non-Functional Requirements
✅ Jobs follow existing code patterns and conventions  
✅ Database queries are performant (indexed columns)  
✅ Logging provides debugging context  
✅ Email sending failures don't block other notifications  

---

## 📊 Monitoring Plan

### Key Metrics
- **Purchase confirmations sent per day**
- **Completion notifications sent per day**
- **Email delivery success rate** (target: >95%)
- **Job execution errors** (target: <1%)
- **Average time from submission to confirmation** (target: <60 seconds)

### Log Queries
```csharp
// Successful purchase confirmations today
"Sent purchase confirmation to {Email} for {ReviewType} review {ReviewId}"

// Successful completion notifications today
"Sent completion notification to {Email} for {ReviewType} review {ReviewId}"

// Errors
logger.Level == "Error" AND logger.Name LIKE "%NotifyCustomer%"
```

---

## 🚨 Risk Assessment

### Low Risk ✅
- **Code changes** — Following established patterns, minimal new code
- **Database schema** — Simple additive column, no breaking changes
- **Email system** — Using existing Brevo integration

### Medium Risk ⚠️
- **Template creation** — Manual process, prone to typos in variable names
- **Email quota** — Increased email volume may affect Brevo quota
- **Timing** — 30-second interval may need tuning based on load

### Mitigation Strategies
- **Template testing** — Send test emails before going live
- **Quota monitoring** — Set up Brevo alerts for approaching limits
- **Configurable intervals** — Make job schedules configurable via environment variables (future enhancement)

---

## 🔄 Rollback Plan

If issues occur:

**Level 1 (Soft Rollback):** Disable jobs
- Change Quartz schedule to run once per day
- Gives time to investigate without stopping service

**Level 2 (Code Rollback):** Revert deployment
- Deploy previous version of email service
- No customer impact (they just won't get new notifications)

**Level 3 (Database Rollback):** Drop columns
- Only if columns cause issues (unlikely)
- SQL: `ALTER TABLE ... DROP COLUMN purchase_confirmed`

---

## 🤝 Agent Handoff

### Developer Agent - Ready to Start
Everything you need is in the implementation checklist:
- Step-by-step tasks
- Code patterns to follow
- File locations
- Acceptance criteria

**Start with:** Phase 2 (Models & DTOs) - database migration can happen in parallel.

### DBA Agent - Action Required
Database migration is straightforward:
- Add two boolean columns
- Add two indexes (optional but recommended)
- See architecture doc for SQL

**Blocker for:** Code deployment (but not code development)

---

## 📚 Documentation

### For Future Maintainers

**Job Schedules:**
- Purchase confirmation: Every 30 seconds
- Review completion: Every 1 minute
- Reviewer notification: Every 1 minute (existing)

**Database Flags:**
- `purchase_confirmed` — Customer notified of submission
- `email_sent` — Customer notified of completion
- `reviewer_notified` — Reviewer notified of assignment

**Brevo Templates:**
| Template ID | Purpose | Variables |
|-------------|---------|-----------|
| TBD | Trade purchase confirmation | trade_id, review_url, submission_date |
| TBD | Team review purchase confirmation | team_review_id, review_url, submission_date |
| TBD | Trade completion | trade_id, review_url, completion_date |
| TBD | Team review completion | team_review_id, review_url, youtube_link, completion_date |

---

## ✨ Future Enhancements

Not in scope for this implementation, but identified for future consideration:

1. **User Preferences** — Allow opt-out of certain notification types
2. **Digest Emails** — Batch multiple notifications
3. **SMS Support** — Alternative notification channel
4. **Retry Logic** — Exponential backoff for failed sends
5. **Webhooks** — Track delivery via Brevo webhooks
6. **Configurable Timing** — Make job intervals environment-configurable

---

## 📞 Questions & Support

**Architecture questions:** Review [customer-notification-jobs-architecture.md](./customer-notification-jobs-architecture.md)  
**Implementation questions:** Review [implementation-checklist.md](./implementation-checklist.md)  
**Need clarification:** Contact Architect Agent with specific questions

---

**Document Version:** 1.0  
**Status:** Approved for Implementation  
**Next Step:** Developer Agent begins Phase 2 (Models & DTOs)
