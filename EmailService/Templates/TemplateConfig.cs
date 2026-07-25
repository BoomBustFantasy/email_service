namespace EmailService.Templates;

/// <summary>
/// Configuration for template ID mapping and sender defaults
/// </summary>
public class TemplateConfig
{
    /// <summary>
    /// Maps internal template keys to Brevo numeric template IDs
    /// Example: { "team-review-notification": 1, "trade-offer": 2 }
    /// </summary>
    public Dictionary<string, long> TemplateIdMap { get; set; } = new();

    /// <summary>
    /// Default sender identity used when no template-specific override is provided
    /// </summary>
    public SenderIdentity DefaultSender { get; set; } = new SenderIdentity
    {
        Email = "noreply@boombust.app",
        Name = "Boom Bust"
    };
}
