# Template Contract Engine

## Overview

The template contract engine provides a type-safe, validated approach to sending transactional emails using Brevo templates. It replaces raw template ID usage with a contract-based system that:

1. **Validates required variables** before sending
2. **Maps internal template keys** to Brevo numeric IDs
3. **Manages sender identity** with defaults and per-template overrides

## Architecture

### Core Components

#### 1. `ITemplateContract`
Base interface for all email template contracts. Each contract:
- Defines required template variables as strongly-typed properties
- Implements validation logic
- Converts data to Brevo-compatible parameter dictionaries
- Optionally specifies sender override

#### 2. `TemplateConfig`
Configuration class that manages:
- **TemplateIdMap**: Maps internal keys → Brevo numeric template IDs
- **DefaultSender**: Global sender identity (email + name)

#### 3. `ITemplateService`
Service that:
- Resolves template keys to Brevo IDs
- Validates contracts before sending
- Applies sender identity (override or default)

#### 4. `BrevoEmailService`
Extended with `SendTemplateEmailAsync(ITemplateContract)` method that:
- Validates contract
- Resolves template ID
- Applies sender identity
- Sends email via Brevo API

## Usage

### Creating a Template Contract

```csharp
using EmailService.Templates.Contracts;

// Example: Team review notification
var contract = new TeamReviewNotificationContract
{
    RecipientEmail = "user@example.com",
    RecipientName = "John Doe",
    ReviewerName = "Jane Smith",
    TeamName = "Dream Team",
    LeagueName = "Fantasy League 2024",
    ReviewUrl = "https://app.com/reviews/123"
};
```

### Sending an Email

```csharp
// Inject IEmailService
var emailService = serviceProvider.GetRequiredService<IEmailService>();

// Send using contract (automatic validation + ID resolution)
await emailService.SendTemplateEmailAsync(contract);
```

### With Sender Override

```csharp
var contract = new PurchaseConfirmationContract
{
    RecipientEmail = "buyer@example.com",
    RecipientName = "Alice Johnson",
    ProductName = "Premium Subscription",
    Amount = 29.99m,
    Currency = "USD",
    TransactionId = "TXN-12345",
    PurchaseDate = DateTime.UtcNow,
    SenderOverride = new SenderIdentity 
    { 
        Email = "payments@boombust.app", 
        Name = "Boom Bust Payments" 
    }
};

await emailService.SendTemplateEmailAsync(contract);
```

## Configuration

### appsettings.json

```json
{
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
}
```

### Environment Variables

Template IDs can be overridden via environment:
```bash
Templates__TemplateIdMap__team-review-notification=101
Templates__DefaultSender__Email=custom@domain.com
```

## Available Contracts

### 1. TeamReviewNotificationContract
**Template Key:** `team-review-notification`

**Required Fields:**
- `RecipientEmail` - Recipient's email address
- `RecipientName` - Recipient's display name
- `ReviewerName` - Name of person providing review
- `TeamName` - Team being reviewed
- `LeagueName` - League name
- `ReviewUrl` - URL to view the review

**Brevo Params:**
```json
{
  "recipient_name": "John Doe",
  "reviewer_name": "Jane Smith",
  "team_name": "Dream Team",
  "league_name": "Fantasy League 2024",
  "review_url": "https://app.com/reviews/123"
}
```

### 2. TradeOfferNotificationContract
**Template Key:** `trade-offer-notification`

**Required Fields:**
- `RecipientEmail`
- `RecipientName`
- `SenderTeamName`
- `ReceiverTeamName`
- `LeagueName`
- `TradeUrl`
- `SentPlayers` - Comma-separated player names
- `ReceivedPlayers` - Comma-separated player names

### 3. PurchaseConfirmationContract
**Template Key:** `purchase-confirmation`

**Required Fields:**
- `RecipientEmail`
- `RecipientName`
- `ProductName`
- `Amount` (decimal)
- `Currency` (e.g., "USD")
- `TransactionId`
- `PurchaseDate` (DateTime)

## Validation

Contracts automatically validate:
- ✅ All required fields are present
- ✅ Email addresses are properly formatted
- ✅ Numeric values are in valid ranges
- ✅ Dates are not default values

**Example Error Handling:**
```csharp
try
{
    await emailService.SendTemplateEmailAsync(contract);
}
catch (ArgumentException ex)
{
    // Contract validation failed
    // ex.Message contains: "Template contract validation failed: RecipientEmail is required; Amount must be greater than zero"
}
catch (KeyNotFoundException ex)
{
    // Template key not found in configuration
    // ex.Message contains: "Template key 'unknown-template' is not configured in TemplateIdMap"
}
```

## Benefits

### Type Safety
- Compile-time checks for required fields
- No magic strings for template variables
- IDE autocomplete support

### Validation
- Automatic validation before sending
- Clear error messages for missing/invalid data
- Email format validation

### Configuration Management
- Centralized template ID mapping
- Environment-based configuration
- Easy to update template IDs without code changes

### Sender Flexibility
- Global default sender identity
- Per-template sender overrides
- Per-email sender customization

## Migration Guide

### Before (Raw Template IDs)
```csharp
var templateParams = new Dictionary<string, string>
{
    ["recipient_name"] = recipientName,
    ["team_name"] = teamName
    // Easy to miss required fields!
};

await emailService.SendTemplateEmailAsync(email, 1, templateParams);
// ^ What is template ID 1? No validation!
```

### After (Template Contracts)
```csharp
var contract = new TeamReviewNotificationContract
{
    RecipientEmail = email,
    RecipientName = recipientName,
    ReviewerName = reviewerName,
    TeamName = teamName,
    LeagueName = leagueName,
    ReviewUrl = reviewUrl
    // Compiler enforces all required fields!
};

await emailService.SendTemplateEmailAsync(contract);
// ^ Self-documenting, validated, type-safe
```

## Future Enhancements

- [ ] Dynamic template variable discovery from Brevo API
- [ ] Template preview/testing utilities
- [ ] Localization support for multi-language templates
- [ ] A/B testing framework
- [ ] Template versioning
