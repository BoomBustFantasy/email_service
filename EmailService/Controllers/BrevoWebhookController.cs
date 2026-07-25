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

        // If no webhook secret is configured, skip validation (development mode)
        if (string.IsNullOrWhiteSpace(_brevoConfig.WebhookSecret))
        {
            _logger.LogWarning("Webhook secret not configured - skipping signature validation");
            return true;
        }

        // Read request body
        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body);
        var body = reader.ReadToEnd();
        Request.Body.Position = 0;

        // Calculate HMAC-SHA256 signature
        var keyBytes = Encoding.UTF8.GetBytes(_brevoConfig.WebhookSecret);
        var bodyBytes = Encoding.UTF8.GetBytes(body);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(bodyBytes);
        var calculatedSignature = BitConverter.ToString(hash).Replace("-", "").ToLower();

        // Compare signatures
        var providedSignature = signatureHeader.ToString().ToLower();
        return calculatedSignature == providedSignature;
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

    private async Task HandleSuppressionEventAsync(BrevoWebhookEvent webhookEvent)
    {
        var reason = webhookEvent.Event.ToLower() switch
        {
            "unsubscribed" => "unsubscribe",
            "hard_bounce" => "hard_bounce",
            "soft_bounce" => "soft_bounce",
            "invalid_email" => "invalid",
            "complaint" => "spam",
            "blocked" => "blocked",
            _ => "unknown"
        };

        // Check if suppression already exists
        var existing = await _supabaseClient
            .From<EmailSuppression>()
            .Where(x => x.Email == webhookEvent.Email)
            .Where(x => x.Reason == reason)
            .Get();

        if (existing.Models.Count == 0)
        {
            // Add new suppression
            var suppression = new EmailSuppression
            {
                Email = webhookEvent.Email,
                Reason = reason,
                SuppressionType = "all",
                CreatedAt = DateTime.UtcNow
            };

            await _supabaseClient
                .From<EmailSuppression>()
                .Insert(suppression);

            _logger.LogWarning(
                "Added email suppression: email={Email}, reason={Reason}",
                webhookEvent.Email,
                reason);
        }
        else
        {
            _logger.LogDebug(
                "Suppression already exists: email={Email}, reason={Reason}",
                webhookEvent.Email,
                reason);
        }
    }
}
