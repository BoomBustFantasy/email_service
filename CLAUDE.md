# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A .NET 9 ASP.NET Core service that is simultaneously an HTTP API and a background queue consumer. It drains a Supabase **pgmq** queue of outbound email jobs, validates each payload against a typed template contract, and sends through Brevo — then ingests Brevo's delivery webhooks to close the loop on delivery state and suppression. It is the consumer half of a dual-repo rollout; the `boom` Nuxt app is the producer that writes to the queue.

Tracked by [PRD #17](https://github.com/BoomBustFantasy/email_service/issues/17). Read that ticket before changing template keys, retry policy, or suppression rules — it is the contract this service is measured against.

## Commands

```bash
dotnet build email_service.sln
```

```bash
dotnet test email_service.sln
```

```bash
dotnet test EmailService.Tests/EmailService.Tests.csproj --filter "FullyQualifiedName~Suppression"
```

```bash
dotnet run --project EmailService
```

Restore pulls `BoomBust.Logging` from the private **GitHub Packages** feed. If it fails, add the source with a PAT that has `read:packages`:

```bash
dotnet nuget add source https://nuget.pkg.github.com/BoomBustFantasy/index.json --name BoomBustFantasy --username BoomBustFantasy --password <PAT> --store-password-in-clear-text
```

`nuget.config` and `appsettings.json` are both gitignored and hold credentials. Copy `EmailService/appsettings.template.json` to `appsettings.json` to start. Every key also works as an environment variable (`Brevo__ApiKey`, `Supabase__ServiceRoleKey`) — that's how the container is configured.

**Non-secret settings go in `EmailService/appsettings.Defaults.json`, which is committed and is the only settings file that reaches production.** Because `appsettings.json` is gitignored it is absent from the Docker build context, so anything defined only there binds to nothing in the container. Config precedence is `appsettings.Defaults.json` → `appsettings.json` (local only) → environment variables.

Note the test project targets **net10.0** while the service targets **net9.0**.

## Architecture

`Program.cs` is the composition root: it binds config sections, registers a singleton Supabase `Client`, registers services, adds `QueueConsumerService` as a hosted service, and maps `/`, `/health`, `/metrics`, plus controllers. `InitializeAsync()` on the Supabase client is awaited after `Build()`.

### The send path

`QueueConsumerService.ProcessMessageAsync` is the spine. Order matters — each step is a gate:

1. **Read** a batch from pgmq via the `read_email_queue` RPC (5s poll, batch 10, 300s visibility timeout).
2. **Idempotency** — skip if `EmailDeliveryLog` already has a `sent`/`delivered` row for this `idempotency_key`.
3. **Suppression** — `CheckSuppressionAsync`, which only applies to `marketing`-classified messages. Transactional mail must reach recipients who unsubscribed from marketing (PRD stories 14/15).
4. **Contract** — `ITemplateContractRegistry.TryCreate` builds a typed contract for the `template_key`, then `Validate()` checks required variables. Either failing calls `RejectMessageAsync`, which logs status `invalid` and archives the message off the queue. A malformed payload is a producer bug that retrying cannot fix, so it must not consume the retry budget.
5. **Send** via `IEmailService.SendTemplateEmailAsync(ITemplateContract)` — the contract overload, which resolves the Brevo numeric ID and applies sender identity. Never call the raw `(to, templateId, params)` overload from the consumer; it bypasses validation entirely.
6. **Archive** on success; on failure, let pgmq redeliver via the visibility timeout.

### Adding a template

Four places, all required:

1. A contract class in `Templates/Contracts/` with a `public const string Key`, a static `Create(recipientEmail, variables)` factory, `Validate`, and `ToBrevoParams`. The four review templates share `ReviewLinkContract`.
2. An entry in `TemplateContractRegistry.Factories`.
3. An entry in `TemplateIdMap` in `EmailService/appsettings.Defaults.json`, mapping the key to the Brevo numeric template ID. Not `appsettings.json` — that never ships.
4. The Brevo template itself, whose `{{params.x}}` names must match `ToBrevoParams()` keys.

Keys are **producer-facing**. `boom` sends `template_key`; changing one is a breaking change across both repos. A key present in `TemplateIdMap` but absent from the registry is rejected as invalid — the map alone does not make a template work.

### Things that are easy to get wrong

- **Webhook signature verification must fail closed.** `BrevoSignatureVerifier.IsValid` returns false for an unset secret, a malformed signature, or a mismatch. It previously returned `true` when `Brevo:WebhookSecret` was unset, which let anyone create suppression records. Comparison is `CryptographicOperations.FixedTimeEquals`, not `==`.
- **The webhook body is read twice.** Model binding consumes it, then the HMAC check rewinds. `Program.cs` calls `EnableBuffering()` for `/webhooks` requests to make that legal — without it the rewind throws on a non-seekable stream.
- **`ReviewEmailFactory` is dead code** carried over from the pre-queue design, still registered in DI with no callers.
- **Three contracts are orphaned**: `TeamReviewNotificationContract`, `TradeOfferNotificationContract`, `PurchaseConfirmationContract`. They are not in the PRD's template scope, not in the registry, and not in `TemplateIdMap` — unreachable. Delete or wire them; don't assume they're live.
- **`email_delivery_log.status` is CHECK-constrained** to `pending`, `sent`, `failed`, `bounced`, `rejected`, `deferred` (`valid_status`, defined in boom's `20260725165628_email_foundation.sql`). Use `DeliveryStatus`, never a literal. Anything outside the set fails the insert, and `LogDeliveryAttempt` swallows the exception, so the row is lost while the queue message is archived anyway — silently. The consumer wrote `stale` and `invalid` until 2026-08-24 and neither was ever recorded; both are now `rejected`, separated by the `Stale: ` / `Invalid: ` reason prefix. **Still broken:** `BrevoWebhookController.MapEventToStatus` returns `delivered`, `opened`, `clicked`, `unsubscribed`, `spam`, `blocked` — none permitted. That needs the constraint widened, not remapping. Knock-on: the idempotency check treats `delivered` as already-processed, but no row can hold it, so only `sent` does any work.
- **A successful send must persist Brevo's message ID.** `SendTemplateEmailAsync(ITemplateContract)` returns a `SendResult`, and the consumer writes `MessageId` to `EmailDeliveryLog.ExternalId`. `BrevoWebhookController` matches delivery webhooks by that column, so a `sent` row without it is unreconcilable and its webhook can never find it. Every `sent` row written before 2026-08-24 has a null `external_id` for this reason.
- **pgmq does not dead-letter on its own**, despite a comment claiming it does. Failed sends simply redeliver when the visibility timeout lapses. Explicit removal only happens via `ArchiveMessageFromPgmq`.
- Jobs and the consumer swallow exceptions and log; a failure never stops the host.

### Logging

`UseBoomBustLogging` (private `BoomBust.Logging` package) configures Serilog — console plus rolling file at `logs/email-service-.txt`, with `Microsoft`, `System`, and `Quartz` forced to Warning.

## Deployment

Push to `main` builds the Dockerfile and pushes `jackbruzan/email_service:latest` and `:<run_number>` to Docker Hub. The GitHub Packages PAT is injected as a BuildKit secret so it never lands in a layer; the runtime stage runs as non-root `appuser`. There is no CI build or test step on pull requests.

## Related repositories

`.github/copilot-instructions.md` and `.github/agents/*.md` describe the **Boom Bust Fantasy Nuxt front-end**, not this service. Ignore them for C# work here. The shared Supabase instance is the real coupling: this service uses the service role key and bypasses RLS.
