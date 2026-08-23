# Email Service Test Suite

## Overview

This test project provides automated tests for the email service's core functionality, including:
- Queue consumer behavior
- Template validation
- Retry/DLQ transitions
- Webhook handling
- Suppression decisions
- Replay controls

## Test Categories

### 1. Template Contract Validation Tests

**Location:** `Templates/TemplateContractTests.cs`

**Coverage:**
- Validates required fields for all template contracts
- Tests email address format validation
- Confirms proper URL validation
- Verifies ToBrevoParams() conversion

**Example Test:**
```csharp
[Fact]
public void TeamReviewNotificationContract_ValidContract_PassesValidation()
{
    var contract = new TeamReviewNotificationContract { /* ... */ };
    var isValid = contract.Validate(out var errors);
    isValid.Should().BeTrue();
}
```

### 2. Webhook Controller Tests

**Location:** `Controllers/BrevoWebhookControllerTests.cs`

**Coverage:**
- Webhook signature verification (HMAC-SHA256)
- Event-to-status mapping
- Delivery log updates
- Suppression record creation
- Error handling

**Expected Behavior:**
| Brevo Event | Expected Action |
|-------------|-----------------|
| `delivered` | Update EmailDeliveryLog status to "delivered", set delivered_at timestamp |
| `hard_bounce` | Set status "bounced", create EmailSuppression record |
| `unsubscribed` | Set status "unsubscribed", create EmailSuppression record |
| `opened` | Set status "opened" |

### 3. Admin Controller Tests

**Location:** `Controllers/AdminControllerTests.cs`

**Coverage:**
- API key authentication
- Batch size validation (1-100 messages)
- Replay strategies (reset original vs. create new)
- Idempotency key preservation
- Error responses

**Key Scenarios:**
- Empty batch → 400 Bad Request
- > 100 messages → 400 Bad Request
- Valid batch → 200 OK with individual results

### 4. Queue Metrics Tests

**Location:** `Services/QueueMetricsTests.cs`

**Coverage:**
- Counter increment operations
- Success rate calculation
- Thread-safe concurrent updates
- Snapshot generation

**Expected Formulas:**
- Success Rate: `SuccessfulDeliveries / TotalProcessed`
- Backoff Delay: `BaseBackoffMs × (BackoffMultiplier ^ (retryCount - 1))`
- Delay Cap: `Math.Min(calculatedDelay, MaxBackoffMs)`

### 5. Dead Letter Queue Tests

**Expected Behavior:**
- Message moved to DLQ after `MaxRetryAttempts` (default: 3)
- DLQ record includes original queue ID, attempts, last error
- Original queue message status set to "dead_lettered"

## Running Tests

```bash
# Run all tests
dotnet test EmailService.Tests/EmailService.Tests.csproj

# Run specific category
dotnet test --filter "FullyQualifiedName~TemplateContractTests"

# Run with detailed output
dotnet test --verbosity detailed

# Generate coverage report
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

## Test Patterns

### Unit Test Pattern

```csharp
public class FeatureTests
{
    [Fact]
    public void Method_Scenario_ExpectedBehavior()
    {
        // Arrange
        var service = new Service(/* dependencies */);
        
        // Act
        var result = service.Method(input);
        
        // Assert
        result.Should().Be(expected);
    }
}
```

### Integration Test Pattern

```csharp
public class IntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Endpoint_ValidRequest_ReturnsSuccess()
    {
        // Arrange
        var client = _factory.CreateClient();
        
        // Act
        var response = await client.PostAsync("/endpoint", content);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

## Mocking Strategy

### Brevo API Mocking

Since tests should never make live API calls to Brevo, we mock all HTTP interactions:

```csharp
var mockHttp = new MockHttpMessageHandler();
mockHttp.When("https://api.brevo.com/v3/smtp/email")
    .Respond("application/json", "{ \"messageId\": \"test-123\" }");

var httpClient = mockHttp.ToHttpClient();
```

### Supabase Mocking

Supabase client interactions are mocked using Moq:

```csharp
var supabaseClientMock = new Mock<Supabase.Client>();
var tableMock = new Mock<Supabase.Postgrest.Table<OutboundEmailQueue>>();

supabaseClientMock
    .Setup(x => x.From<OutboundEmailQueue>())
    .Returns(tableMock.Object);
```

## Test Data Fixtures

### Valid Email Addresses
- `user@example.com`
- `test.user+tag@example.co.uk`
- `user_name@sub.example.com`

### Invalid Email Addresses
- `` (empty)
- `invalid-email`
- `@example.com`
- `user@`
- `user@.com`

### Queue Message States
- `pending` - Available for processing
- `completed` - Successfully delivered
- `dead_lettered` - Failed after max retries

## Known Limitations

### Current Test Suite Limitations

1. **Supabase Integration**: Full Supabase mocking is complex due to the fluent API. Integration tests would require a test database.
   
2. **Webhook Signature Verification**: Testing HMAC-SHA256 signature validation requires crafting valid signatures with known secrets.

3. **Background Service Testing**: QueueConsumerService runs as a BackgroundService, requiring special testing infrastructure.

4. **Time-Dependent Tests**: Backoff delays and timestamp assertions can be flaky without time mocking.

### Future Test Enhancements

- [ ] Add integration tests with test Supabase instance
- [ ] Implement HttpClient mocking for Brevo API calls
- [ ] Add BackgroundService testing infrastructure
- [ ] Create test fixtures for common scenarios
- [ ] Add performance/load tests for queue consumer
- [ ] Implement time-travel testing for backoff logic
- [ ] Add contract tests against actual Brevo templates
- [ ] Create chaos engineering tests for failure scenarios

## Continuous Integration

### GitHub Actions Workflow

```yaml
name: Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '9.0'
      - run: dotnet test --configuration Release
```

## Coverage Goals

| Category | Target Coverage |
|----------|----------------|
| Template Validation | 100% |
| Webhook Event Mapping | 100% |
| Admin API Auth | 100% |
| Queue Metrics | 90% |
| Retry/DLQ Logic | 90% |
| Overall | 85%+ |

## Debugging Tests

### VS Code Launch Configuration

```json
{
  "name": ".NET Core Test",
  "type": "coreclr",
  "request": "launch",
  "preLaunchTask": "build",
  "program": "dotnet",
  "args": ["test", "${workspaceFolder}/EmailService.Tests/EmailService.Tests.csproj"],
  "cwd": "${workspaceFolder}",
  "stopAtEntry": false
}
```

### xUnit Test Explorer

Visual Studio and VS Code both support xUnit test discovery and execution through Test Explorer.

## Contributing

When adding new features, please:
1. Write tests first (TDD approach)
2. Ensure all existing tests pass
3. Add integration tests for new endpoints
4. Update this README with new test categories
5. Maintain or improve coverage percentages

## Related Documentation

- [Template Contracts](../../docs/template-contracts.md)
- [Retry/Backoff/DLQ](../../docs/retry-backoff-dlq.md)
- [Brevo Webhook Integration](../../docs/brevo-webhook-integration.md)
- [Dead Letter Replay API](../../docs/dead-letter-replay-api.md)
