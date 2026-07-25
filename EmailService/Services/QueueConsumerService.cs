using EmailService.SupabaseModels;
using Microsoft.Extensions.Options;
using Supabase;
using EmailService.Configs;
using static Supabase.Postgrest.Constants;
using System.Text.Json;

namespace EmailService.Services;

/// <summary>
/// Background service that consumes outbound email messages from the queue
/// and processes them with idempotent, retry-aware delivery semantics.
/// Includes exponential backoff, dead letter queue, and metrics tracking.
/// </summary>
public class QueueConsumerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QueueConsumerService> _logger;
    private readonly AppConfig _appConfig;
    private readonly QueueReliabilityConfig _reliabilityConfig;
    private readonly QueueMetrics _metrics;
    private const int PollingIntervalSeconds = 5;
    private const int MessageBatchSize = 10;
    private const int LockDurationMinutes = 5;
    
    public QueueMetrics Metrics => _metrics;

    public QueueConsumerService(
        IServiceProvider serviceProvider,
        ILogger<QueueConsumerService> logger,
        IOptions<AppConfig> appConfig,
        IOptions<QueueReliabilityConfig> reliabilityConfig,
        QueueMetrics metrics)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _appConfig = appConfig.Value;
        _reliabilityConfig = reliabilityConfig.Value;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Queue consumer service starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessQueueBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing queue batch");
            }

            await Task.Delay(TimeSpan.FromSeconds(PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Queue consumer service stopping...");
    }

    private async Task ProcessQueueBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var supabaseClient = scope.ServiceProvider.GetRequiredService<Client>();

        // Fetch pending messages that are not locked and haven't exceeded retry count
        var now = DateTime.UtcNow;
        var response = await supabaseClient
            .From<OutboundEmailQueue>()
            .Where(x => x.Status == "pending")
            .Where(x => x.RetryCount < _reliabilityConfig.MaxRetryAttempts)
            .Filter("locked_until", Operator.LessThan, now.ToString("o"))
            .Limit(MessageBatchSize)
            .Get();

        var messages = response.Models;
        
        // Update queue depth metric
        _metrics.SetQueueDepth(messages.Count);

        if (messages.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Processing {Count} messages from queue", messages.Count);

        foreach (var message in messages)
        {
            await ProcessMessageAsync(message, supabaseClient, cancellationToken);
        }
        
        // Check if we need to send degradation alerts
        await CheckOperationalAlertsAsync(supabaseClient, cancellationToken);
    }

    private async Task ProcessMessageAsync(
        OutboundEmailQueue message,
        Client supabaseClient,
        CancellationToken cancellationToken)
    {
        _metrics.IncrementProcessed();
        
        try
        {
            // Apply exponential backoff if this is a retry
            if (message.RetryCount > 0)
            {
                var backoffDelay = CalculateBackoffDelay(message.RetryCount);
                _logger.LogInformation(
                    "Message {MessageId} retry attempt {RetryCount} - applying {BackoffMs}ms backoff",
                    message.Id, message.RetryCount, backoffDelay);
                    
                await Task.Delay(backoffDelay, cancellationToken);
                _metrics.IncrementRetries();
            }
            
            // Acquire lock on message
            var lockUntil = DateTime.UtcNow.AddMinutes(LockDurationMinutes);
            var lockUpdate = await supabaseClient
                .From<OutboundEmailQueue>()
                .Where(x => x.Id == message.Id)
                .Where(x => x.LockedUntil == null || x.LockedUntil < DateTime.UtcNow)
                .Set(x => x.LockedUntil!, lockUntil)
                .Update();

            if (lockUpdate.Models.Count == 0)
            {
                // Another consumer acquired the lock
                _logger.LogDebug("Message {MessageId} already locked by another consumer", message.Id);
                return;
            }

            // Check idempotency - has this already been processed?
            var existingLog = await supabaseClient
                .From<EmailDeliveryLog>()
                .Where(x => x.IdempotencyKey == message.IdempotencyKey)
                .Where(x => x.Status == "sent" || x.Status == "delivered")
                .Single();

            if (existingLog != null)
            {
                _logger.LogInformation(
                    "Message {MessageId} with key {IdempotencyKey} already processed - marking complete",
                    message.Id,
                    message.IdempotencyKey);

                await MarkMessageCompleteAsync(message.Id, supabaseClient, cancellationToken);
                _metrics.IncrementSuccessful();
                return;
            }

            // Process the message
            // Note: Actual email sending will be implemented in later tickets
            // For now, we establish the queue consumption pattern
            _logger.LogInformation(
                "Processing message {MessageId}: template={TemplateKey}, recipient={RecipientEmail}",
                message.Id,
                message.TemplateKey,
                message.RecipientEmail);

            // Create delivery log entry
            var deliveryLog = new EmailDeliveryLog
            {
                IdempotencyKey = message.IdempotencyKey,
                QueueMessageId = message.Id,
                TemplateKey = message.TemplateKey,
                RecipientEmail = message.RecipientEmail,
                TemplateVariables = message.TemplateVariables,
                Status = "pending",
                CreatedAt = DateTime.UtcNow
            };

            await supabaseClient
                .From<EmailDeliveryLog>()
                .Insert(deliveryLog);

            // TODO: In subsequent tickets, this is where we will:
            // 1. Resolve template key to Brevo template ID
            // 2. Validate template variables
            // 3. Send via Brevo
            // 4. Update delivery log with result

            // For now, mark as complete
            await MarkMessageCompleteAsync(message.Id, supabaseClient, cancellationToken);
            _metrics.IncrementSuccessful();

            _logger.LogInformation("Successfully processed message {MessageId}", message.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId}", message.Id);
            _metrics.IncrementFailed();
            await HandleMessageErrorAsync(message, ex.Message, supabaseClient, cancellationToken);
        }
    }

    private async Task MarkMessageCompleteAsync(
        long messageId,
        Client supabaseClient,
        CancellationToken cancellationToken)
    {
        await supabaseClient
            .From<OutboundEmailQueue>()
            .Where(x => x.Id == messageId)
            .Set(x => x.Status!, "completed")
            .Set(x => x.LockedUntil!, null)
            .Update();
    }

    private async Task HandleMessageErrorAsync(
        OutboundEmailQueue message,
        string errorMessage,
        Client supabaseClient,
        CancellationToken cancellationToken)
    {
        var newRetryCount = message.RetryCount + 1;
        var shouldDeadLetter = newRetryCount >= _reliabilityConfig.MaxRetryAttempts;

        if (shouldDeadLetter)
        {
            _logger.LogWarning(
                "Message {MessageId} exceeded max retries ({MaxRetries}) - moving to dead letter queue",
                message.Id,
                _reliabilityConfig.MaxRetryAttempts);

            // Move to dead letter queue
            await MoveToDeadLetterQueueAsync(message, errorMessage, supabaseClient, cancellationToken);
            _metrics.IncrementDeadLettered();
        }
        else
        {
            // Increment retry count and release lock
            await supabaseClient
                .From<OutboundEmailQueue>()
                .Where(x => x.Id == message.Id)
                .Set(x => x.RetryCount!, newRetryCount)
                .Set(x => x.LastError!, errorMessage)
                .Set(x => x.Status!, "pending")
                .Set(x => x.LockedUntil!, null)
                .Update();

            _logger.LogInformation(
                "Message {MessageId} failed - retry {RetryCount}/{MaxRetries}",
                message.Id,
                newRetryCount,
                _reliabilityConfig.MaxRetryAttempts);
        }
    }

    private async Task MoveToDeadLetterQueueAsync(
        OutboundEmailQueue message,
        string errorMessage,
        Client supabaseClient,
        CancellationToken cancellationToken)
    {
        // Create dead letter entry
        var deadLetter = new DeadLetterEmail
        {
            OriginalQueueId = message.Id,
            RecipientEmail = message.RecipientEmail,
            TemplateKey = message.TemplateKey,
            TemplateParams = message.TemplateVariables,
            Attempts = message.RetryCount + 1,
            LastError = errorMessage,
            CreatedAt = message.CreatedAt ?? DateTime.UtcNow,
            MovedToDlqAt = DateTime.UtcNow
        };

        await supabaseClient
            .From<DeadLetterEmail>()
            .Insert(deadLetter);

        // Mark original message as dead_lettered
        await supabaseClient
            .From<OutboundEmailQueue>()
            .Where(x => x.Id == message.Id)
            .Set(x => x.Status!, "dead_lettered")
            .Set(x => x.LastError!, errorMessage)
            .Set(x => x.LockedUntil!, null)
            .Update();

        _logger.LogError(
            "Message {MessageId} moved to dead letter queue after {Attempts} attempts. Error: {Error}",
            message.Id,
            message.RetryCount + 1,
            errorMessage);
    }

    private int CalculateBackoffDelay(int retryCount)
    {
        var delay = (int)(_reliabilityConfig.BaseBackoffMs * 
                         Math.Pow(_reliabilityConfig.BackoffMultiplier, retryCount - 1));
        
        return Math.Min(delay, _reliabilityConfig.MaxBackoffMs);
    }

    private async Task CheckOperationalAlertsAsync(
        Client supabaseClient,
        CancellationToken cancellationToken)
    {
        // Skip if no alert email configured
        if (string.IsNullOrWhiteSpace(_reliabilityConfig.OperationalAlertEmail))
        {
            return;
        }

        try
        {
            // Check success rate degradation
            if (_metrics.TotalProcessed > 20 && 
                _metrics.SuccessRate < _reliabilityConfig.SuccessRateDegradationThreshold)
            {
                _logger.LogWarning(
                    "Success rate degradation detected: {SuccessRate:P2} below threshold {Threshold:P2}",
                    _metrics.SuccessRate,
                    _reliabilityConfig.SuccessRateDegradationThreshold);
                
                // TODO: Send alert email (will be implemented when email sending is wired up)
            }

            // Check dead letter queue size
            var dlqResponse = await supabaseClient
                .From<DeadLetterEmail>()
                .Get();
            
            var dlqCount = dlqResponse.Models.Count;

            if (dlqCount >= _reliabilityConfig.DeadLetterQueueSizeThreshold)
            {
                _logger.LogWarning(
                    "Dead letter queue size threshold exceeded: {DlqCount} >= {Threshold}",
                    dlqCount,
                    _reliabilityConfig.DeadLetterQueueSizeThreshold);
                
                // TODO: Send alert email (will be implemented when email sending is wired up)
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking operational alerts");
        }
    }
}
