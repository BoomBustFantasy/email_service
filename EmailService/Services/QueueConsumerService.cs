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

            // Drop stale mail. A notification that arrives days after the event it
            // describes is worse than silence, and a consumer outage produces a
            // backlog of exactly that.
            if (IsStale(outboxRecord.CreatedAt, _reliabilityConfig.MaxMessageAgeHours, out var age))
            {
                var staleReason =
                    DeliveryStatus.StaleReasonPrefix +
                    $"message is {age.TotalHours:F1}h old, older than the " +
                    $"{_reliabilityConfig.MaxMessageAgeHours}h limit";

                _logger.LogWarning(
                    "Dropping stale message {MsgId} for outbox {OutboxId}: {Reason}",
                    message.MsgId, outboxRecord.Id, staleReason);

                // 'rejected', not 'stale' — the latter is not permitted by the
                // column's CHECK constraint and the row would vanish silently.
                await LogDeliveryAttempt(
                    supabaseClient, outboxRecord, message.ReadCount + 1,
                    DeliveryStatus.Rejected, staleReason);
                await ArchiveMessageFromPgmq(supabaseClient, message.MsgId);
                return;
            }

            // Check idempotency - has this already been processed successfully?
            var existingLog = await supabaseClient
                .From<EmailDeliveryLog>()
                .Where(x => x.IdempotencyKey == outboxRecord.IdempotencyKey)
                // "delivered" is checked for completeness, but no row can currently
                // hold it: the webhook writes it and the CHECK constraint forbids it.
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

            // Check suppression list. Suppression only gates marketing sends —
            // transactional mail (review completions, purchase confirmations)
            // must still reach a recipient who has unsubscribed from marketing.
            var isSuppressed = await CheckSuppressionAsync(
                supabaseClient, outboxRecord.RecipientEmail, outboxRecord.Classification);
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
            string? externalId = null;

            try
            {
                // Get scoped services for email sending
                using var scope = _serviceProvider.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                var templateService = scope.ServiceProvider.GetRequiredService<ITemplateService>();

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

                // Build the typed contract for this template key and validate it
                // before sending. A payload missing required variables is a producer
                // bug that retrying cannot fix, so dead-letter it immediately rather
                // than burning the retry budget (PRD #17: "malformed messages fail
                // fast and visibly").
                var contractRegistry = scope.ServiceProvider.GetRequiredService<ITemplateContractRegistry>();
                if (!contractRegistry.TryCreate(
                        outboxRecord.TemplateKey,
                        outboxRecord.RecipientEmail,
                        templateParams,
                        out var contract,
                        out var registryError))
                {
                    _logger.LogError(
                        "Unknown template key for outbox {OutboxId}: {Error}", outboxRecord.Id, registryError);

                    await RejectMessageAsync(
                        supabaseClient, message, outboxRecord, registryError!);
                    return;
                }

                if (!contract!.Validate(out var validationErrors))
                {
                    var reason = $"Template contract validation failed for '{outboxRecord.TemplateKey}': " +
                                 string.Join("; ", validationErrors);
                    _logger.LogError("Invalid payload for outbox {OutboxId}: {Reason}", outboxRecord.Id, reason);

                    await RejectMessageAsync(supabaseClient, message, outboxRecord, reason);
                    return;
                }

                // Send the email through the contract path, which resolves the Brevo
                // template ID and applies the sender identity for this template.
                var sendResult = await emailService.SendTemplateEmailAsync(contract);
                sent = sendResult.Success;
                externalId = sendResult.MessageId;

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

            // Log delivery attempt with actual status. externalId is Brevo's message
            // ID: it is what BrevoWebhookController matches delivery webhooks against,
            // so a sent row without it can never be reconciled or resolved.
            await LogDeliveryAttempt(
                supabaseClient,
                outboxRecord,
                message.ReadCount + 1,
                sent ? DeliveryStatus.Sent : DeliveryStatus.Failed,
                errorMessage,
                externalId);

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

            // pgmq redelivers on visibility timeout but never gives up on its own —
            // an earlier comment here claimed it dead-letters automatically, and one
            // message was retried 217 times before anyone noticed. Give up explicitly.
            if (HasExhaustedRetries(message.ReadCount, _reliabilityConfig.MaxRetryAttempts))
            {
                await DeadLetterMessageAsync(supabaseClient, message, ex.Message);
                return;
            }

            _logger.LogWarning(
                "Message {MsgId} failed (attempt {Attempt} of {Max}) - pgmq will redeliver",
                message.MsgId, message.ReadCount, _reliabilityConfig.MaxRetryAttempts);
        }
    }

    /// <summary>
    /// pgmq's read_ct counts deliveries, so it is already 1 on the first attempt.
    /// </summary>
    public static bool HasExhaustedRetries(int readCount, int maxRetryAttempts) =>
        maxRetryAttempts > 0 && readCount >= maxRetryAttempts;

    /// <summary>
    /// Moves a message that has run out of attempts onto the dead-letter queue,
    /// where the replay API can find it, and alerts if configured.
    /// </summary>
    private async Task DeadLetterMessageAsync(Client supabaseClient, PgmqMessage message, string reason)
    {
        try
        {
            await supabaseClient.Rpc("dead_letter_email_message", new Dictionary<string, object>
            {
                { "p_msg_id", message.MsgId },
                { "p_reason", reason }
            });

            _metrics.IncrementDeadLettered();

            _logger.LogError(
                "Message {MsgId} dead-lettered after {ReadCount} attempts: {Reason}",
                message.MsgId, message.ReadCount, reason);

            await SendOperationalAlertAsync(
                "Email message dead-lettered",
                $"Message {message.MsgId} failed {message.ReadCount} times and was moved to " +
                $"email_outbound_dlq.{Environment.NewLine}{Environment.NewLine}Last error: {reason}");
        }
        catch (Exception ex)
        {
            // Leave it on the main queue rather than losing it; it will be retried
            // and dead-lettered again on the next pass.
            _logger.LogError(ex, "Failed to dead-letter message {MsgId}", message.MsgId);
        }
    }

    /// <summary>
    /// True when a message is older than the configured limit. A null creation
    /// timestamp is treated as fresh — dropping mail because a timestamp is
    /// missing would be worse than sending it.
    /// </summary>
    public static bool IsStale(DateTime? createdAt, int maxAgeHours, out TimeSpan age)
    {
        age = TimeSpan.Zero;

        if (createdAt is null || maxAgeHours <= 0)
        {
            return false;
        }

        var created = createdAt.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(createdAt.Value, DateTimeKind.Utc)
            : createdAt.Value.ToUniversalTime();

        age = DateTime.UtcNow - created;
        return age > TimeSpan.FromHours(maxAgeHours);
    }

    /// <summary>
    /// Marketing classification is the only one suppression applies to (PRD #17:
    /// "apply suppression for marketing messages / transactional sends continue").
    /// </summary>
    public const string MarketingClassification = "marketing";

    public static bool IsSuppressible(string? classification) =>
        string.Equals(classification, MarketingClassification, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Removes a message that can never succeed. A payload whose template key is
    /// unknown or whose required variables are missing is a producer defect —
    /// retrying it just burns the retry budget and delays real traffic, so it is
    /// recorded as "rejected" (prefixed "Invalid: ") in the delivery log and archived
    /// off the queue.
    /// </summary>
    private async Task RejectMessageAsync(
        Client supabaseClient, PgmqMessage message, EmailOutbox outboxRecord, string reason)
    {
        // 'rejected', not 'invalid' — see DeliveryStatus. A malformed payload is
        // still a permanent, deliberate non-send; the prefix keeps it separable
        // from a staleness drop.
        await LogDeliveryAttempt(
            supabaseClient, outboxRecord, message.ReadCount + 1,
            DeliveryStatus.Rejected, DeliveryStatus.InvalidReasonPrefix + reason);
        await ArchiveMessageFromPgmq(supabaseClient, message.MsgId);
        _metrics.IncrementFailed();
    }

    private async Task<bool> CheckSuppressionAsync(Client supabaseClient, string email, string? classification)
    {
        if (!IsSuppressible(classification))
        {
            return false;
        }

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
        string? errorMessage,
        string? externalId = null)
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
                ExternalId = externalId,
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

                await SendOperationalAlertAsync(
                    "Email success rate degraded",
                    $"Success rate is {_metrics.SuccessRate:P2}, below the " +
                    $"{_reliabilityConfig.SuccessRateDegradationThreshold:P2} threshold, " +
                    $"across {_metrics.TotalProcessed} processed messages.");
            }

            // Alert when dead-lettered mail is piling up unattended.
            var dlqDepth = await GetDeadLetterDepthAsync(supabaseClient);
            if (dlqDepth >= _reliabilityConfig.DeadLetterQueueSizeThreshold)
            {
                _logger.LogWarning(
                    "Dead letter queue size ({DlqCount}) exceeds threshold ({Threshold})",
                    dlqDepth, _reliabilityConfig.DeadLetterQueueSizeThreshold);

                await SendOperationalAlertAsync(
                    "Email dead-letter queue is backing up",
                    $"email_outbound_dlq holds {dlqDepth} messages, at or above the " +
                    $"{_reliabilityConfig.DeadLetterQueueSizeThreshold} alert threshold. " +
                    "Use the admin replay endpoint once the cause is fixed.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking operational alerts");
        }
    }

    private async Task<int> GetDeadLetterDepthAsync(Client supabaseClient)
    {
        try
        {
            var depth = await supabaseClient.Rpc<int>("get_email_dlq_depth", new Dictionary<string, object>());
            _metrics.SetDeadLetterDepth(depth);
            return depth;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading dead letter queue depth");
            return 0;
        }
    }

    /// <summary>
    /// Sends an operational alert to the configured address. Alerts go straight
    /// through Brevo rather than the outbox — a queue that is already failing
    /// cannot be trusted to deliver the notice that it is failing.
    /// </summary>
    private async Task SendOperationalAlertAsync(string subject, string body)
    {
        var alertEmail = _reliabilityConfig.OperationalAlertEmail;
        if (string.IsNullOrWhiteSpace(alertEmail))
        {
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            await emailService.SendEmailAsync(alertEmail, $"[EmailService] {subject}", body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send operational alert '{Subject}'", subject);
        }
    }
}

/// <summary>
/// Represents a message returned from pgmq
/// </summary>
public class PgmqMessage
{
    // read_email_queue returns snake_case columns. Without these attributes
    // Newtonsoft silently leaves MsgId and ReadCount at 0 — which meant every
    // archive call targeted message 0 and archived nothing, so no message was
    // ever removed from the queue, and read_ct never reached the retry limit.
    [JsonProperty("msg_id")]
    public long MsgId { get; set; }

    [JsonProperty("read_ct")]
    public int ReadCount { get; set; }

    [JsonProperty("enqueued_at")]
    public DateTime EnqueuedAt { get; set; }

    [JsonProperty("vt")]
    public DateTime Vt { get; set; }

    [JsonProperty("message")]
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

