# Implementation Checklist: Customer Notification Jobs

**Reference:** [customer-notification-jobs-architecture.md](./customer-notification-jobs-architecture.md)  
**Status:** Ready for Implementation  
**Estimated Effort:** 4-6 hours

---

## 🎯 Quick Summary

Add two new Quartz jobs to automatically email customers:
1. When they submit a review request (purchase confirmation)
2. When their review is completed

---

## 📋 Implementation Steps

### Phase 1: Database Preparation
**Owner:** DBA Agent or manual execution  
**Blocker:** Must complete before Phase 2

- [ ] **Task 1.1:** Create database migration file
  - File: `migration_add_purchase_confirmed.sql`
  - Add `purchase_confirmed` column to `TradeReview` table
  - Add `purchase_confirmed` column to `TeamReviews` table
  - Add indexes for query performance
  
- [ ] **Task 1.2:** Execute migration on Supabase staging
  - Verify columns exist: `SELECT * FROM "TradeReview" LIMIT 1;`
  - Verify indexes created: `\di`
  
- [ ] **Task 1.3:** Update Supabase type generation (if auto-generated)
  - Regenerate types to include new `purchase_confirmed` field

---

### Phase 2: Code Changes - Models & DTOs
**Owner:** Developer Agent  
**Dependencies:** Phase 1 complete

- [ ] **Task 2.1:** Update `SupabaseModels/TradeReview.cs`
  - Add property:
    ```csharp
    [Column("purchase_confirmed")]
    public bool PurchaseConfirmed { get; set; }
    ```

- [ ] **Task 2.2:** Update `SupabaseModels/TeamReview.cs`
  - Add property:
    ```csharp
    [Column("purchase_confirmed")]
    public bool PurchaseConfirmed { get; set; }
    ```

- [ ] **Task 2.3:** Create `DTOs/TradeReviewPurchaseInfo.cs`
  - Properties: `TradeId`, `UserEmail`, `CreatedAt`

- [ ] **Task 2.4:** Create `DTOs/TeamReviewPurchaseInfo.cs`
  - Properties: `Id`, `UserEmail`, `CreatedAt`

---

### Phase 3: Code Changes - Services
**Owner:** Developer Agent  
**Dependencies:** Phase 2 complete

- [ ] **Task 3.1:** Update `Services/ISupabaseService.cs`
  - Add method signatures:
    ```csharp
    Task<List<TradeReviewPurchaseInfo>> GetUnconfirmedTradeReviewPurchasesAsync();
    Task<List<TeamReviewPurchaseInfo>> GetUnconfirmedTeamReviewPurchasesAsync();
    Task MarkTradePurchaseConfirmedAsync(long tradeId);
    Task MarkTeamReviewPurchaseConfirmedAsync(long teamReviewId);
    ```

- [ ] **Task 3.2:** Implement methods in `Services/SupabaseService.cs`
  - `GetUnconfirmedTradeReviewPurchasesAsync()` — Query where `purchase_confirmed = false`
  - `GetUnconfirmedTeamReviewPurchasesAsync()` — Query where `purchase_confirmed = false`
  - `MarkTradePurchaseConfirmedAsync()` — Update `purchase_confirmed = true`
  - `MarkTeamReviewPurchaseConfirmedAsync()` — Update `purchase_confirmed = true`
  - Pattern: Follow existing methods like `GetTeamReviewsForReviewerNotificationAsync()`

---

### Phase 4: Code Changes - Jobs
**Owner:** Developer Agent  
**Dependencies:** Phase 3 complete

- [ ] **Task 4.1:** Create `Jobs/NotifyCustomerOfPurchaseConfirmationJob.cs`
  - Template ID constants (set to `0` initially, update after Brevo templates created):
    ```csharp
    private const long TradePurchaseConfirmationTemplateId = 0; // TODO: Update
    private const long TeamReviewPurchaseConfirmationTemplateId = 0; // TODO: Update
    ```
  - Inject: `ISupabaseService`, `IEmailService`, `AppConfig`, `ILogger`
  - Execute logic:
    1. Fetch unconfirmed trade reviews
    2. Fetch unconfirmed team reviews
    3. For each, send template email with params
    4. Mark purchase_confirmed = true
  - Pattern: Follow `NotifyReviewerOfTeamReviewJob.cs`

- [ ] **Task 4.2:** Create `Jobs/NotifyCustomerOfReviewCompletionJob.cs`
  - Template ID constants (set to `0` initially, update after Brevo templates created):
    ```csharp
    private const long TradeCompletionTemplateId = 0; // TODO: Update
    private const long TeamReviewCompletionTemplateId = 0; // TODO: Update
    ```
  - Inject: `ISupabaseService`, `IEmailService`, `AppConfig`, `ILogger`
  - Execute logic:
    1. Call `GetCompletedTradeReviewsWithUsersAsync()` (already exists)
    2. Call `GetPendingTeamReviewEmailsWithYoutubeAsync()` (already exists)
    3. Send template emails with params
    4. Call `MarkEmailSentAsync()` or `MarkTeamReviewEmailedAsync()` (already exist)

---

### Phase 5: Code Changes - Configuration
**Owner:** Developer Agent  
**Dependencies:** Phase 4 complete

- [ ] **Task 5.1:** Update `Program.cs` Quartz configuration
  - Add schedule for `NotifyCustomerOfPurchaseConfirmationJob` (every 30 seconds)
  - Add schedule for `NotifyCustomerOfReviewCompletionJob` (every 1 minute)
  - Pattern: Copy existing `ScheduleJob` call for `NotifyReviewerOfTeamReviewJob`

