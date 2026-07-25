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

    public async Task<bool> SendTemplateEmailAsync(ITemplateContract contract)
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

            // Extract recipient email from params (assumes recipient_email or similar)
            if (!templateParams.TryGetValue("recipient_name", out var recipientName))
            {
                throw new ArgumentException("Template contract must provide recipient_name in params");
            }

            // Get recipient email from contract
            var recipientEmail = contract switch
            {
                Templates.Contracts.TeamReviewNotificationContract teamReview => teamReview.RecipientEmail,
                Templates.Contracts.TradeOfferNotificationContract tradeOffer => tradeOffer.RecipientEmail,
                Templates.Contracts.PurchaseConfirmationContract purchase => purchase.RecipientEmail,
                _ => throw new NotSupportedException($"Unsupported contract type: {contract.GetType().Name}")
            };

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
                _logger.LogInformation("Successfully sent template email to {Email}", recipientEmail);
                return true;
            }

            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Brevo contract template API error {StatusCode}: {Error}", (int)response.StatusCode, error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send contract template email for {TemplateKey}", contract.TemplateKey);
            return false;
        }
    }
}
