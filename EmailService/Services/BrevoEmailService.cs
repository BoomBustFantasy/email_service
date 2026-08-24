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

            var payload = new Dictionary<string, object>
            {
                ["to"] = new[] { new { email = to } },
                ["templateId"] = templateId,
                ["params"] = templateParams ?? new Dictionary<string, string>()
            };

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

            var payload = new Dictionary<string, object>
            {
                ["sender"] = new { name = sender.Name, email = sender.Email },
                ["to"] = new[] { new { email = recipientEmail } },
                ["templateId"] = templateId,
                ["params"] = templateParams
            };

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