---

### Phase 6: Brevo Template Creation
**Owner:** Manual / Marketing team  
**Can be done in parallel with Phase 2-5**

- [ ] **Task 6.1:** Create "Trade Review Purchase Confirmation" template in Brevo
  - Subject: "We've Received Your Trade Review Request"
  - Variables needed: `{{params.trade_id}}`, `{{params.review_url}}`, `{{params.submission_date}}`
  - Record template ID: `_______`

- [ ] **Task 6.2:** Create "Team Review Purchase Confirmation" template in Brevo
  - Subject: "We've Received Your Team Review Request"
  - Variables needed: `{{params.team_review_id}}`, `{{params.review_url}}`, `{{params.submission_date}}`
  - Record template ID: `_______`

- [ ] **Task 6.3:** Create "Trade Review Completion" template in Brevo
  - Subject: "Your Trade Review is Complete"
  - Variables needed: `{{params.trade_id}}`, `{{params.review_url}}`, `{{params.completion_date}}`
  - Record template ID: `_______`

- [ ] **Task 6.4:** Create "Team Review Completion" template in Brevo
  - Subject: "Your Team Review is Ready!"
  - Variables needed: `{{params.team_review_id}}`, `{{params.review_url}}`, `{{params.youtube_link}}`, `{{params.completion_date}}`
  - Record template ID: `_______`

- [ ] **Task 6.5:** Update template ID constants in code
  - Update `NotifyCustomerOfPurchaseConfirmationJob.cs` with IDs from 6.1 and 6.2
  - Update `NotifyCustomerOfReviewCompletionJob.cs` with IDs from 6.3 and 6.4

---

### Phase 7: Testing
**Owner:** Developer Agent + QA  
**Dependencies:** All phases complete

- [ ] **Task 7.1:** Unit test purchase confirmation job
  - Test trade review confirmation
  - Test team review confirmation
  - Test error handling

- [ ] **Task 7.2:** Unit test review completion job
  - Test trade completion notification
  - Test team review completion notification
  - Test error handling

- [ ] **Task 7.3:** Integration test on staging
  - Submit test trade review → verify confirmation email received
  - Submit test team review → verify confirmation email received
  - Complete test trade review → verify completion email received
  - Complete test team review with YouTube link → verify completion email received

- [ ] **Task 7.4:** Verify database state
  - Check `purchase_confirmed` flag set to true after confirmation
  - Check `email_sent` flag set to true after completion notification

- [ ] **Task 7.5:** Monitor logs
  - Verify structured logging output
  - Verify no errors in job execution
  - Verify email send success messages

---

### Phase 8: Deployment
**Owner:** DevOps / Developer Agent  
**Dependencies:** Phase 7 complete, all tests passing

- [ ] **Task 8.1:** Deploy to staging environment
  - Build: `dotnet build --configuration Release`
  - Publish: `dotnet publish --configuration Release`
  - Deploy container

- [ ] **Task 8.2:** Monitor staging for 24 hours
  - Check email sending rate
  - Monitor Brevo quota usage
  - Monitor error logs

- [ ] **Task 8.3:** Deploy to production
  - Execute database migration on production Supabase
  - Deploy email service to production
  - Monitor for first hour

---

## 🔍 Code Review Checklist

Before marking complete, verify:

### Code Quality
- [ ] All new files follow existing naming conventions (PascalCase for classes)
- [ ] Error handling consistent with `NotifyReviewerOfTeamReviewJob.cs`
- [ ] Logging uses structured logging with context variables
- [ ] No hardcoded values (all config from AppConfig)
- [ ] DTOs use `string.Empty` defaults (not `null`)

### Architecture
- [ ] New methods added to interface before implementation
- [ ] Jobs implement `IJob` interface
- [ ] Jobs use `[DisallowConcurrentExecution]` attribute
- [ ] Dependencies injected via constructor
- [ ] No direct Supabase client usage (all through ISupabaseService)

### Database
- [ ] Column names use snake_case (matching Supabase conventions)
- [ ] All columns have proper Postgrest attributes
- [ ] Indexes created for frequently queried columns

### Email
- [ ] Template IDs are non-zero and valid
- [ ] Template parameters match Brevo template variables
- [ ] Email sending errors are logged but don't crash job

---

## 🚨 Rollback Procedure

If issues occur in production:

1. **Immediate:** Disable jobs via config (set interval to 24 hours)
2. **Short-term:** Revert to previous email service version
3. **If needed:** Drop `purchase_confirmed` columns from database

---

## 📊 Success Metrics

After deployment, monitor:

- **Purchase confirmation emails sent** — Should match count of new reviews
- **Completion notification emails sent** — Should match count of completed reviews
- **Email delivery rate** — Target: >95%
- **Job execution errors** — Target: <1%
- **Average time to send confirmation** — Target: <60 seconds

---

## 🤝 Handoff Notes

### For Developer Agent
- Reference architecture doc for detailed design decisions
- Follow existing patterns from `NotifyReviewerOfTeamReviewJob.cs`
- All template IDs should be configurable (not hardcoded)
- Test locally before pushing to staging

### For DBA Agent
- Database migration is straightforward (just adding two columns)
- Indexes are optional but recommended for performance
- No breaking changes to existing schema

### For QA/Testing
- Focus on email delivery verification
- Check that flags are set correctly after sends
- Verify no duplicate emails sent
- Test error scenarios (missing email, Brevo API down)

---

**Checklist Version:** 1.0  
**Last Updated:** 2026-07-22  
**Ready for:** Developer Agent implementation
