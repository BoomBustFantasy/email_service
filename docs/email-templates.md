# Email Templates

The Brevo templates this service sends, and the variables each one receives.

**Source of truth is this table, not the PRDs.** [email_service #17](https://github.com/BoomBustFantasy/email_service/issues/17) and [boom #247](https://github.com/JackBruzan/boom/issues/247) both scope five templates with a `purchase_type` variable. Both closed 2026-07-25, and the producer kept shipping after that — three more templates were added and `purchase_type` became `product_type`. The variables below were verified against live `email_outbox` rows on 2026-08-22, not read off the PRDs.

Producer definitions live in `boom/server/utils/emailEnqueue.ts` (`EmailTemplateKey` and `EmailTemplateVariables`).

## Templates

Variables are referenced in Brevo as `{{ params.name }}`. All templates are `classification: transactional`.

**None of these templates exist in Brevo yet** (confirmed 2026-08-22). The `3`, `5`, and `6` currently sitting in `TemplateIdMap` are stale and point at nothing dependable — every ID below needs to be filled in from a newly created template.

Ordered by queued volume, so the highest-traffic templates get built first.

| Template | Key | Brevo ID | Variables | Backlog | Sent when |
|---|---|---|---|---|---|
| Trade review complete | `trade_review_completed` | **8** | `trade_id`, `trade_url` | 133 | Reviewer completes a trade review |
| Reviewer: team assigned | `reviewer_team_assigned` | **5** | `review_id`, `review_url` | 32 | Team review assigned to a reviewer |
| Purchase confirmation | `stripe_purchase_confirmation` | **10** | `credits_amount`, `product_type` | 27 | Stripe credits recorded |
| Trade submitted | `trade_submitted` | **7** | `trade_id`, `trade_url`, `next_show_label`, `youtube_url` | 22 | Trade submitted for review |
| Team review ready | `team_review_ready` | **9** | `review_id`, `review_url` | 18 | Team review published |
| Welcome | `welcome_email` | **6** | *none* | 2 | Account created |
| Season pass confirmed | `membership_season_pass_confirmed` | **11** | `tier`, `expires_at`, `checkin_credits_included` | 0 | Season pass purchased |
| Reviewer: trade assigned | `reviewer_trade_assigned` | _not needed_ | `trade_id`, `trade_url` | 0 | **Never — see below** |

Fill in each ID after creating the template in Brevo, then mirror them into `TemplateIdMap` in `appsettings.json`.

Value notes for template design:

- `next_show_label` is a pre-formatted human string (`"Saturday, August 22 at 7:30 PM CT"`), not a date to format.
- `product_type` is `"team_review"` or `"trade_credit"`; `credits_amount` is a number.
- `tier` is `"Pro"` or `"MVP"`.
- `welcome_email` receives `{}` — no variables at all, so it cannot personalize. Adding a name means changing the producer first.

## Sample payloads

Real values pulled from the outbox:

```jsonc
// trade_submitted
{
  "trade_id": 1467,
  "trade_url": "https://boombustfantasy.com/trades/1467",
  "next_show_label": "Saturday, August 22 at 7:30 PM CT",
  "youtube_url": "https://www.youtube.com/@BoomBustFantasy"
}

// team_review_ready — customer-facing link
{ "review_id": 720, "review_url": "https://boombustfantasy.com/team-reviews/720" }

// reviewer_team_assigned — reviewer-facing link, note the /review suffix
{ "review_id": 722, "review_url": "https://boombustfantasy.com/team-reviews/722/review" }

// stripe_purchase_confirmation — product_type is "team_review" or "trade_credit"
{ "credits_amount": 1, "product_type": "team_review" }

// welcome_email
{}
```

The two team-review URLs differ deliberately: customers land on the review, reviewers land on the review form. Don't reuse one link style across both templates.

## `reviewer_trade_assigned` is dead

It appears in the producer's `EmailTemplateKey` union but has no call site in `boom` and has never produced a queued message. No Brevo template is needed until someone wires it up; if it stays unwired, drop it from the union.

## Adding a template

Four places, all required — a Brevo ID without a contract gets rejected as invalid, and a contract without an ID fails to resolve:

1. Create the template in Brevo; note its numeric ID.
2. Add a contract class in `EmailService/Templates/Contracts/` with a `public const string Key`, a static `Create(recipientEmail, variables)` factory, `Validate`, and `ToBrevoParams`. The review templates share `ReviewLinkContract`.
3. Register it in `TemplateContractRegistry.Factories`.
4. Map key → Brevo ID in `TemplateIdMap` (`appsettings.json`, and the template file).

Template keys are the producer-facing contract. Renaming one is a breaking change across both repos — the producer sends the key, this service resolves it.
