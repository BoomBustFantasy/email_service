namespace EmailService.DTOs;

/// <summary>
/// Brevo webhook event payload
/// </summary>
public class BrevoWebhookEvent
{
    public required string Event { get; set; }
    public required string Email { get; set; }
    public long? Id { get; set; }
    public string? MessageId { get; set; }
    public DateTime? Date { get; set; }
    public string? Subject { get; set; }
    public long? TemplateId { get; set; }
    public string? Reason { get; set; }
    public string? Tag { get; set; }
    public string? Ip { get; set; }
    public string? Link { get; set; }
}
