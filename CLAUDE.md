# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A headless .NET 9 console app (`Microsoft.Extensions.Hosting` generic host, no ASP.NET endpoints) that polls the Boom Bust Fantasy Supabase database on a Quartz schedule and sends transactional email through Brevo. It is a background worker for the Boom Bust Fantasy Nuxt app, which lives in a separate repository.

## Commands

```bash
dotnet build email_service.sln
```

```bash
dotnet run --project EmailService
```

Restore pulls `BoomBust.Logging` from the private **GitHub Packages** feed at `https://nuget.pkg.github.com/BoomBustFantasy/index.json`. If restore fails on that package, add the source with a PAT that has `read:packages`:

```bash
dotnet nuget add source https://nuget.pkg.github.com/BoomBustFantasy/index.json --name BoomBustFantasy --username BoomBustFantasy --password <PAT> --store-password-in-clear-text
```

`nuget.config` is gitignored and holds those credentials — never commit it.

### Tests

There is no test project on `main`. `EmailService.Tests` (xUnit + FluentAssertions + Moq, targeting net10.0) exists only on the `PRD-17` branch and is not referenced by `email_service.sln`. When working on a branch that has it:

```bash
dotnet test EmailService.Tests/EmailService.Tests.csproj
```

```bash
dotnet test EmailService.Tests/EmailService.Tests.csproj --filter "FullyQualifiedName~ExponentialBackoff"
```

### Local config

Copy `EmailService/appsettings.template.json` to `EmailService/appsettings.json` and fill in the blanks. `appsettings.json` is gitignored. Every key can also be supplied as an environment variable (`Brevo__ApiKey`, `Supabase__ServiceRoleKey`, …) — that's how the container is configured, since the image ships without an `appsettings.json`.

`Supabase:ServiceRoleKey` is mandatory; both the `Client` registration and `SupabaseService`'s constructor throw at startup without it.

## Architecture

`Program.cs` is the single composition root and does four things: binds `Brevo`/`Supabase`/`App` config sections to POCOs in `Configs/`, registers a singleton Supabase `Client`, registers the scoped services, and schedules Quartz jobs. `InitializeAsync()` on the Supabase client is awaited *after* `Build()` and before `RunAsync()`.

The pipeline for every job is the same three-step loop:

1. `ISupabaseService` queries a review table for rows where a `*_sent` / `*_notified` flag is still false, then resolves the user's email by calling Supabase **Admin Auth** (`GetUserById`) per row — emails live in `auth.users`, not in the app tables.
2. `IEmailService.SendTemplateEmailAsync` POSTs to `https://api.brevo.com/v3/smtp/email` with a numeric `templateId` and a `params` dictionary.
3. Only on a successful send does the job flip the flag back in Supabase. **This flag is the sole idempotency guard** — the schedule re-runs every minute, so any code path that sends before marking will spam users on the next tick.

### Things that are easy to get wrong

- **Template IDs are hardcoded constants inside the job class** (e.g. `TeamReviewNotificationTemplateId = 5` in `NotifyReviewerOfTeamReviewJob`), and the `templateParams` keys must match the `{{params.x}}` placeholders defined in the Brevo dashboard. Neither side is validated at compile time or at startup — a typo just produces a blank field in a delivered email.
- **`ReviewEmailFactory` is dead code.** It is registered in DI and builds plain-text `EmailMessage` bodies for all four email types, but nothing resolves it — the service moved to Brevo templates. Same for `IEmailService.SendEmailAsync` (the non-template overload).
- **Most of `ISupabaseService` is unused.** Only `GetTeamReviewsForReviewerNotificationAsync` / `MarkTeamReviewerNotifiedAsync` have a caller. The other six methods (completed trade reviews, team-review-ready-with-YouTube-link, trade reviewer notifications) are fully implemented but have no job driving them. Adding one of those emails means writing a job and registering it in `Program.cs`, not writing new data access.
- **The N+1 admin-auth call is deliberate but unbounded.** Each row triggers a separate `GetUserById` round trip; there is no `.Limit()` on any query, so a large backlog means a long job run. `[DisallowConcurrentExecution]` on the job prevents overlapping ticks.
- **`TradeReview` maps to the `Trades` table**, not `TradeReviews`. `TeamReview` maps to `TeamReviews`.
- **`SupabaseModels/User.cs` is inert** — it uses `System.ComponentModel.DataAnnotations.Schema` attributes rather than Postgrest ones and is not a `BaseModel`, so it can't be used with `_supabase.From<T>()`. User lookups go through Admin Auth instead.
- Jobs swallow their own exceptions and log; a failure never surfaces as a non-zero exit or a stopped host.

### Logging

`UseBoomBustLogging` (from the private `BoomBust.Logging` package) configures Serilog: console plus rolling file at `logs/email-service-.txt`, with `Microsoft`, `System`, and `Quartz` namespaces forced to Warning. `BetterStack` keys in the config template feed that package.

## Deployment

Push to `main` triggers `.github/workflows/production-deploy.yml`, which builds the Dockerfile and pushes `jackbruzan/email_service:latest` and `:<run_number>` to Docker Hub. The build stage injects the GitHub Packages PAT as a BuildKit secret (`--mount=type=secret,id=nuget_token`) so it never lands in an image layer; the runtime stage runs as non-root `appuser`. There is no CI build or test step on pull requests.

## Related repositories

`.github/copilot-instructions.md` and `.github/agents/*.md` describe the **Boom Bust Fantasy Nuxt 4 front-end**, not this service. Ignore them when working on C# code here. The only overlap that matters is the shared Supabase Postgres instance — the `Trades` and `TeamReviews` tables this service reads and writes are owned by that app, and the front-end enforces RLS on them (this service bypasses RLS via the service role key).
