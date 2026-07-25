using EmailService.SupabaseModels;
using Microsoft.Extensions.Options;
using Supabase;
using EmailService.Configs;
using static Supabase.Postgrest.Constants;

namespace EmailService.Services;

/// <summary>
/// Background service that consumes outbound email messages from the queue
/// and processes them with idempotent, retry-aware delivery semantics.
/// </summary>
public class QueueConsumerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QueueConsumerService> _logger;
    private readonly AppConfig _appConfig;
    private const int PollingIntervalSeconds = 5;
    private const int MessageBatchSize = 10;
    private const int MaxRetryAttempts = 3;
    private const int LockDurationMinutes = 5;

    public QueueConsumerService(
        IServiceProvider serviceProvider,
        ILogger<QueueConsumerService> logger,
        IOptions<AppConfig> appConfig)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _appConfig = appConfig.Value;
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
            .Where(x => x.RetryCount < MaxRetryAttempts)
            .Filter("locked_until", Operator.LessThan, now.ToString("o"))
            .Limit(MessageBatchSize)
            .Get();

        var messages = response.Models;

        if (messages.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Processing {Count} messages from queue", messages.Count);

        foreach (var message in messages)
        {
            await ProcessMessageAsync(message, supabaseClient, cancellationToken);
        }
    }

    private async Task ProcessMessageAsync(
        OutboundEmailQueue message,
        Client supabaseClient,
        CancellationToken cancellationToken)
    {
        try
        {
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

            _logger.LogInformation("Successfully processed message {MessageId}", message.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId}", message.Id);
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
        var shouldDeadLetter = newRetryCount >= MaxRetryAttempts;

        await supabaseClient
            .From<OutboundEmailQueue>()
            .Where(x => x.Id == message.Id)
            .Set(x => x.RetryCount!, newRetryCount)
            .Set(x => x.LastError!, errorMessage)
            .Set(x => x.Status!, shouldDeadLetter ? "dead_letter" : "pending")
            .Set(x => x.LockedUntil!, null)
            .Update();

        if (shouldDeadLetter)
        {
            _logger.LogWarning(
                "Message {MessageId} moved to dead letter queue after {RetryCount} attempts",
                message.Id,
                newRetryCount);
        }
        else
        {
            _logger.LogInformation(
                "Message {MessageId} retry count incremented to {RetryCount}",
                message.Id,
                newRetryCount);
        }
    }
}
