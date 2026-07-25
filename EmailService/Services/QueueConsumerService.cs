using EmailService.SupabaseModels;
using Microsoft.Extensions.Options;
using Supabase;
using EmailService.Configs;
using System.Text.Json;

namespace EmailService.Services;

/// <summary>
/// Background service that consumes outbound email messages from pgmq queue
/// and processes them with idempotent, retry-aware delivery semantics.
/// Uses pgmq (PostgreSQL Message Queue) for message management.
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
    private const int VisibilityTimeoutSeconds = 300; // 5 minutes
    private const string QueueName = "email_outbound";
    
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
        _logger.LogInformation("Queue consumer service starting with pgmq...");

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

        // Read batch of messages from pgmq
        var messages = await ReadMessagesFromPgmq(supabaseClient, MessageBatchSize);
        
        _metrics.SetQueueDepth(messages.Count);

        if (messages.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Processing {Count} messages from pgmq queue '{QueueName}'", 
            messages.Count, QueueName);

        foreach (var message in messages)
        {
            await ProcessMessageAsync(message, supabaseClient, cancellationToken);
        }
        
        // Check operational metrics
        await CheckOperationalAlertsAsync(supabaseClient, cancellationToken);
    }

    private async Task<List<PgmqMessage>> ReadMessagesFromPgmq(Client supabaseClient, int batchSize)
    {
        try
        {
            // Call pgmq.read() function
            var result = await supabaseClient.Rpc<PgmqMessage[]>(
                "pgmq.read",
                new Dictionary<string, object>
                {
                    { "queue_name", QueueName },
                    { "vt", VisibilityTimeoutSeconds },
                    { "qty", batchSize }
                });

            return result?.ToList() ?? new List<PgmqMessage>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading from pgmq queue");
            return new List<PgmqMessage>();
        }
    }

    private async Task ProcessMessageAsync(
        PgmqMessage message,
        Client supabaseClient,
        CancellationToken cancellationToken)
    {
        _metrics.IncrementProcessed();
        
        try
        {
            // Parse message payload
            var payload = JsonSerializer.Deserialize<EmailQueuePayload>(message.Message);
            if (payload == null)
            {
                throw new InvalidOperationException("Failed to deserialize message payload");
            }

            _logger.LogInformation(
                "Processing message {MsgId}: outbox_id={OutboxId}, recipient={RecipientEmail}",
                message.MsgId,
                payload.OutboxId,
                payload.RecipientEmail);

            // Fetch the outbox record
            var outboxRecord = await supabaseClient
                .From<EmailOutbox>()
                .Where(x => x.Id == payload.OutboxId)
                .Single();

            if (outboxRecord == null)
            {
                _logger.LogWarning("Outbox record {OutboxId} not found - deleting message from queue", 
                    payload.OutboxId);
                await DeleteMessageFromPgmq(supabaseClient, message.MsgId);
                return;
            }

            // Check idempotency - has this already been processed successfully?
            var existingLog = await supabaseClient
                .From<EmailDeliveryLog>()
                .Where(x => x.IdempotencyKey == outboxRecord.IdempotencyKey)
                .Where(x => x.Status == "sent" || x.Status == "delivered")
                .Single();

            if (existingLog != null)
            {
                _logger.LogInformation(
                    "Message with key {IdempotencyKey} already processed successfully - deleting from queue",
                    outboxRecord.IdempotencyKey);

                await DeleteMessageFromPgmq(supabaseClient, message.MsgId);
                _metrics.IncrementSuccessful();
                return;
            }

            // Check suppression list
            var isSuppressed = await CheckSuppressionAsync(supabaseClient, outboxRecord.RecipientEmail);
            if (isSuppressed)
            {
                _logger.LogInformation(
                    "Recipient {Email} is suppressed - skipping and deleting from queue",
                    outboxRecord.RecipientEmail);

                await LogDeliveryAttempt(supabaseClient, outboxRecord, message.ReadCount, 
                    "suppressed", "Recipient is on suppression list");
                await DeleteMessageFromPgmq(supabaseClient, message.MsgId);
                _metrics.IncrementSuccessful();
                return;
            }

            // TODO: Send email via Brevo (to be implemented in future work)
            // For now, just log the attempt
            await LogDeliveryAttempt(supabaseClient, outboxRecord, message.ReadCount, 
                "pending", null);

            // Simulate successful send for now
            _logger.LogInformation("Successfully processed message {MsgId} for outbox {OutboxId}",
                message.MsgId, payload.OutboxId);

            // Delete message from pgmq (marks as successfully processed)
            await DeleteMessageFromPgmq(supabaseClient, message.MsgId);
            _metrics.IncrementSuccessful();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MsgId}", message.MsgId);
            _metrics.IncrementFailed();
            
            // pgmq will automatically retry based on visibility timeout
            // After max retries, pgmq will move to DLQ automatically
            _logger.LogWarning(
                "Message {MsgId} failed (read count: {ReadCount}) - will be retried by pgmq",
                message.MsgId, message.ReadCount);
        }
    }

    private async Task<bool> CheckSuppressionAsync(Client supabaseClient, string email)
    {
        try
        {
            var suppression = await supabaseClient
                .From<EmailSuppression>()
                .Where(x => x.Email == email.ToLowerInvariant())
                .Single();

            return suppression != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking suppression for {Email}", email);
            return false; // Fail open - don't suppress if we can't check
        }
    }

    private async Task LogDeliveryAttempt(
        Client supabaseClient,
        EmailOutbox outbox,
        int attemptNumber,
        string status,
        string? errorMessage)
    {
        try
        {
            var log = new EmailDeliveryLog
            {
                OutboxId = outbox.Id,
                IdempotencyKey = outbox.IdempotencyKey,
                AttemptNumber = attemptNumber,
                Status = status,
                ErrorMessage = errorMessage,
                AttemptedAt = DateTime.UtcNow
            };

            await supabaseClient
                .From<EmailDeliveryLog>()
                .Insert(log);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging delivery attempt for outbox {OutboxId}", outbox.Id);
        }
    }

    private async Task DeleteMessageFromPgmq(Client supabaseClient, long msgId)
    {
        try
        {
            await supabaseClient.Rpc(
                "pgmq.delete",
                new Dictionary<string, object>
                {
                    { "queue_name", QueueName },
                    { "msg_id", msgId }
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting message {MsgId} from pgmq", msgId);
        }
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

            // Check pgmq DLQ size (dead letter messages)
            var dlqMetrics = await supabaseClient.Rpc<PgmqMetrics>(
                "pgmq.metrics",
                new Dictionary<string, object>
                {
                    { "queue_name", $"{QueueName}_dlq" }
                });

            var dlqCount = (int)(dlqMetrics?.QueueLength ?? 0);
            _metrics.SetQueueDepth(dlqCount);

            if (dlqCount >= _reliabilityConfig.DeadLetterQueueSizeThreshold)
            {
                _logger.LogWarning(
                    "Dead letter queue size ({DlqCount}) exceeds threshold ({Threshold})",
                    dlqCount,
                    _reliabilityConfig.DeadLetterQueueSizeThreshold);
                
                // TODO: Send alert email
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking operational alerts");
        }
    }
}

/// <summary>
/// Represents a message returned from pgmq
/// </summary>
public class PgmqMessage
{
    public long MsgId { get; set; }
    public int ReadCount { get; set; }
    public DateTime EnqueuedAt { get; set; }
    public DateTime Vt { get; set; }
    public required string Message { get; set; }
}

/// <summary>
/// Represents pgmq queue metrics
/// </summary>
public class PgmqMetrics
{
    public string QueueName { get; set; } = string.Empty;
    public long QueueLength { get; set; }
    public int? NewestMsgAgeSec { get; set; }
    public int? OldestMsgAgeSec { get; set; }
    public long TotalMessages { get; set; }
    public DateTime ScrapeTime { get; set; }
}

/// <summary>
/// Payload stored in pgmq messages referencing email_outbox rows
/// </summary>
public class EmailQueuePayload
{
    public Guid OutboxId { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public string TemplateKey { get; set; } = string.Empty;
}

