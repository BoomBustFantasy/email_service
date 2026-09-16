using System.Collections.Generic;
using System.Text.Json;
using EmailService.Services;
using FluentAssertions;
using Xunit;

namespace EmailService.Tests.Services;

/// <summary>
/// Brevo rejects a send whose <c>params</c> is an empty object with HTTP 400.
/// The consumer reports that as "Email service returned false", retries the
/// identical body three times, and dead-letters the message. welcome_email is
/// the only template whose contract produces no params (the producer sends
/// <c>{}</c> by design), so every welcome email ever attempted failed this way
/// while every other template sent normally. The field must be omitted, not
/// sent empty.
/// </summary>
public class BrevoPayloadTests
{
    private const string Recipient = "user@example.com";
    private static readonly object Sender = new { name = "Boom Bust Fantasy", email = "hello@boombustfantasy.com" };

    private static JsonElement Serialize(Dictionary<string, object> payload) =>
        JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement;

    [Fact]
    public void EmptyParams_OmitsTheParamsField()
    {
        var json = Serialize(BrevoEmailService.BuildTemplatePayload(
            Sender, Recipient, 6, new Dictionary<string, string>()));

        json.TryGetProperty("params", out _).Should().BeFalse(
            "Brevo answers 400 to an empty params object");
    }

    [Fact]
    public void NullParams_OmitsTheParamsField()
    {
        var json = Serialize(BrevoEmailService.BuildTemplatePayload(
            Sender, Recipient, 6, null));

        json.TryGetProperty("params", out _).Should().BeFalse();
    }

    [Fact]
    public void PopulatedParams_AreForwardedUnchanged()
    {
        var json = Serialize(BrevoEmailService.BuildTemplatePayload(
            Sender, Recipient, 7, new Dictionary<string, string>
            {
                ["trade_id"] = "42",
                ["trade_url"] = "https://boombustfantasy.com/trades/42"
            }));

        var parameters = json.GetProperty("params");
        parameters.GetProperty("trade_id").GetString().Should().Be("42");
        parameters.GetProperty("trade_url").GetString().Should().Be("https://boombustfantasy.com/trades/42");
    }

    [Fact]
    public void RecipientTemplateAndSender_AreAlwaysPresent()
    {
        var json = Serialize(BrevoEmailService.BuildTemplatePayload(
            Sender, Recipient, 6, new Dictionary<string, string>()));

        json.GetProperty("templateId").GetInt64().Should().Be(6);
        json.GetProperty("to")[0].GetProperty("email").GetString().Should().Be(Recipient);
        json.GetProperty("sender").GetProperty("email").GetString().Should().Be("hello@boombustfantasy.com");
    }

    [Fact]
    public void NullSender_OmitsTheSenderField()
    {
        // The raw (to, templateId, params) overload sends no sender and lets the
        // Brevo template's own sender apply.
        var json = Serialize(BrevoEmailService.BuildTemplatePayload(
            null, Recipient, 6, null));

        json.TryGetProperty("sender", out _).Should().BeFalse();
    }
}
