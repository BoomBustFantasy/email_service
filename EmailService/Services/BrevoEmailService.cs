using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using EmailService.Configs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EmailService.Services;

public class BrevoEmailService : IEmailService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BrevoConfig _config;
    private readonly ILogger<BrevoEmailService> _logger;

    public BrevoEmailService(IHttpClientFactory httpClientFactory, IOptions<BrevoConfig> config, ILogger<BrevoEmailService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config.Value;
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
}
