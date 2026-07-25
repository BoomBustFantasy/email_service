using EmailService.SupabaseModels;
using Microsoft.Extensions.Options;
using Supabase;
using EmailService.Configs;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using EmailService.Templates;

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
            // Call read_email_queue wrapper function
            var parameters = new Dictionary<string, object>
            {
                { "p_batch_size", batchSize },
                { "p_visibility_timeout_seconds", VisibilityTimeoutSeconds }
            };

            var result = await supabaseClient.Rpc<PgmqMessage[]>("read_email_queue", parameters);
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
            // Parse message payload (JObject from Newtonsoft.Json)
            var messageJson = JsonConvert.SerializeObject(message.Message);
            var payload = JsonConvert.DeserializeObject<EmailQueuePayload>(messageJson);

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
                _logger.LogWarning("Outbox record {OutboxId} not found - archiving message from queue",
                    payload.OutboxId);
                await ArchiveMessageFromPgmq(supabaseClient, message.MsgId);
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
                    "Message with key {IdempotencyKey} already processed successfully - archiving from queue",
                    outboxRecord.IdempotencyKey);

                await ArchiveMessageFromPgmq(supabaseClient, message.MsgId);
                _metrics.IncrementSuccessful();
                return;
            }

            // Check suppression list
            var isSuppressed = await CheckSuppressionAsync(supabaseClient, outboxRecord.RecipientEmail);
            if (isSuppressed)
            {
                _logger.LogInformation(
                    "Recipient {Email} is suppressed - skipping and archiving from queue",
                    outboxRecord.RecipientEmail);

                await LogDeliveryAttempt(supabaseClient, outboxRecord, message.ReadCount + 1,
                    "suppressed", "Recipient is on suppression list");
                await ArchiveMessageFromPgmq(supabaseClient, message.MsgId);
                _metrics.IncrementSuccessful();
                return;
            }

            // Send email via Brevo
            bool sent = false;
            string? errorMessage = null;

            try
            {
                // Get scoped services for email sending
                using var scope = _serviceProvider.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                var templateService = scope.ServiceProvider.GetRequiredService<ITemplateService>();

                // Resolve template ID from template key
                var templateId = templateService.ResolveTemplateId(outboxRecord.TemplateKey);

                // Convert template variables (JObject) to Dictionary<string, string>
                var templateParams = new Dictionary<string, string>();
                if (outboxRecord.TemplateVariables != null)
                {
                    var jObject = outboxRecord.TemplateVariables as JObject ??
                                  JObject.Parse(JsonConvert.SerializeObject(outboxRecord.TemplateVariables));

                    foreach (var prop in jObject.Properties())
                    {
                        templateParams[prop.Name] = prop.Value.ToString();
                    }
                }

                // Send the email
                sent = await emailService.SendTemplateEmailAsync(
                    outboxRecord.RecipientEmail,
                    templateId,
                    templateParams);

                if (sent)
                {
                    _logger.LogInformation(
                        "Successfully sent email {OutboxId} to {Email} using template {TemplateKey}",
                        outboxRecord.Id, outboxRecord.RecipientEmail, outboxRecord.TemplateKey);
                }
                else
                {
                    errorMessage = "Email service returned false";
                    _logger.LogWarning(
                        "Failed to send email {OutboxId} to {Email}: {Error}",
                        outboxRecord.Id, outboxRecord.RecipientEmail, errorMessage);
                }
            }
            catch (Exception ex)
            {
                sent = false;
                errorMessage = ex.Message;
                _logger.LogError(ex,
                    "Error sending email {OutboxId} to {Email}",
                    outboxRecord.Id, outboxRecord.RecipientEmail);
            }

            // Log delivery attempt with actual status
            await LogDeliveryAttempt(
                supabaseClient,
                outboxRecord,
                message.ReadCount + 1,
                sent ? "sent" : "failed",
                errorMessage);

            if (sent)
            {
                // Archive message from queue (marks as successfully processed)
                await ArchiveMessageFromPgmq(supabaseClient, message.MsgId);
                _metrics.IncrementSuccessful();

                _logger.LogInformation("Successfully processed message {MsgId} for outbox {OutboxId}",
                    message.MsgId, payload.OutboxId);
            }
            else
            {
                // Don't archive - let pgmq retry based on visibility timeout
                _metrics.IncrementFailed();
                throw new InvalidOperationException($"Email sending failed: {errorMessage}");
            }
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
            var normalizedEmail = email.ToLowerInvariant();
            var suppression = await supabaseClient
                .From<EmailSuppression>()
                .Where(x => x.Email == normalizedEmail)
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

    private async Task ArchiveMessageFromPgmq(Client supabaseClient, long msgId)
    {
        try
        {
            // Call archive_email_message wrapper function (marks as successfully processed)
            var parameters = new Dictionary<string, object>
            {
                { "p_msg_id", msgId }
            };

            await supabaseClient.Rpc("archive_email_message", parameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error archiving message {MsgId} from pgmq", msgId);
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
            // TODO: Fix metrics model to match get_email_queue_metrics wrapper return structure
            // var dlqMetrics = await supabaseClient.Rpc<PgmqMetrics[]>("get_email_queue_metrics", new Dictionary<string, object>());
            // var dlqCount = (int)(dlqMetrics?.FirstOrDefault()?.QueueLength ?? 0);
            // _metrics.SetQueueDepth(dlqCount);
            // if (dlqCount >= _reliabilityConfig.DeadLetterQueueSizeThreshold)
            // {
            //     _logger.LogWarning(
            //         "Dead letter queue size ({DlqCount}) exceeds threshold ({Threshold})",
            //         dlqCount,
            //         _reliabilityConfig.DeadLetterQueueSizeThreshold);
            //     // TODO: Send alert email
            // }
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
    public required object Message { get; set; }
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
    [JsonProperty("outbox_id")]
    public Guid OutboxId { get; set; }

    [JsonProperty("recipient_email")]
    public string RecipientEmail { get; set; } = string.Empty;

    [JsonProperty("template_key")]
    public string TemplateKey { get; set; } = string.Empty;
}

