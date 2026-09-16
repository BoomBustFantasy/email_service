using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using EmailService.Configs;
using EmailService.Templates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EmailService.Services;

public class BrevoEmailService : IEmailService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BrevoConfig _config;
    private readonly ITemplateService _templateService;
    private readonly ILogger<BrevoEmailService> _logger;

    public BrevoEmailService(
        IHttpClientFactory httpClientFactory,
        IOptions<BrevoConfig> config,
        ITemplateService templateService,
        ILogger<BrevoEmailService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config.Value;
        _templateService = templateService;
        _logger = logger;
    }

    public async Task<bool> SendEmailAsync(string to, string subject, string body, string fromDisplayName = "Boom Bust")
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("api-key", _config.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var payload = new
            {
                sender = new { name = fromDisplayName, email = _config.FromEmail },
                to = new[] { new { email = to } },
                subject = subject,
                textContent = body
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("https://api.brevo.com/v3/smtp/email", content);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Brevo API error {StatusCode}: {Error}", (int)response.StatusCode, error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", to);
            return false;
        }
    }

    public async Task<bool> SendTemplateEmailAsync(string to, long templateId, Dictionary<string, string>? templateParams = null)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("api-key", _config.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var payload = BuildTemplatePayload(sender: null, to, templateId, templateParams);

            var json = JsonSerializer.Serialize(payload);
            _logger.LogDebug("Brevo template request payload: {Json}", json);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("https://api.brevo.com/v3/smtp/email", content);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Brevo template API error {StatusCode}: {Error}", (int)response.StatusCode, error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send template email to {Email}", to);
            return false;
        }
    }

    public async Task<SendResult> SendTemplateEmailAsync(ITemplateContract contract)
    {
        try
        {
            // Validate the contract
            _templateService.ValidateContract(contract);

            // Resolve template ID from internal key
            var templateId = _templateService.ResolveTemplateId(contract.TemplateKey);

            // Get sender identity (uses override or default)
            var sender = _templateService.GetSenderIdentity(contract);

            // Convert contract to Brevo params
            var templateParams = contract.ToBrevoParams();

            var recipientEmail = contract.RecipientEmail;

            _logger.LogInformation(
                "Sending template email: Key={TemplateKey}, ID={TemplateId}, To={Recipient}, Sender={SenderName}",
                contract.TemplateKey, templateId, recipientEmail, sender.Name);

            // Call Brevo API
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("api-key", _config.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var payload = BuildTemplatePayload(
                new { name = sender.Name, email = sender.Email },
                recipientEmail,
                templateId,
                templateParams);

            var json = JsonSerializer.Serialize(payload);
            _logger.LogDebug("Brevo contract template request: {Json}", json);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("https://api.brevo.com/v3/smtp/email", content);

            if (response.IsSuccessStatusCode)
            {
                var messageId = ReadMessageId(await response.Content.ReadAsStringAsync());

                if (messageId is null)
                {
                    // Brevo accepted the send, so this is not a failure — but the row
                    // will be unreconcilable and its delivery webhook will never match.
                    _logger.LogWarning(
                        "Brevo accepted the send to {Email} but returned no messageId; " +
                        "external_id will be null and delivery webhooks cannot be matched",
                        recipientEmail);
                }

                _logger.LogInformation(
                    "Successfully sent template email to {Email} (messageId {MessageId})",
                    recipientEmail, messageId ?? "<none>");

                return SendResult.Sent(messageId);
            }

            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Brevo contract template API error {StatusCode}: {Error}", (int)response.StatusCode, error);
            return SendResult.Failed();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send contract template email for {TemplateKey}", contract.TemplateKey);
            return SendResult.Failed();
        }
    }

    /// <summary>
    /// Builds the body for POST /v3/smtp/email. Brevo rejects a template send
    /// whose <c>params</c> is empty OR absent with
    /// <c>400 {"code":"missing_parameter","message":"params is blank"}</c>
    /// (confirmed in production logs 2026-09-16). The consumer then fails the
    /// message, retries it three times, and dead-letters it. welcome_email is the
    /// only template whose contract legitimately produces no params (the producer
    /// sends <c>{}</c>), which is why every welcome email ever attempted failed
    /// while every other template sent. When the contract has nothing to
    /// substitute, <c>recipient_email</c> is sent so params is never blank.
    /// </summary>
    /// <summary>
    /// The one key sent when a contract has no params of its own. Brevo refuses a
    /// blank params object, so something must be there; the recipient address is
    /// harmless and usable from the template as <c>{{ params.recipient_email }}</c>.
    /// </summary>
    public const string FallbackParamKey = "recipient_email";

    public static Dictionary<string, object> BuildTemplatePayload(
        object? sender,
        string recipientEmail,
        long templateId,
        IReadOnlyDictionary<string, string>? templateParams)
    {
        var payload = new Dictionary<string, object>();

        if (sender is not null)
        {
            payload["sender"] = sender;
        }

        payload["to"] = new[] { new { email = recipientEmail } };
        payload["templateId"] = templateId;

        payload["params"] = templateParams is { Count: > 0 }
            ? templateParams
            : new Dictionary<string, string> { [FallbackParamKey] = recipientEmail };

        return payload;
    }

    /// <summary>
    /// Pulls the message ID out of Brevo's send response. A single-recipient send
    /// returns <c>{"messageId":"..."}</c>; the batch form returns
    /// <c>{"messageIds":["..."]}</c>. Never throws — a body we cannot parse costs
    /// reconciliation, not the send, which Brevo has already accepted.
    /// </summary>
    public static string? ReadMessageId(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("messageId", out var single) &&
                single.ValueKind == JsonValueKind.String)
            {
                var value = single.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            if (root.TryGetProperty("messageIds", out var many) &&
                many.ValueKind == JsonValueKind.Array &&
                many.GetArrayLength() > 0)
            {
                var first = many[0];
                if (first.ValueKind == JsonValueKind.String)
                {
                    var value = first.GetString();
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
