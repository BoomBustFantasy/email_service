using Microsoft.AspNetCore.Mvc;
using EmailService.DTOs;
using EmailService.Services;
using Microsoft.Extensions.Options;
using EmailService.Configs;
using System.Text;
using System.Security.Cryptography;
using EmailService.SupabaseModels;
using Supabase;

namespace EmailService.Controllers;

/// <summary>
/// Webhook endpoint for receiving Brevo delivery events
/// </summary>
[ApiController]
[Route("webhooks")]
public class BrevoWebhookController : ControllerBase
{
    private readonly ILogger<BrevoWebhookController> _logger;
    private readonly BrevoConfig _brevoConfig;
    private readonly Client _supabaseClient;

    public BrevoWebhookController(
        ILogger<BrevoWebhookController> logger,
        IOptions<BrevoConfig> brevoConfig,
        Client supabaseClient)
    {
        _logger = logger;
        _brevoConfig = brevoConfig.Value;
        _supabaseClient = supabaseClient;
    }

    /// <summary>
    /// Receives and processes Brevo webhook events
    /// </summary>
    [HttpPost("brevo")]
    public async Task<IActionResult> HandleBrevoWebhook([FromBody] BrevoWebhookEvent webhookEvent)
    {
        try
        {
            // Validate webhook signature
            if (!ValidateWebhookSignature())
            {
                _logger.LogWarning("Brevo webhook rejected - invalid or missing signature");
                return Unauthorized("Invalid webhook signature");
            }

            _logger.LogInformation(
                "Received Brevo webhook: event={Event}, email={Email}, messageId={MessageId}",
                webhookEvent.Event,
                webhookEvent.Email,
                webhookEvent.MessageId);

            // Process the webhook event
            await ProcessWebhookEventAsync(webhookEvent);

            return Ok(new { status = "processed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Brevo webhook: {Event}", webhookEvent.Event);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    private bool ValidateWebhookSignature()
    {
        // Get the Brevo signature header
        if (!Request.Headers.TryGetValue("X-Brevo-Signature", out var signatureHeader))
        {
            return false;
        }

        // An unconfigured secret means we cannot verify anything. Reject rather
        // than accept — an unsigned webhook can create suppression records and
        // mutate delivery state.
        if (string.IsNullOrWhiteSpace(_brevoConfig.WebhookSecret))
        {
            _logger.LogError(
                "Brevo:WebhookSecret is not configured - rejecting webhook. Set it to enable signature verification.");
            return false;
        }

        // Read request body. Program.cs buffers /webhooks requests so this
        // rewind is legal after model binding has consumed the stream.
        if (!Request.Body.CanSeek)
        {
            _logger.LogError("Request body is not seekable - cannot verify webhook signature.");
            return false;
        }

        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = reader.ReadToEnd();
        Request.Body.Position = 0;

        return BrevoSignatureVerifier.IsValid(
            _brevoConfig.WebhookSecret, body, signatureHeader.ToString());
    }

    private async Task ProcessWebhookEventAsync(BrevoWebhookEvent webhookEvent)
    {
        // Update delivery log based on event type
        var status = MapEventToStatus(webhookEvent.Event);

        if (status == null)
        {
            _logger.LogDebug("Ignoring webhook event type: {Event}", webhookEvent.Event);
            return;
        }

        // Find delivery log by Brevo message ID (stored in ExternalId)
        if (!string.IsNullOrWhiteSpace(webhookEvent.MessageId))
        {
            var deliveryLogs = await _supabaseClient
                .From<EmailDeliveryLog>()
                .Where(x => x.ExternalId == webhookEvent.MessageId)
                .Get();

            if (deliveryLogs.Models.Count > 0)
            {
                var log = deliveryLogs.Models.First();
                await UpdateDeliveryLogAsync(log, webhookEvent, status);
            }
            else
            {
                _logger.LogWarning(
                    "Delivery log not found for Brevo message ID: {MessageId}",
                    webhookEvent.MessageId);
            }
        }

        // Handle suppression events (unsubscribe, bounce, spam)
        if (IsSuppression​Event(webhookEvent.Event))
        {
            await HandleSuppressionEventAsync(webhookEvent);
        }
    }

    private static string? MapEventToStatus(string eventName)
    {
        return eventName.ToLower() switch
        {
            "request" => "pending",
            "delivered" => "delivered",
            "soft_bounce" => "bounced",
            "hard_bounce" => "bounced",
            "invalid_email" => "bounced",
            "deferred" => "pending",
            "click" => "clicked",
            "opened" => "opened",
            "unique_opened" => "opened",
            "unsubscribed" => "unsubscribed",
            "complaint" => "spam",
            "blocked" => "blocked",
            "error" => "failed",
            _ => null
        };
    }

    private static bool IsSuppressionEvent(string eventName)
    {
        var suppressionEvents = new[] { "hard_bounce", "soft_bounce", "invalid_email", "unsubscribed", "complaint", "blocked" };
        return suppressionEvents.Contains(eventName.ToLower());
    }

    private async Task UpdateDeliveryLogAsync(
        EmailDeliveryLog log,
        BrevoWebhookEvent webhookEvent,
        string status)
    {
        // Update the delivery log with event details
        var update = _supabaseClient
            .From<EmailDeliveryLog>()
            .Where(x => x.Id == log.Id)
            .Set(x => x.Status!, status);

        // Add error message for failure events
        if (status == "bounced" || status == "failed" || status == "spam" || status == "blocked")
        {
            update = update.Set(x => x.ErrorMessage!, webhookEvent.Reason ?? status);
        }

        await update.Update();

        _logger.LogInformation(
            "Updated delivery log {LogId}: status={Status}, outbox_id={OutboxId}",
            log.Id,
            status,
            log.OutboxId);
    }

    /// <summary>
    /// Maps a Brevo event to a permitted <c>email_suppression.reason</c>.
    ///
    /// Brevo's event names are not the database's vocabulary — the column admits
    /// only the four values in <see cref="SuppressionReason"/>. Brevo's three
    /// delivery-failure events all collapse to <c>bounce</c>; the address could
    /// not be delivered to, and which flavour it was lives in Brevo, not here.
    ///
    /// Returns null for anything that should not suppress, so an unexpected event
    /// is ignored rather than written as a value the constraint would reject.
    /// </summary>
    public static string? MapEventToSuppressionReason(string eventName) =>
        eventName.ToLowerInvariant() switch
        {
            "unsubscribed" => SuppressionReason.Unsubscribe,
            "hard_bounce" => SuppressionReason.Bounce,
            "soft_bounce" => SuppressionReason.Bounce,
            "invalid_email" => SuppressionReason.Bounce,
            "blocked" => SuppressionReason.Bounce,
            "complaint" => SuppressionReason.Complaint,
            _ => null
        };

    private async Task HandleSuppressionEventAsync(BrevoWebhookEvent webhookEvent)
    {
        var reason = MapEventToSuppressionReason(webhookEvent.Event);

        if (reason is null)
        {
            _logger.LogWarning(
                "No suppression reason mapped for event {Event} - not suppressing {Email}",
                webhookEvent.Event, webhookEvent.Email);
            return;
        }

        // Dedupe on email alone. email_suppression has UNIQUE(email), so an
        // address suppressed for one reason cannot be inserted again under
        // another — matching on email+reason passed this check and then violated
        // the index, which is why a bounce after an unsubscribe used to 500.
        var existing = await _supabaseClient
            .From<EmailSuppression>()
            .Where(x => x.Email == webhookEvent.Email)
            .Get();

        if (existing.Models.Count > 0)
        {
            _logger.LogDebug(
                "Suppression already exists: email={Email}, existing reason={ExistingReason}, event={Event}",
                webhookEvent.Email,
                existing.Models.First().Reason,
                webhookEvent.Event);
            return;
        }

        var suppression = new EmailSuppression
        {
            Email = webhookEvent.Email,
            Reason = reason,
            SuppressionType = SuppressionType.All,
            CreatedAt = DateTime.UtcNow
        };

        await _supabaseClient
            .From<EmailSuppression>()
            .Insert(suppression);

        _logger.LogWarning(
            "Added email suppression: email={Email}, reason={Reason}, event={Event}",
            webhookEvent.Email,
            reason,
            webhookEvent.Event);
    }
}
