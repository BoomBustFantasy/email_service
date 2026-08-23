namespace EmailService.Templates.Contracts;

/// <summary>
/// Template contract for welcome emails sent when a new account is created
/// </summary>
public class WelcomeEmailContract : ITemplateContract
{
    public const string Key = "welcome_email";

    public string TemplateKey => Key;
    public SenderIdentity? SenderOverride { get; init; }

    public static ITemplateContract Create(
        string recipientEmail, IReadOnlyDictionary<string, string> variables) =>
        new WelcomeEmailContract
        {
            RecipientEmail = recipientEmail,
            RecipientName = TemplateVariables.Read(variables, "recipient_name"),
            GetStartedUrl = variables.TryGetValue("get_started_url", out var url) ? url : null
        };

    // Required template variables
    public required string RecipientEmail { get; init; }
    public required string RecipientName { get; init; }

    // Optional - link to get started or profile page
    public string? GetStartedUrl { get; init; }

    public bool Validate(out List<string> errors)
    {
        errors = new List<string>();

        if (string.IsNullOrWhiteSpace(RecipientEmail))
            errors.Add("RecipientEmail is required");

        if (string.IsNullOrWhiteSpace(RecipientName))
            errors.Add("RecipientName is required");

        // Validate email format
        if (!string.IsNullOrWhiteSpace(RecipientEmail) && !IsValidEmail(RecipientEmail))
            errors.Add($"RecipientEmail '{RecipientEmail}' is not a valid email address");

        return errors.Count == 0;
    }

    public Dictionary<string, string> ToBrevoParams()
    {
        var parameters = new Dictionary<string, string>
        {
            ["recipient_name"] = RecipientName
        };

        if (!string.IsNullOrWhiteSpace(GetStartedUrl))
        {
            parameters["get_started_url"] = GetStartedUrl;
        }

        return parameters;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}
